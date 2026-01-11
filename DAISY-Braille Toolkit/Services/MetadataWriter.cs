using CsvHelper;
using CsvHelper.Configuration;
using DAISY_Braille_Toolkit.Models;
using System.Globalization;

namespace DAISY_Braille_Toolkit.Services;

public static class MetadataWriter
{
    public static string WriteMetadataCsv(string metadataDir, JobManifest job)
    {
        Directory.CreateDirectory(metadataDir);
        var path = Path.Combine(metadataDir, "metadata.csv");

        var rows = new List<KeyValuePair<string, string>>
        {
            new("job_id", job.JobId.ToString()),
            new("created_utc", job.CreatedUtc.ToString("o")),
            new("source_file", Path.GetFileName(job.InputPath)),
            new("mode", job.Mode.ToString()),
            new("language", job.Language),
            new("title", job.Title ?? ""),
            new("author", job.Author ?? ""),
            new("tts_voice_id", job.ElevenLabsVoiceId),
            new("tts_model_id", job.Tts?.Settings?.ModelId ?? ""),
            new("tts_output_format", job.Tts?.Settings?.OutputFormat ?? "")
        };

        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        });

        csv.WriteField("key");
        csv.WriteField("value");
        csv.NextRecord();

        foreach (var (key, value) in rows)
        {
            csv.WriteField(key);
            csv.WriteField(value);
            csv.NextRecord();
        }

        return path;
    }
}
