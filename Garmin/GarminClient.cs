using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Hermes.Garmin;

public sealed class GarminClient(HttpClient http, string apiKey)
{
    public async Task<IReadOnlyList<JsonElement>> GetActivitiesAsync(DateTime sinceUtc, CancellationToken ct)
    {
        using var tokenResponse = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    "/personalapitoken-service/personal-api/personal-api-key/token");
                request.Headers.TryAddWithoutValidation("Authorization", $"ApiKey {apiKey}");
                return request;
            }, ct);
        var token = (await tokenResponse.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Garmin token is empty.");

        const int pageSize = 100;
        const int maxPages = 100;
        var output = new List<JsonElement>();
        for (var page = 0; page < maxPages; page++)
        {
            var offset = page * pageSize;
            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"/activitylist-service/activities/search/activities?start={offset}&limit={pageSize}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            }, ct);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Unexpected Garmin activity list.");

            var items = document.RootElement.EnumerateArray().ToList();
            if (items.Count == 0) return output;
            foreach (var activity in items)
            {
                // Garmin activity list is sorted newest first.
                if (DateTime.TryParse(GarminRunMapper.Text(activity, "startTimeGMT"),
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var started) && started < sinceUtc)
                    return output;
                output.Add(activity.Clone());
            }
            if (items.Count < pageSize) return output;
        }
        throw new InvalidOperationException("Garmin pagination safety limit reached.");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = requestFactory();
            var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return response;
            if (attempt == 2 || response.StatusCode != HttpStatusCode.TooManyRequests
                && (int)response.StatusCode < 500)
            {
                response.EnsureSuccessStatusCode();
            }
            var retryAfter = response.Headers.RetryAfter?.Delta;
            response.Dispose();
            await Task.Delay(retryAfter is { } delay && delay <= TimeSpan.FromSeconds(30)
                ? delay : TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
        }
        throw new InvalidOperationException("Garmin retry limit reached.");
    }
}
