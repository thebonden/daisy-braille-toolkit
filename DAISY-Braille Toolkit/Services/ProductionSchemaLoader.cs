using System.Text;

namespace DAISY_Braille_Toolkit.Services
{
    /// <summary>
    /// Loads the Production CSV column schema from the shipped file: Data/metadata daisy.txt.
    /// This file is considered the "single source of truth" for which columns the exported CSV must contain.
    /// </summary>
    public static class ProductionSchemaLoader
    {
        private static readonly string[] FallbackColumns =
        {
            "Date",
            "Title",
            "Language",
            "Model",
            "Voice",
            "VoiceId",
            "SourceFile",
            "JobFolder",
            "Notes"
        };

        public static string DefaultSchemaPath()
            => Path.Combine(AppContext.BaseDirectory, "Data", "metadata daisy.txt");

        public static IReadOnlyList<string> LoadColumns(string? schemaPath = null)
        {
            schemaPath ??= DefaultSchemaPath();

            try
            {
                if (!File.Exists(schemaPath))
                    return FallbackColumns;

                var lines = File.ReadAllLines(schemaPath, Encoding.UTF8);
                var cols = new List<string>();

                foreach (var raw in lines)
                {
                    var line = (raw ?? string.Empty).Trim();
                    if (line.Length == 0) continue;

                    // Stop when we hit notes/examples in the file.
                    if (line.StartsWith("nogle af data", StringComparison.OrdinalIgnoreCase)) break;
                    if (line.StartsWith("alle data", StringComparison.OrdinalIgnoreCase)) break;
                    if (line.StartsWith("<meta", StringComparison.OrdinalIgnoreCase)) break;

                    // Skip heading line
                    if (line.StartsWith("Data der skal i CSV filen", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = ExtractColumnName(line);
                    if (name.Length > 0)
                        cols.Add(name);
                }

                // Uniq + preserve order
                var uniq = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in cols)
                {
                    if (seen.Add(c)) uniq.Add(c);
                }

                return uniq.Count > 0 ? uniq : FallbackColumns;
            }
            catch
            {
                return FallbackColumns;
            }
        }

        private static string ExtractColumnName(string line)
        {
            // Keep the human-friendly Danish column names, but strip inline explanations.
            var idx = line.IndexOf('(');
            if (idx > 0) line = line[..idx];

            var ex = line.IndexOf("exempel", StringComparison.OrdinalIgnoreCase);
            if (ex > 0) line = line[..ex];

            return line.Trim().TrimEnd(':');
        }
    }
}
