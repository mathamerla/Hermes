
using System.Net.Http.Headers;
using System.Text.Json;

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

Console.WriteLine(
    $"Successfully authenticated. Response kind: " +
    $"{activities.RootElement.ValueKind}");
