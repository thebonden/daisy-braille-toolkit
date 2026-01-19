using System;
using System.Globalization;

namespace DAISY_Braille_Toolkit.Models
{
    public class ProductionDataRow
    {
        public string Timestamp { get; set; } = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        public string Title { get; set; } = "";
        public string Language { get; set; } = "";
        public string ModelId { get; set; } = "";
        public string VoiceName { get; set; } = "";
        public string VoiceId { get; set; } = "";
        public string SourceFile { get; set; } = "";
        public string JobFolder { get; set; } = "";
        public string Notes { get; set; } = "";

        public static string CsvHeader => "Timestamp,Title,Language,ModelId,VoiceName,VoiceId,SourceFile,JobFolder,Notes";

        public string ToCsvRow()
        {
            return string.Join(",",
                Escape(Timestamp),
                Escape(Title),
                Escape(Language),
                Escape(ModelId),
                Escape(VoiceName),
                Escape(VoiceId),
                Escape(SourceFile),
                Escape(JobFolder),
                Escape(Notes));
        }

        private static string Escape(string? value)
        {
            value ??= string.Empty;
            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (value.Contains('"'))
            {
                value = value.Replace("\"", "\"\"");
            }
            return needsQuotes ? $"\"{value}\"" : value;
        }
    }
}
