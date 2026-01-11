using System.Text.Json.Serialization;

namespace DAISY_Braille_Toolkit.Models;

public enum OutputMode
{
    DaisyOnly = 0,
    BrailleOnly = 1,
    Both = 2
}

public enum StepStatus
{
    NotStarted,
    Running,
    Completed,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineStep
{
    Import,
    DtBook,
    Tts,
    DaisyBuild,
    PefBuild,
    IsoAndCsv
}

public sealed class StepState
{
    public StepStatus Status { get; set; } = StepStatus.NotStarted;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class JobManifest
{
    public string SchemaVersion { get; set; } = "1.1";
    public Guid JobId { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string InputPath { get; set; } = "";
    public string OutputRoot { get; set; } = "";
    public OutputMode Mode { get; set; } = OutputMode.Both;

    // Metadata (til CSV / forside)
    public string? Title { get; set; }
    public string? Author { get; set; }

    // ElevenLabs
    public string ElevenLabsVoiceId { get; set; } = "";
    public string Language { get; set; } = "da-DK";

    // TTS-resume (segmenter + settings). Gemmes i job.json så man kan fortsætte uden at betale igen.
    public TtsJobState? Tts { get; set; }

    public Dictionary<PipelineStep, StepState> Steps { get; set; } = new();
}

public sealed class TtsJobState
{
    public TtsSettings Settings { get; set; } = new();
    public List<TtsSegment> Segments { get; set; } = new();
}

public sealed class TtsSettings
{
    public string ModelId { get; set; } = "eleven_multilingual_v2";
    public string OutputFormat { get; set; } = "mp3_44100_128";
    public int MaxCharsPerSegment { get; set; } = 1500;
}

public sealed class TtsSegment
{
    public int Index { get; set; }
    public string Text { get; set; } = "";

    // Hash til cache (voice+model+format+text)
    public string CacheKey { get; set; } = "";

    public StepStatus Status { get; set; } = StepStatus.NotStarted;
    public string? Error { get; set; }
}
