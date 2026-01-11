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

        var sb = new StringBuilder();

        foreach (var para in body.Elements<Paragraph>())
        {
            var text = para.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }
}
