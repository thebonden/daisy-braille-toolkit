using DiscUtils.Iso9660;
using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services;

public static class IsoBuilder
{
    public static string BuildIso(string isoDir, JobManifest job, string label = "DAISY")
    {
        Directory.CreateDirectory(isoDir);
        var isoPath = Path.Combine(isoDir, "output.iso");

        var builder = new CDBuilder
        {
            UseJoliet = true,
            VolumeIdentifier = SanitizeLabel(label)
        };

        // Inkludér mapper hvis de findes
        AddDirectoryIfExists(builder, job.OutputRoot, "daisy");
        AddDirectoryIfExists(builder, job.OutputRoot, "braille");
        AddDirectoryIfExists(builder, job.OutputRoot, "metadata");

        builder.Build(isoPath);
        return isoPath;
    }

    private static void AddDirectoryIfExists(CDBuilder builder, string root, string name)
    {
        var dir = Path.Combine(root, name);
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            builder.AddFile($"{name}/{rel}", file);
        }
    }

    private static string SanitizeLabel(string label)
    {
        // ISO9660 label: hold den enkel
        label = new string(label.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(label))
            label = "DAISY";
        return label.Length <= 32 ? label : label[..32];
    }
}
