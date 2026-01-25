using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DAISY_Braille_Toolkit.Services;

public static class DocumentTextExtractor
{
    public static string ExtractText(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input file not found.", inputPath);

        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        return ext switch
        {
            ".txt" => File.ReadAllText(inputPath, Encoding.UTF8),
            ".docx" => ExtractDocx(inputPath),
            _ => throw new NotSupportedException($"Unsupported input type: {ext}. Only .txt and .docx are supported.")
        };
    }

    private static string ExtractDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        // Build a lookup of styleId -> human readable name (helps with localized Word installations).
        var styleIdToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var styles = doc.MainDocumentPart?.StyleDefinitionsPart?.Styles;
        if (styles != null)
        {
            foreach (var s in styles.Elements<Style>())
            {
                var id = s.StyleId?.Value;
                var name = s.StyleName?.Val?.Value;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    styleIdToName[id] = name;
            }
        }

        var sb = new StringBuilder();

        foreach (var para in body.Elements<Paragraph>())
        {
            var text = para.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var headingLevel = TryGetHeadingLevel(para, styleIdToName);
                if (headingLevel is >= 1 and <= 4)
                {
                    var level = headingLevel.Value;
                    // Markdown-style headings are easy to parse later for DAISY navigation.
                    sb.AppendLine(new string('#', level) + " " + text);
                }
                else
                {
                    sb.AppendLine(text);
                }
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static int? TryGetHeadingLevel(Paragraph para, Dictionary<string, string> styleIdToName)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId)) return null;

        // Common style IDs are non-localized (Heading1, Heading2...), but some templates may use other ids.
        var level = HeadingLevelFromToken(styleId);
        if (level != null) return level;

        if (styleIdToName.TryGetValue(styleId, out var styleName))
        {
            level = HeadingLevelFromToken(styleName);
            if (level != null) return level;
        }

        return null;
    }

    private static int? HeadingLevelFromToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        // Normalize: strip spaces/hyphens to catch things like "Heading 1" / "Overskrift 1".
        var t = new string(token.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray());
        t = t.ToLowerInvariant();

        // English/Danish (common): heading1..4, overskrift1..4
        for (var i = 1; i <= 4; i++)
        {
            if (t == $"heading{i}" || t.StartsWith($"heading{i}") || t == $"overskrift{i}" || t.StartsWith($"overskrift{i}"))
                return i;

            // "heading{i}" as part of a longer name is also acceptable.
            if (t.Contains($"heading{i}") || t.Contains($"overskrift{i}"))
                return i;
        }

        return null;
    }
}
