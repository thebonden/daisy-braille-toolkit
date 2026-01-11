namespace DAISY_Braille_Toolkit.Services;

public sealed class TtsCache
{
    public string Root { get; }

    public TtsCache(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaisyBrailleToolkit",
            "tts-cache");
        Directory.CreateDirectory(Root);
    }

    public string AudioPath(string key, string ext) => Path.Combine(Root, $"{key}{ext}");
    public string JsonPath(string key) => Path.Combine(Root, $"{key}.json");

    public bool Has(string key, string ext)
        => File.Exists(AudioPath(key, ext)) && File.Exists(JsonPath(key));

    public void Put(string key, string ext, byte[] audioBytes, string rawJson)
    {
        File.WriteAllBytes(AudioPath(key, ext), audioBytes);
        File.WriteAllText(JsonPath(key), rawJson);
    }

    public void CopyToJob(string key, string ext, string jobTtsDir, int index)
    {
        Directory.CreateDirectory(jobTtsDir);

        var outAudio = Path.Combine(jobTtsDir, $"seg_{index:0000}{ext}");
        var outJson = Path.Combine(jobTtsDir, $"seg_{index:0000}.json");

        File.Copy(AudioPath(key, ext), outAudio, overwrite: true);
        File.Copy(JsonPath(key), outJson, overwrite: true);
    }
}
