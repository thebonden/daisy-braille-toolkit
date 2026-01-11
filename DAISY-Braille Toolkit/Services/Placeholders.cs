using System;
using System.Linq;
using System.Security;

namespace DAISY_Braille_Toolkit.Services;

/// <summary>
/// Placeholder output indtil vi kobler rigtig DAISY Pipeline 2 og Braille-oversættelse på.
/// </summary>
public static class DtBookPlaceholder
{
    public static string Build(string plainText, string? title, string? author, string lang)
    {
        plainText ??= string.Empty;
        lang = string.IsNullOrWhiteSpace(lang) ? "da-DK" : lang;

        var paras = plainText
            .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        static string E(string s) => SecurityElement.Escape(s) ?? "";

        var body = string.Join("\n", paras.Select(p => $"      <p>{E(p)}</p>"));

        // Klassisk verbatim interpolated string (ingen C# 11 raw strings)
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<dtbook xmlns=""http://www.daisy.org/z3986/2005/dtbook/"" xml:lang=""{E(lang)}"">
  <head>
    <meta name=""dc:Title"" content=""{E(title ?? "")}""/>
    <meta name=""dc:Creator"" content=""{E(author ?? "")}""/>
  </head>
  <book>
    <bodymatter>
{body}
    </bodymatter>
  </book>
</dtbook>
";
    }
}

public static class PefPlaceholder
{
    public static string Build(string plainText, string? title, string? author)
    {
        // Dette er IKKE rigtig punktskrift-oversættelse. Kun et placeholder PEF dokument.
        static string E(string s) => SecurityElement.Escape(s) ?? "";

        var lines = (plainText ?? "")
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();

        var rows = string.Join("\n", lines.Select(l => $"            <row>{E(l)}</row>"));

        // Klassisk verbatim interpolated string (ingen C# 11 raw strings)
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<pef xmlns=""http://www.daisy.org/ns/2008/pef"" version=""2008-1"">
  <head>
    <meta name=""dc:Title"" content=""{E(title ?? "")}""/>
    <meta name=""dc:Creator"" content=""{E(author ?? "")}""/>
  </head>
  <body>
    <volume>
      <section>
        <page>
          <row>
            <cell>PLACEHOLDER</cell>
          </row>
{rows}
        </page>
      </section>
    </volume>
  </body>
</pef>
";
    }
}
