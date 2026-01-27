namespace DAISY_Braille_Toolkit.Models
{
    public class AppSettings
    {
        public string UiLanguage { get; set; } = "en";

        // SharePoint / Microsoft Lists (optional)
        // Provisioning scripts create the required lists. These settings tell the app where to read/write.
        public bool SharePointEnabled { get; set; } = false;
        public string SharePointSiteUrl { get; set; } = "";          // e.g. https://tenant.sharepoint.com/sites/YourSite
        public string SharePointTenantId { get; set; } = "";         // Entra ID tenant GUID
        public string SharePointClientId { get; set; } = "";         // App registration (public client) GUID
        public string SharePointCountersList { get; set; } = "DBT_Counters";
        public string SharePointProductionsList { get; set; } = "DBT_Productions";

        // Volume label / Production ID rules
        public string VolumeLabelPrefix { get; set; } = "";          // e.g. DBS25
        public int SequenceDigits { get; set; } = 3;                  // 3 => 000..999

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
