using System.Net.Http;
using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services;

public sealed class PipelineRunner
{
    private readonly JobStore _store = new();
    private readonly TtsCache _ttsCache = new();

    private static readonly HttpClient Http = new();

    public async Task RunAsync(
        JobManifest job,
        PipelineStep? forceStartAt,
        Action<string> log,
        Action<double, string> progress,
        string? elevenLabsApiKey,
        CancellationToken ct = default)
    {
        var steps = GetPlan(job.Mode);

        // Find start-step: Auto = første NotStarted/Failed
        var startIndex = 0;
        if (forceStartAt is null)
        {
            startIndex = steps.FindIndex(s =>
                job.Steps[s].Status is StepStatus.NotStarted or StepStatus.Failed);

            if (startIndex < 0)
            {
                progress(1, "All done");
                return;
            }
        }
        else
        {
            startIndex = steps.IndexOf(forceStartAt.Value);
            if (startIndex < 0) startIndex = 0;

            // Hvis man tvinger start fra et step, så nulstil efterfølgende steps
            for (var i = startIndex; i < steps.Count; i++)
                job.Steps[steps[i]] = new StepState();

            _store.Save(job.OutputRoot, job);
        }

        for (var i = startIndex; i < steps.Count; i++)
        {
            var step = steps[i];
            var pct = (double)i / steps.Count;

            await RunStep(step, job, elevenLabsApiKey, log, msg => progress(pct, msg), ct);
            _store.Save(job.OutputRoot, job);
        }

        progress(1, "Færdig");
    }

    private static List<PipelineStep> GetPlan(OutputMode mode)
    {
        var plan = new List<PipelineStep> { PipelineStep.Import, PipelineStep.DtBook, PipelineStep.Tts };

        if (mode is OutputMode.DaisyOnly or OutputMode.Both)
            plan.Add(PipelineStep.DaisyBuild);

        if (mode is OutputMode.BrailleOnly or OutputMode.Both)
            plan.Add(PipelineStep.PefBuild);

        plan.Add(PipelineStep.IsoAndCsv);
        return plan;
    }

