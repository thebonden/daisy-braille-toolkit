namespace DAISY_Braille_Toolkit.Services;

public static class TextSegmentation
{
    /// <summary>
    /// Splitter tekst til TTS-venlige bidder.
    /// - Splitter på tomme linjer (afsnit)
    /// - Samler flere afsnit i en segment, indtil maxChars
    /// </summary>
    public static List<string> SplitIntoSegments(string text, int maxChars = 1500)
    {
        text ??= string.Empty;
        text = Normalize(text);

        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        // Paragraphs
        var paras = text
            .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var segments = new List<string>();
        var current = "";

        foreach (var p in paras)
        {
            if (current.Length == 0)
            {
                current = p;
                continue;
            }

            // +2 for paragraph break
            if (current.Length + 2 + p.Length <= maxChars)
            {
                current += "\n\n" + p;
            }
            else
            {
                segments.Add(current);
                current = p;
            }
        }

        if (current.Length > 0)
            segments.Add(current);

        // Fallback: hvis et enkelt afsnit er ekstremt langt, så klip hårdt.
        var normalized = new List<string>();
        foreach (var seg in segments)
        {
            if (seg.Length <= maxChars)
            {
                normalized.Add(seg);
                continue;
            }

            for (var i = 0; i < seg.Length; i += maxChars)
                normalized.Add(seg.Substring(i, Math.Min(maxChars, seg.Length - i)));
        }

        return normalized;
    }

    private static string Normalize(string text)
    {
        // ensartede linjeskift
        text = text.Replace("\r\n", "\n");
        // trim whitespace per linje
        var lines = text.Split('\n').Select(l => l.TrimEnd());
        return string.Join("\n", lines).Trim();
    }
}
