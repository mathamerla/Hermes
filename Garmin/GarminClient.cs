using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Hermes.Garmin;

public sealed class GarminClient(HttpClient http, string apiKey)
{
    public async Task<IReadOnlyList<JsonElement>> GetActivitiesAsync(DateTime sinceUtc, CancellationToken ct)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post,
            "/personalapitoken-service/personal-api/personal-api-key/token");
        tokenRequest.Headers.TryAddWithoutValidation("Authorization", $"ApiKey {apiKey}");
        using var tokenResponse = await http.SendAsync(tokenRequest, ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Garmin token is empty.");

        const int pageSize = 100;
        const int maxPages = 100;
        var output = new List<JsonElement>();
        for (var page = 0; page < maxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/activitylist-service/activities/search/activities?start={page * pageSize}&limit={pageSize}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Unexpected Garmin activity list.");

            var items = document.RootElement.EnumerateArray().ToList();
            if (items.Count == 0) return output;
            foreach (var activity in items)
            {
                // The Garmin activity list is sorted newest first.
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
}
