
using System.Net.Http.Headers;
using System.Text.Json;
using System.Globalization;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

var apiKey = Environment.GetEnvironmentVariable("GARMIN_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "Missing GARMIN_API_KEY environment variable.");
}

using var http = new HttpClient
{
    BaseAddress = new Uri("https://personal.connectapi.garmin.com"),
    Timeout = TimeSpan.FromSeconds(30)
};

// 1. Exchange personal API key for an access token.
using var tokenRequest = new HttpRequestMessage(
    HttpMethod.Post,
    "/personalapitoken-service/personal-api/personal-api-key/token");

tokenRequest.Headers.TryAddWithoutValidation(
    "Authorization", $"ApiKey {apiKey}");

using var tokenResponse = await http.SendAsync(tokenRequest);
tokenResponse.EnsureSuccessStatusCode();

var accessToken = await tokenResponse.Content.ReadAsStringAsync();

if (string.IsNullOrEmpty(accessToken))
    throw new InvalidOperationException("Token missing");

// 2. Read the list of recent activities.
using var request = new HttpRequestMessage(
    HttpMethod.Get,
    "/activitylist-service/activities/search/activities?start=0&limit=10");

request.Headers.Authorization =
    new AuthenticationHeaderValue("Bearer", accessToken);

using var response = await http.SendAsync(request);
response.EnsureSuccessStatusCode();

using var activities = JsonDocument.Parse(
    await response.Content.ReadAsStringAsync());

const string spreadsheetId =
    "1DnbhvPN6AALIfgnU4GXkxPEmH6bZCq-SFGhX9q5-J5Q";

var credential = await GoogleCredential
    .GetApplicationDefaultAsync();

credential = credential.CreateScoped(
    SheetsService.Scope.Spreadsheets);

using var sheets = new SheetsService(
    new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "TrainingSync"
    });

// Read existing activity identifiers.
var existingRequest = sheets.Spreadsheets.Values.Get(
    spreadsheetId,
    "Runs!B2:B");

var existingResponse = await existingRequest.ExecuteAsync();

var existingIds = (existingResponse.Values ?? [])
    .Where(row => row.Count > 0)
    .Select(row => row[0]?.ToString() ?? "")
    .ToHashSet(StringComparer.Ordinal);

var rows = new List<IList<object>>();

foreach (var activity in activities.RootElement.EnumerateArray())
{
    var type = activity
        .GetProperty("activityType")
        .GetProperty("typeKey")
        .GetString() ?? "";

    // Include outdoor, trail and treadmill running.
    if (!type.Contains("running", StringComparison.OrdinalIgnoreCase))
        continue;

    var id = activity.GetProperty("activityId").ToString();

    if (existingIds.Contains(id))
        continue;

    double? Number(string key)
    {
        if (!activity.TryGetProperty(key, out var value))
            return null;

        return value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
    }

    string Text(string key)
    {
        return activity.TryGetProperty(key, out var value)
            ? value.ToString()
            : "";
    }

    var distanceKm = (Number("distance") ?? 0) / 1000.0;
    var durationSec = Number("duration") ?? 0;

    var pace = distanceKm > 0
        ? durationSec / 60.0 / distanceKm
        : 0;

    var localStart = Text("startTimeLocal");
    var localDate = localStart.Length >= 10
        ? localStart[..10]
        : "";

    // Garmin uses UTC for startTimeGMT.
    var utcStart = Text("startTimeGMT");

    rows.Add(new List<object>
    {
        "Garmin",                       // Source
        id,                             // SourceActivityId
        utcStart,                       // StartUTC
        localDate,                      // LocalDate
        type,                           // Type
        Math.Round(distanceKm, 3),      // DistanceKm
        durationSec,                    // DurationSec
        Math.Round(pace, 3),            // PaceMinKm
        Number("averageHR") ?? 0,       // AvgHR
        Number("maxHR") ?? 0,           // MaxHR
        Number("elevationGain") ?? 0,   // ElevationM
        "",                             // TrainingLoad
        "",                             // Notes
        DateTime.UtcNow.ToString("O")   // UpdatedUTC
    });

    existingIds.Add(id);
}

if (rows.Count > 0)
{
    var append = sheets.Spreadsheets.Values.Append(
        new ValueRange { Values = rows },
        spreadsheetId,
        "Runs!A:N");

    append.ValueInputOption =
        SpreadsheetsResource.ValuesResource
            .AppendRequest.ValueInputOptionEnum.RAW;

    append.InsertDataOption =
        SpreadsheetsResource.ValuesResource
            .AppendRequest.InsertDataOptionEnum.INSERTROWS;

    await append.ExecuteAsync();
}

Console.WriteLine($"Imported {rows.Count} running activities.");