    private async Task RunStep(PipelineStep step, JobManifest job, string? elevenLabsApiKey, Action<string> log, Action<string> status, CancellationToken ct)
    {
        var state = job.Steps[step];
        state.Status = StepStatus.Running;
        state.StartedUtc = DateTime.UtcNow;
        state.Error = null;

        try
        {
            status($"Kører: {step}");
            log($"== {step} ==");

            switch (step)
            {
                case PipelineStep.Import:
                    DoImport(job, log);
                    break;

                case PipelineStep.DtBook:
                    await DoDtBookAsync(job, log, ct);
                    break;

                case PipelineStep.Tts:
                    await DoTtsAsync(job, elevenLabsApiKey, log, status, ct);
                    break;

                case PipelineStep.DaisyBuild:
                    await DoDaisyPlaceholderAsync(job, log, ct);
                    break;

                case PipelineStep.PefBuild:
                    await DoPefPlaceholderAsync(job, log, ct);
                    break;

                case PipelineStep.IsoAndCsv:
                    DoIsoAndCsv(job, log);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(step), step, null);
            }

            state.Status = StepStatus.Completed;
            state.FinishedUtc = DateTime.UtcNow;
            log($"OK: {step}");
        }
        catch (Exception ex)
        {
            state.Status = StepStatus.Failed;
            state.FinishedUtc = DateTime.UtcNow;
            state.Error = ex.ToString();
            log($"FEJL: {step}\n{ex}");
            throw;
        }
    }

    private static void DoImport(JobManifest job, Action<string> log)
    {
        if (!File.Exists(job.InputPath))
            throw new FileNotFoundException("Job inputfil mangler.", job.InputPath);

        // JobStore har allerede kopieret input til job/input.
        log($"Input: {job.InputPath}");
    }

    private static async Task DoDtBookAsync(JobManifest job, Action<string> log, CancellationToken ct)
    {
        var dtbookDir = Path.Combine(job.OutputRoot, "dtbook");
        Directory.CreateDirectory(dtbookDir);

        var text = DocumentTextExtractor.ExtractText(job.InputPath);
        var outText = Path.Combine(dtbookDir, "fulltext.txt");
        await File.WriteAllTextAsync(outText, text, ct);
        log($"Skrev: {outText} ({text.Length} tegn)");
    }


    private static List<string>? TryLoadEditorSegments(JobManifest job, Action<string> log)
    {
        try
        {
            var segDir = Path.Combine(job.OutputRoot, "segments");
            if (!Directory.Exists(segDir)) return null;

            // Prefer TTS track edits (avoid API calls for preview; this is local text files)
            var ttsFiles = Directory.EnumerateFiles(segDir, "*.tts.txt", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ttsFiles.Count == 0) return null;

            var texts = new List<string>();
            foreach (var ttsPath in ttsFiles)
            {
                var t = File.ReadAllText(ttsPath).Trim();
                if (string.IsNullOrWhiteSpace(t))
                {
                    var sourcePath = ttsPath.Replace(".tts.txt", ".source.txt");
                    if (File.Exists(sourcePath))
                        t = File.ReadAllText(sourcePath).Trim();
                }

                if (!string.IsNullOrWhiteSpace(t))
                    texts.Add(t);
            }

            if (texts.Count == 0) return null;

            log($"Loaded {texts.Count} segment(s) from TTS editor: {segDir}");
            return texts;
        }
        catch (Exception ex)
        {
            log("Could not load editor segments: " + ex.Message);
            return null;
        }
    }

    private async Task DoTtsAsync(JobManifest job, string? apiKey, Action<string> log, Action<string> status, CancellationToken ct)
    {
        apiKey ??= Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs API key mangler. Sæt miljøvariablen ELEVENLABS_API_KEY eller indsæt den i UI.");

        var dtbookTextPath = Path.Combine(job.OutputRoot, "dtbook", "fulltext.txt");
        if (!File.Exists(dtbookTextPath))
            throw new FileNotFoundException("Mangler dtbook/fulltext.txt (kør DtBook step først).", dtbookTextPath);

        job.Tts ??= new TtsJobState();

        var settings = job.Tts.Settings;
        var text = await File.ReadAllTextAsync(dtbookTextPath, ct);

        // Normalize max chars per segment (defaults depend on model)
        if (settings.MaxCharsPerSegment <= 0)
            settings.MaxCharsPerSegment = TextSegmenter.GetSafeMaxChars(settings.ModelId);

        if (job.Tts.Segments.Count == 0)
        {
            // If the user already built/edited segments in the GUI (jobFolder/segments), use those.
            var editorSegs = TryLoadEditorSegments(job, log);

            var segTexts = new List<string>();
            if (editorSegs != null)
            {
                foreach (var t in editorSegs)
                {
                    if (t.Length > settings.MaxCharsPerSegment)
                        segTexts.AddRange(TextSegmenter.SplitForTts(t, settings.MaxCharsPerSegment));
                    else
                        segTexts.Add(t);
                }
            }
            else
            {
                segTexts = TextSegmenter.SplitForTts(text, settings.MaxCharsPerSegment).ToList();
            }

            job.Tts.Segments = segTexts.Select((t, i) => new TtsSegment
            {
                Index = i + 1,
                Text = t,
                CacheKey = ElevenLabsClient.ComputeCacheKey(job.ElevenLabsVoiceId, settings.ModelId, settings.OutputFormat, t)
            }).ToList();

            _store.Save(job.OutputRoot, job);
            log($"Built {job.Tts.Segments.Count} TTS segment(s) (max {settings.MaxCharsPerSegment} chars per segment)");
        }

        var ext = GuessAudioExtension(settings.OutputFormat);
        var jobTtsDir = Path.Combine(job.OutputRoot, "tts");
        Directory.CreateDirectory(jobTtsDir);

        var client = new ElevenLabsClient(Http);

        for (var i = 0; i < job.Tts.Segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var seg = job.Tts.Segments[i];

            var outAudio = Path.Combine(jobTtsDir, $"seg_{seg.Index:0000}{ext}");
            var outJson = Path.Combine(jobTtsDir, $"seg_{seg.Index:0000}.json");

            // Allerede genereret i job-mappen?
            if (seg.Status == StepStatus.Completed && File.Exists(outAudio) && File.Exists(outJson))
            {
                status($"TTS: {i + 1}/{job.Tts.Segments.Count} (genbruger job output)");
                continue;
            }

            // Genbrug cache?
            if (_ttsCache.Has(seg.CacheKey, ext))
            {
                _ttsCache.CopyToJob(seg.CacheKey, ext, jobTtsDir, seg.Index);
                seg.Status = StepStatus.Completed;
                seg.Error = null;
                _store.Save(job.OutputRoot, job);
                status($"TTS: {i + 1}/{job.Tts.Segments.Count} (cache)");
                continue;
            }

            // Kald API
            status($"TTS: {i + 1}/{job.Tts.Segments.Count} (API)");
            log($"TTS segment {seg.Index}: {seg.Text.Length} tegn");

            seg.Status = StepStatus.Running;
            seg.Error = null;
            _store.Save(job.OutputRoot, job);

            try
            {
                var resp = await client.ConvertWithTimestampsAsync(
                    apiKey,
                    job.ElevenLabsVoiceId,
                    seg.Text,
                    settings.ModelId,
                    settings.OutputFormat,
                    ct);

                _ttsCache.Put(seg.CacheKey, ext, resp.AudioBytes, resp.RawJson);
                _ttsCache.CopyToJob(seg.CacheKey, ext, jobTtsDir, seg.Index);

                seg.Status = StepStatus.Completed;
                seg.Error = null;
                _store.Save(job.OutputRoot, job);
            }
            catch (Exception ex)
            {
                seg.Status = StepStatus.Failed;
                seg.Error = ex.Message;
                _store.Save(job.OutputRoot, job);
                throw;
            }
        }
    }

    private static string GuessAudioExtension(string outputFormat)
    {
        outputFormat = (outputFormat ?? "").ToLowerInvariant();
        if (outputFormat.StartsWith("mp3")) return ".mp3";
        if (outputFormat.StartsWith("wav")) return ".wav";
        if (outputFormat.StartsWith("pcm")) return ".pcm";
        return ".bin";
    }

    private static async Task DoDaisyPlaceholderAsync(JobManifest job, Action<string> log, CancellationToken ct)
    {
        var daisyDir = Path.Combine(job.OutputRoot, "daisy");
        var audioDir = Path.Combine(daisyDir, "audio");
        Directory.CreateDirectory(audioDir);

        // Kopiér TTS-audio ind i DAISY-mappen (placeholder struktur)
        var ttsDir = Path.Combine(job.OutputRoot, "tts");
        if (Directory.Exists(ttsDir))
        {
            foreach (var file in Directory.EnumerateFiles(ttsDir, "seg_*.*"))
            {
                if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(audioDir, name), overwrite: true);
            }
        }

        // Placeholder DTBook XML
        var textPath = Path.Combine(job.OutputRoot, "dtbook", "fulltext.txt");
        var text = File.Exists(textPath) ? await File.ReadAllTextAsync(textPath, ct) : "";

        var xml = DtBookPlaceholder.Build(text, job.Title, job.Author, job.Language);
        var dtbookOut = Path.Combine(daisyDir, "dtbook.xml");
        await File.WriteAllTextAsync(dtbookOut, xml, ct);

        // Enkel index-fil
        var indexTxt = Path.Combine(daisyDir, "index.txt");
        await File.WriteAllTextAsync(indexTxt, "Placeholder DAISY output. (SMIL/OPF kommer senere)\n", ct);

        log($"DAISY placeholder skrevet til: {daisyDir}");
    }

    private static async Task DoPefPlaceholderAsync(JobManifest job, Action<string> log, CancellationToken ct)
    {
        var brailleDir = Path.Combine(job.OutputRoot, "braille");
        Directory.CreateDirectory(brailleDir);

        var textPath = Path.Combine(job.OutputRoot, "dtbook", "fulltext.txt");
        var text = File.Exists(textPath) ? await File.ReadAllTextAsync(textPath, ct) : "";

        var pef = PefPlaceholder.Build(text, job.Title, job.Author);
        var outPef = Path.Combine(brailleDir, "book.pef");
        await File.WriteAllTextAsync(outPef, pef, ct);

        log($"PEF placeholder skrevet: {outPef}");
    }

    private static void DoIsoAndCsv(JobManifest job, Action<string> log)
    {
        var metaDir = Path.Combine(job.OutputRoot, "metadata");
        var isoDir = Path.Combine(job.OutputRoot, "iso");

        var csvPath = MetadataWriter.WriteMetadataCsv(metaDir, job);
        log($"Skrev CSV: {csvPath}");

        var label = string.IsNullOrWhiteSpace(job.Title) ? "DAISY" : job.Title!;
        var isoPath = IsoBuilder.BuildIso(isoDir, job, label);
        log($"Skrev ISO: {isoPath}");
    }
}
