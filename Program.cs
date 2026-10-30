using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

const string spreadsheetId = "1DnbhvPN6AALIfgnU4GXkxPEmH6bZCq-SFGhX9q5-J5Q";
const int pageSize = 100;
const int maxPages = 100;
var fullSync = args.Contains("--full-sync", StringComparer.OrdinalIgnoreCase);
var since = fullSync ? DateTime.MinValue : DateTime.UtcNow.AddDays(-14);

var apiKey = Environment.GetEnvironmentVariable("GARMIN_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Missing GARMIN_API_KEY.");

using var http = new HttpClient
{
    BaseAddress = new Uri("https://personal.connectapi.garmin.com"),
    Timeout = TimeSpan.FromSeconds(45)
};
using var tokenRequest = new HttpRequestMessage(
    HttpMethod.Post, "/personalapitoken-service/personal-api/personal-api-key/token");
tokenRequest.Headers.TryAddWithoutValidation("Authorization", $"ApiKey {apiKey}");
using var tokenResponse = await http.SendAsync(tokenRequest);
tokenResponse.EnsureSuccessStatusCode();
// Personal API currently returns the bearer token as raw response text.
var accessToken = (await tokenResponse.Content.ReadAsStringAsync()).Trim().Trim('"');
if (string.IsNullOrWhiteSpace(accessToken))
    throw new InvalidOperationException("Garmin token response was empty.");

var credential = (await GoogleCredential.GetApplicationDefaultAsync())
    .CreateScoped(SheetsService.Scope.Spreadsheets);
using var sheets = new SheetsService(new BaseClientService.Initializer
{
    HttpClientInitializer = credential,
    ApplicationName = "Hermes"
});

var existingRequest = sheets.Spreadsheets.Values.Get(spreadsheetId, "Runs!A2:N");
var existingResponse = await existingRequest.ExecuteAsync();
var existingRows = existingResponse.Values ?? new List<IList<object>>();
var byId = new Dictionary<string, (int RowNumber, IList<object> Data)>(StringComparer.Ordinal);
for (var i = 0; i < existingRows.Count; i++)
{
    var row = existingRows[i];
    if (row.Count >= 2 && Value(row, 0) == "Garmin" && !string.IsNullOrEmpty(Value(row, 1)))
        byId[Value(row, 1)] = (i + 2, row);
}

var inserts = new List<IList<object>>();
var changed = 0;
var scanned = 0;
var endReached = false;
var seen = new HashSet<string>(StringComparer.Ordinal);
for (var page = 0; page < maxPages; page++)
{
    using var request = new HttpRequestMessage(HttpMethod.Get,
        $"/activitylist-service/activities/search/activities?start={page * pageSize}&limit={pageSize}");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    if (json.RootElement.ValueKind != JsonValueKind.Array)
        throw new InvalidOperationException("Unexpected Garmin activity list format.");
    var activities = json.RootElement.EnumerateArray().ToList();
    if (activities.Count == 0)
    {
        endReached = true;
        break;
    }

    foreach (var activity in activities)
    {
        scanned++;
        var started = String(activity, "startTimeGMT");
        if (!DateTime.TryParse(started, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var utcStart))
            continue;
        if (utcStart < since)
        {
            // Garmin returns activities newest first.
            endReached = true;
            break;
        }
        var type = activity.TryGetProperty("activityType", out var activityType)
            ? String(activityType, "typeKey") : "";
        if (!type.Contains("running", StringComparison.OrdinalIgnoreCase))
            continue;

        var id = activity.GetProperty("activityId").ToString();
        if (!seen.Add(id))
            continue;
        var distanceKm = Number(activity, "distance") is { } d ? d / 1000.0 : (double?)null;
        var duration = Number(activity, "duration");
        var pace = distanceKm is > 0 && duration.HasValue
            ? duration.Value / 60.0 / distanceKm.Value : (double?)null;
        var localStart = String(activity, "startTimeLocal");
        var localDate = localStart.Length >= 10 ? localStart[..10] : "";
        byId.TryGetValue(id, out var previous);
        // Keep manually maintained TrainingLoad and Notes.
        IList<object> row = new List<object>
        {
            "Garmin", id, utcStart.ToString("O"), localDate, type,
            Cell(distanceKm, 3), Cell(duration), Cell(pace, 3),
            Cell(Number(activity, "averageHR")), Cell(Number(activity, "maxHR")),
            Cell(Number(activity, "elevationGain")),
            Value(previous.Data, 11), Value(previous.Data, 12),
            ""
        };
        if (previous.RowNumber == 0)
        {
            row[13] = DateTime.UtcNow.ToString("O");
            inserts.Add(row);
        }
        else if (!Enumerable.Range(0, 13).All(i => Equivalent(Value(row, i), Value(previous.Data, i))))
        {
            row[13] = DateTime.UtcNow.ToString("O");
            var update = sheets.Spreadsheets.Values.Update(
                new ValueRange { Values = new List<IList<object>> { row } },
                spreadsheetId, $"Runs!A{previous.RowNumber}:N{previous.RowNumber}");
            update.ValueInputOption = SpreadsheetsResource.ValuesResource
                .UpdateRequest.ValueInputOptionEnum.RAW;
            await update.ExecuteAsync();
            changed++;
        }
    }

    if (endReached || activities.Count < pageSize)
    {
        endReached = true;
        break;
    }
}
if (!endReached)
    throw new InvalidOperationException("Garmin pagination safety cap reached; rerun with a narrower window.");

if (inserts.Count > 0)
{
    // Write new entries once, after all Garmin pages succeeded.
    var append = sheets.Spreadsheets.Values.Append(
        new ValueRange { Values = inserts }, spreadsheetId, "Runs!A:N");
    append.ValueInputOption = SpreadsheetsResource.ValuesResource
        .AppendRequest.ValueInputOptionEnum.RAW;
    append.InsertDataOption = SpreadsheetsResource.ValuesResource
        .AppendRequest.InsertDataOptionEnum.INSERTROWS;
    await append.ExecuteAsync();
}
Console.WriteLine($"Garmin sync OK: scanned={scanned}, inserted={inserts.Count}, updated={changed}, mode={(fullSync ? "full" : "14d")}.");

static string Value(IList<object>? values, int index) =>
    values is not null && index < values.Count ? Convert.ToString(values[index], CultureInfo.InvariantCulture) ?? "" : "";
static string String(JsonElement item, string property) =>
    item.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
        ? value.ToString() : "";
static double? Number(JsonElement item, string property) =>
    item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
        ? value.GetDouble() : null;
static object Cell(double? value, int? decimals = null) =>
    value.HasValue ? decimals.HasValue ? Math.Round(value.Value, decimals.Value) : value.Value : "";
static bool Equivalent(string left, string right)
{
    if (double.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) &&
        double.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
        return Math.Abs(a - b) < 0.00001;
    return string.Equals(left, right, StringComparison.Ordinal);
}
