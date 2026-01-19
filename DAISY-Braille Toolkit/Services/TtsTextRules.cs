using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DAISY_Braille_Toolkit.Services
{
    public static class TtsTextRules
    {
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
                input = Regex.Replace(input, $@"{Regex.Escape(kv.Key)}", kv.Value, RegexOptions.IgnoreCase);
            }

            input = Regex.Replace(input, @"([01]?\d|2[0-3]):([0-5]\d)", "klokken $1 $2");

            return input;
        }
    }
}
