using System.Net.Http.Headers;
using System.Text.Json;

namespace Hermes.Garmin;

public sealed class GarminRecoveryClient(HttpClient http, string apiKey)
{
    public async Task<RecoveryPayload> GetDailyAsync(DateOnly date, CancellationToken ct)
    {
        using var auth = new HttpRequestMessage(HttpMethod.Post,
            "/personalapitoken-service/personal-api/personal-api-key/token");
        auth.Headers.TryAddWithoutValidation("Authorization", $"ApiKey {apiKey}");
        using var authResponse = await http.SendAsync(auth, ct);
        authResponse.EnsureSuccessStatusCode();
        var token = (await authResponse.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Empty Garmin token.");

        var iso = date.ToString("yyyy-MM-dd");
        return new RecoveryPayload(date,
            await GetAsync($"/sleep-service/sleep/dailySleep?date={iso}", token, ct),
            await GetAsync($"/hrv-service/hrv/{iso}", token, ct),
            await GetAsync($"/wellness-service/wellness/dailyHeartRate?date={iso}", token, ct));
    }

    private async Task<JsonElement?> GetAsync(string path, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        res.EnsureSuccessStatusCode();
        var content = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content) || content.Trim() == "null") return null;
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
public sealed record RecoveryPayload(DateOnly Date, JsonElement? Sleep, JsonElement? Hrv, JsonElement? HeartRate);
