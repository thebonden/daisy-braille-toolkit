namespace DAISY_Braille_Toolkit.Models
{
    public sealed class VoiceInfo
    {
        public string VoiceId { get; set; } = "";
        public string Name { get; set; } = "";

        public string Language { get; set; } = "";
        public string Accent { get; set; } = "";

        public string PreviewUrl { get; set; } = "";

        public string DisplayName
        {
            get
            {
                var lang = string.IsNullOrWhiteSpace(Language) ? "Ukendt sprog" : Language.Trim();
                var acc = string.IsNullOrWhiteSpace(Accent) ? "" : $" / {Accent.Trim()}";
                var name = string.IsNullOrWhiteSpace(Name) ? VoiceId : Name.Trim();
                return $"{name} — {lang}{acc} ({VoiceId})";
            }
        }

        public string LanguageForFilter => string.IsNullOrWhiteSpace(Language) ? "Ukendt" : Language.Trim();
        public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewUrl);
    }
}
