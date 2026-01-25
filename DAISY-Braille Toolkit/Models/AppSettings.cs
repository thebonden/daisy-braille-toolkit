namespace DAISY_Braille_Toolkit.Models
{
    public class AppSettings
    {
        public string UiLanguage { get; set; } = "en";

        // Default ElevenLabs selections
        public string ElevenLabsModelId { get; set; } = "eleven_multilingual_v2";
        public string ElevenLabsVoiceId { get; set; } = "";

        // General behavior
        public bool DeleteTempFilesWhenJobFinished { get; set; } = true;

        // Production data
        public string ProductionCsvPath { get; set; } = "";

        // Backward-compatible aliases (older code names)
        public string DefaultModel { get => ElevenLabsModelId; set => ElevenLabsModelId = value; }
        public string DefaultVoiceId { get => ElevenLabsVoiceId; set => ElevenLabsVoiceId = value; }
        public bool CleanupTempAfterJob { get => DeleteTempFilesWhenJobFinished; set => DeleteTempFilesWhenJobFinished = value; }
    }
}
