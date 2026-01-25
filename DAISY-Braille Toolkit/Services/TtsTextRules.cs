using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DAISY_Braille_Toolkit.Services
{
    public static class TtsTextRules
    {
        /// <summary>
        /// Applies small Danish-friendly substitutions on the TTS track only.
        /// NOTE: Do not apply this to Source text (DAISY/PEF).
        /// </summary>
        public static string ApplyDanishFixes(string input)
        {
            input ??= "";

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "st.", "stueetagen" },
                { "st", "stueetagen" },
                { "mf.", "midt for" },
                { "mf", "midt for" },
                { "th.", "til højre" },
                { "th", "til højre" },
                { "tv.", "til venstre" },
                { "tv", "til venstre" },
                { "UN", "De Forenede Nationer" }
            };

            foreach (var kv in map)
            {
                // Replace tokens as standalone words: avoid changing inside longer words.
                var pattern = $@"(?<!\w){Regex.Escape(kv.Key)}(?!\w)";
                input = Regex.Replace(input, pattern, kv.Value, RegexOptions.IgnoreCase);
            }

            // Clock times: 09:30 -> "klokken 09 30"
            input = Regex.Replace(input, @"\b([01]?\d|2[0-3]):([0-5]\d)\b", "klokken $1 $2");

            return input;
        }
    }
}
