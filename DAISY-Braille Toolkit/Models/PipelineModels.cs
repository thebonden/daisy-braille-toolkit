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
    public string SchemaVersion { get; set; } = "1.0";
    public Guid JobId { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string InputPath { get; set; } = "";
    public string OutputRoot { get; set; } = "";
    public OutputMode Mode { get; set; } = OutputMode.Both;

    // ElevenLabs
    public string ElevenLabsVoiceId { get; set; } = "";
    public string Language { get; set; } = "da-DK";

    public Dictionary<PipelineStep, StepState> Steps { get; set; } = new();
}
