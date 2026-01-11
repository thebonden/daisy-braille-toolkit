using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DAISY_Braille_Toolkit.Services;

public sealed class ElevenLabsClient
{
    private readonly HttpClient _http;

    public ElevenLabsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ElevenLabsResponse> ConvertWithTimestampsAsync(
        string apiKey,
        string voiceId,
        string text,
        string modelId,
        string outputFormat,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs API key mangler.");
        if (string.IsNullOrWhiteSpace(voiceId))
            throw new InvalidOperationException("ElevenLabs Voice ID mangler.");
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Tekst til TTS er tom.");

        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}/with-timestamps?output_format={Uri.EscapeDataString(outputFormat)}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("xi-api-key", apiKey);

        var payload = new
        {
            text,
            model_id = modelId,
            voice_settings = new
            {
                stability = 0.5,
                similarity_boost = 0.8,
                style = 0.0,
                use_speaker_boost = true
            }
        };

        var json = JsonSerializer.Serialize(payload);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"ElevenLabs fejl ({(int)res.StatusCode}): {body}");

        // Gem hele JSON'en (alignment + audio)
        var audioBase64 = TryGetAudioBase64(body)
            ?? throw new InvalidOperationException("ElevenLabs response indeholder ikke 'audio_base64'.");

        var audioBytes = Convert.FromBase64String(audioBase64);

        return new ElevenLabsResponse
        {
            RawJson = body,
            AudioBytes = audioBytes
        };
    }

    private static string? TryGetAudioBase64(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Primære feltnavne (fra ElevenLabs docs/postman)
        if (root.TryGetProperty("audio_base64", out var a) && a.ValueKind == JsonValueKind.String)
            return a.GetString();

        // Fallbacks (hvis schema ændrer sig)
        if (root.TryGetProperty("audio", out var b) && b.ValueKind == JsonValueKind.String)
            return b.GetString();
        if (root.TryGetProperty("audioBase64", out var c) && c.ValueKind == JsonValueKind.String)
            return c.GetString();

        return null;
    }

    public static string ComputeCacheKey(string voiceId, string modelId, string outputFormat, string text)
    {
        var input = $"voice:{voiceId}\nmodel:{modelId}\nformat:{outputFormat}\n{text}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class ElevenLabsResponse
{
    public required byte[] AudioBytes { get; init; }
    public required string RawJson { get; init; }
}
