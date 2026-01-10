using System.Text.Json;
using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services;

public sealed class JobStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public static string ManifestPath(string jobDir) => Path.Combine(jobDir, "job.json");

    public JobManifest CreateNew(string inputPath, string outputRoot, OutputMode mode, string voiceId)
    {
        var jobDir = Path.Combine(outputRoot, $"Job_{DateTime.Now:yyyy-MM-dd_HHmmss}");
        Directory.CreateDirectory(jobDir);

        // Output under-mapper
        Directory.CreateDirectory(Path.Combine(jobDir, "input"));
        Directory.CreateDirectory(Path.Combine(jobDir, "dtbook"));
        Directory.CreateDirectory(Path.Combine(jobDir, "tts"));
        Directory.CreateDirectory(Path.Combine(jobDir, "daisy"));
        Directory.CreateDirectory(Path.Combine(jobDir, "braille"));
        Directory.CreateDirectory(Path.Combine(jobDir, "iso"));
        Directory.CreateDirectory(Path.Combine(jobDir, "metadata"));
        Directory.CreateDirectory(Path.Combine(jobDir, "logs"));

        // Kopi af input (så job'et er reproducerbart)
        var inputCopy = Path.Combine(jobDir, "input", Path.GetFileName(inputPath));
        File.Copy(inputPath, inputCopy, overwrite: true);

        var manifest = new JobManifest
        {
            InputPath = inputCopy,
            OutputRoot = jobDir,
            Mode = mode,
            ElevenLabsVoiceId = voiceId
        };

        foreach (var step in Enum.GetValues<PipelineStep>())
            manifest.Steps[step] = new StepState();

        Save(jobDir, manifest);
        return manifest;
    }

    public JobManifest Load(string jobDir)
    {
        var json = File.ReadAllText(ManifestPath(jobDir));
        var manifest = JsonSerializer.Deserialize<JobManifest>(json, JsonOpts)
                       ?? throw new InvalidOperationException("Kunne ikke læse job.json");

        // sørg for alle steps findes (hvis schema udvides senere)
        foreach (var step in Enum.GetValues<PipelineStep>())
            manifest.Steps.TryAdd(step, new StepState());

        return manifest;
    }

    public void Save(string jobDir, JobManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        File.WriteAllText(ManifestPath(jobDir), json);
    }
}
