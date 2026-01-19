using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services
{
    public sealed class ElevenLabsApi
    {
        private readonly string _apiKey;
        private readonly HttpClient _http;

        public ElevenLabsApi(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _http = new HttpClient { BaseAddress = new Uri("https://api.elevenlabs.io/") };
            _http.DefaultRequestHeaders.Add("xi-api-key", _apiKey);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<List<VoiceInfo>> GetVoicesAsync()
        {
            using var resp = await _http.GetAsync("v1/voices").ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"ElevenLabs voices fejlede: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

            using var doc = JsonDocument.Parse(body);
            var voicesEl = doc.RootElement.GetProperty("voices");

            var list = new List<VoiceInfo>();
            foreach (var v in voicesEl.EnumerateArray())
            {
                var id = v.TryGetProperty("voice_id", out var vid) ? vid.GetString() : null;
                var name = v.TryGetProperty("name", out var nm) ? nm.GetString() : null;

                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var vi = new VoiceInfo { VoiceId = id!, Name = name ?? "" };

                if (v.TryGetProperty("preview_url", out var pu) && pu.ValueKind == JsonValueKind.String)
                    vi.PreviewUrl = pu.GetString() ?? "";

                if (string.IsNullOrWhiteSpace(vi.PreviewUrl) &&
                    v.TryGetProperty("fine_tuning", out var ft) && ft.ValueKind == JsonValueKind.Object &&
                    ft.TryGetProperty("preview_url", out var fpu) && fpu.ValueKind == JsonValueKind.String)
                {
                    vi.PreviewUrl = fpu.GetString() ?? "";
                }

                if (v.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
                {
                    if (labels.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
                        vi.Language = lang.GetString() ?? "";

                    if (labels.TryGetProperty("accent", out var acc) && acc.ValueKind == JsonValueKind.String)
                        vi.Accent = acc.GetString() ?? "";
                }

                list.Add(vi);
            }

            list.Sort((a, b) =>
            {
                var c1 = string.Compare(a.LanguageForFilter, b.LanguageForFilter, StringComparison.OrdinalIgnoreCase);
                if (c1 != 0) return c1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        public async Task<byte[]> ConvertTextToSpeechAsync(
            string voiceId,
            string text,
            string modelId,
            string outputFormat = "mp3_44100_128")
        {
            if (string.IsNullOrWhiteSpace(voiceId)) throw new ArgumentException("voiceId", nameof(voiceId));
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("text", nameof(text));
            if (string.IsNullOrWhiteSpace(modelId)) throw new ArgumentException("modelId", nameof(modelId));

            var payload = new { text = text, model_id = modelId, output_format = outputFormat };
            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/text-to-speech/{voiceId}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"ElevenLabs TTS fejlede: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");
            }

            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
    }
}
