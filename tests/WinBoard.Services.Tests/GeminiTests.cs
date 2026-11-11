using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using WinBoard.Services.Gemini;

namespace WinBoard.Services.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
    public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return Responses.Dequeue()();
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Ok(string text) =>
        Json(HttpStatusCode.OK, new JsonObject
        {
            ["candidates"] = new JsonArray(new JsonObject
            {
                ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) }
            })
        }.ToJsonString());
}

public class GeminiTests
{
    private static (GeminiClient, FakeHandler, GeminiOptions) Make(Action<GeminiOptions>? cfg = null, IUsageStore? store = null, TimeProvider? time = null)
    {
        var h = new FakeHandler();
        var o = new GeminiOptions { ApiKey = "cle-test" };
        cfg?.Invoke(o);
        return (new GeminiClient(new HttpClient(h), o, store, time), h, o);
    }

    [Fact]
    public async Task Transcribe_BuildsExpectedRequest()
    {
        var (c, h, _) = Make();
        h.Responses.Enqueue(() => FakeHandler.Ok("Bonjour."));
        var wav = WavEncoder.Encode(new short[] { 1, 2, 3 }, 16000, 1);

        var text = await c.TranscribeAsync(wav, "fr", default);

        Assert.Equal("Bonjour.", text);
        var (req, body) = h.Calls.Single();
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", req.RequestUri!.ToString());
        Assert.Equal("cle-test", req.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("cle-test", req.RequestUri.ToString());
        Assert.DoesNotContain("cle-test", body);
        var json = JsonNode.Parse(body)!;
        var inline = json["contents"]![0]!["parts"]![0]!["inline_data"]!;
        Assert.Equal("audio/wav", inline["mime_type"]!.GetValue<string>());
        Assert.Equal(wav, Convert.FromBase64String(inline["data"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(json["system_instruction"]!["parts"]![0]!["text"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Rewrite_ConcatenatesTextParts_AndCustomModel()
    {
        var (c, h, _) = Make(o => o.Model = "gemini-x");
        h.Responses.Enqueue(() => FakeHandler.Json(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"- un"},{"text":"\n- deux"}]}}]}"""));
        var r = await c.RewriteTextAsync("euh un deux", DictationStyle.BulletPoints, "fr", default);
        Assert.Equal("- un\n- deux", r);
        Assert.Contains("models/gemini-x:generateContent", h.Calls[0].Request.RequestUri!.ToString());
        Assert.Contains("euh un deux", h.Calls[0].Body);
    }

    [Fact]
    public async Task Refine_PromptMentionsSelfCorrections()
    {
        var (c, h, _) = Make();
        h.Responses.Enqueue(() => FakeHandler.Ok("ok"));
        await c.RefineDictationAsync(WavEncoder.Encode(new short[4], 16000, 1), "fr", DictationStyle.Clean, default);
        Assert.Contains("self-correction", h.Calls[0].Body);
    }

    [Fact]
    public async Task Http429_ThenSuccess_RetriesOnce()
    {
        var (c, h, _) = Make();
        h.Responses.Enqueue(() =>
        {
            var r = FakeHandler.Json((HttpStatusCode)429, """{"error":{"code":429,"message":"x","status":"RESOURCE_EXHAUSTED"}}""");
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return r;
        });
        h.Responses.Enqueue(() => FakeHandler.Ok("fin"));
        Assert.Equal("fin", await c.RewriteTextAsync("a", DictationStyle.Clean, "fr", default));
        Assert.Equal(2, h.Calls.Count);
    }

    [Fact]
    public async Task Http429_Twice_ThrowsQuota()
    {
        var (c, h, _) = Make();
        for (int i = 0; i < 2; i++)
            h.Responses.Enqueue(() => FakeHandler.Json((HttpStatusCode)429,
                """{"error":{"code":429,"message":"quota","details":[{"retryDelay":"0s"}]}}"""));
        var ex = await Assert.ThrowsAsync<GeminiQuotaException>(() => c.RewriteTextAsync("a", DictationStyle.Clean, "fr", default));
        Assert.False(ex.IsLocal);
        Assert.Equal(2, h.Calls.Count);
    }

    [Fact]
    public async Task LocalMinuteQuota_ThrowsWithoutNetwork()
    {
        var (c, h, _) = Make(o => o.RequestsPerMinute = 2);
        h.Responses.Enqueue(() => FakeHandler.Ok("a"));
        h.Responses.Enqueue(() => FakeHandler.Ok("b"));
        await c.RewriteTextAsync("1", DictationStyle.Clean, "fr", default);
        await c.RewriteTextAsync("2", DictationStyle.Clean, "fr", default);
        var ex = await Assert.ThrowsAsync<GeminiQuotaException>(() => c.RewriteTextAsync("3", DictationStyle.Clean, "fr", default));
        Assert.True(ex.IsLocal);
        Assert.Equal(2, h.Calls.Count);
        Assert.Equal(0, c.GetUsage().MinuteRemaining);
    }

    [Fact]
    public async Task LocalDailyQuota_PersistsAcrossClients()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var (c1, h1, _) = Make(o => o.RequestsPerDay = 1, new FileUsageStore(path));
            h1.Responses.Enqueue(() => FakeHandler.Ok("a"));
            await c1.RewriteTextAsync("1", DictationStyle.Clean, "fr", default);

            var (c2, h2, _) = Make(o => o.RequestsPerDay = 1, new FileUsageStore(path));
            await Assert.ThrowsAsync<GeminiQuotaException>(() => c2.RewriteTextAsync("2", DictationStyle.Clean, "fr", default));
            Assert.Empty(h2.Calls);
            Assert.Equal(0, c2.GetUsage().DayRemaining);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MinuteWindow_SlidesWithTime()
    {
        var t = new FakeTime();
        var (c, h, _) = Make(o => o.RequestsPerMinute = 1, time: t);
        h.Responses.Enqueue(() => FakeHandler.Ok("a"));
        h.Responses.Enqueue(() => FakeHandler.Ok("b"));
        await c.RewriteTextAsync("1", DictationStyle.Clean, "fr", default);
        await Assert.ThrowsAsync<GeminiQuotaException>(() => c.RewriteTextAsync("2", DictationStyle.Clean, "fr", default));
        t.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("b", await c.RewriteTextAsync("2", DictationStyle.Clean, "fr", default));
    }

    [Fact]
    public async Task ApiError_ThrowsWithCodeAndMessage()
    {
        var (c, h, _) = Make();
        h.Responses.Enqueue(() => FakeHandler.Json(HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid","status":"INVALID_ARGUMENT"}}"""));
        var ex = await Assert.ThrowsAsync<GeminiApiException>(() => c.RewriteTextAsync("a", DictationStyle.Clean, "fr", default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("INVALID_ARGUMENT", ex.Status);
        Assert.Contains("API key not valid", ex.Message);
    }

    [Fact]
    public async Task OversizedAudio_IsRejectedBeforeNetwork()
    {
        var (c, h, _) = Make();
        var big = new byte[GeminiClient.MaxInlineAudioBytes + 1];
        var ex = await Assert.ThrowsAsync<GeminiAudioTooLargeException>(() => c.TranscribeAsync(big, "fr", default));
        Assert.Contains("Mo", ex.Message);
        Assert.Empty(h.Calls);
        Assert.Equal(10, c.GetUsage().MinuteRemaining);
    }

    [Fact]
    public async Task MissingKey_Throws()
    {
        var (c, _, _) = Make(o => o.ApiKey = "");
        await Assert.ThrowsAsync<GeminiException>(() => c.RewriteTextAsync("a", DictationStyle.Clean, "fr", default));
    }

    [Fact]
    public void Wav_HasValidHeader()
    {
        var wav = WavEncoder.Encode(new short[] { 1, -2, 3 }, 16000, 1);
        Assert.Equal(44 + 6, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(36 + 6, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(32000, BitConverter.ToInt32(wav, 28));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(6, BitConverter.ToInt32(wav, 40));
        Assert.Equal(-2, BitConverter.ToInt16(wav, 46));
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan d) => _now += d;
    }
}
