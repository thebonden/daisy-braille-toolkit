using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DAISY_Braille_Toolkit.Services
{
    public static class TextSegmenter
    {
        // ElevenLabs docs: up to 5,000 characters per generation (paid plans).
        // We keep a small safety margin for tags/formatting differences.
        private const int DefaultApiMaxChars = 5000;
        private const int SafetyMarginChars = 200;

        /// <summary>
        /// Returns a conservative max char count to use per TTS request.
        /// </summary>
        public static int GetSafeMaxChars(string? modelId)
        {
            // Future: model-specific limits if ElevenLabs exposes them.
            return GetSafeMaxChars(DefaultApiMaxChars);
        }

        /// <summary>
        /// Returns a conservative max char count to use per TTS request.
        /// </summary>
        public static int GetSafeMaxChars(int apiMaxChars)
        {
            if (apiMaxChars <= 0) apiMaxChars = DefaultApiMaxChars;
            var safe = apiMaxChars - SafetyMarginChars;
            return Math.Max(500, safe);
        }

        /// <summary>
        /// Legacy alias used by UI/workflow code.
        /// </summary>
        public static List<string> SplitToSegments(string text, int maxChars)
            => SplitForTts(text, maxChars);

        /// <summary>
        /// Splits text into chunks suitable for TTS requests.
        /// Prefers splitting on paragraph boundaries; falls back to hard slicing.
        /// </summary>
        public static List<string> SplitForTts(string text, int maxChars)
        {
            if (maxChars < 500) maxChars = 500;
            text = NormalizeNewlines(text ?? string.Empty);

            var chunks = new List<string>();
            var current = string.Empty;

            // Paragraph split (blank lines)
            foreach (var p in Regex.Split(text, @"\n\s*\n+"))
            {
                var para = p.Trim();
                if (para.Length == 0) continue;

                if (current.Length == 0)
                {
                    current = para;
                    continue;
                }

                var candidate = current + "\n\n" + para;
                if (candidate.Length <= maxChars)
                {
                    current = candidate;
                    continue;
                }

                chunks.Add(current);
                current = para;
            }

            if (current.Length > 0)
                chunks.Add(current);

            // Ensure no chunk exceeds maxChars (hard slice)
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i].Length <= maxChars) continue;

                var longText = chunks[i];
                chunks.RemoveAt(i);

                int insertAt = i;
                for (int start = 0; start < longText.Length; start += maxChars)
                {
                    var len = Math.Min(maxChars, longText.Length - start);
                    chunks.Insert(insertAt++, longText.Substring(start, len));
                }

                i = insertAt - 1;
            }

            return chunks;
        }

        private static string NormalizeNewlines(string s)
            => (s ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
    }
}
