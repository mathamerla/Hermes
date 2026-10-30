using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Hermes.Garmin;
using Hermes.Storage;
using Hermes.Synchronization;

const string defaultSpreadsheetId = "1DnbhvPN6AALIfgnU4GXkxPEmH6bZCq-SFGhX9q5-J5Q";
var key = Environment.GetEnvironmentVariable("GARMIN_API_KEY");
if (string.IsNullOrWhiteSpace(key))
    throw new InvalidOperationException("Missing GARMIN_API_KEY.");

var spreadsheetId = Environment.GetEnvironmentVariable("TRAINING_SPREADSHEET_ID") ?? defaultSpreadsheetId;
using var http = new HttpClient
{
    BaseAddress = new Uri("https://personal.connectapi.garmin.com"),
    Timeout = TimeSpan.FromSeconds(45)
};
var credential = (await GoogleCredential.GetApplicationDefaultAsync())
    .CreateScoped(SheetsService.Scope.Spreadsheets);
using var sheetsClient = new SheetsService(new BaseClientService.Initializer
{
    HttpClientInitializer = credential, ApplicationName = "Hermes"
});
var service = new ActivitySyncService(new GarminClient(http, key),
    new GoogleSheetsRepository(sheetsClient, spreadsheetId), TimeProvider.System);
var fullSync = args.Contains("--full-sync", StringComparer.OrdinalIgnoreCase);
var result = await service.RunAsync(fullSync, CancellationToken.None);
Console.WriteLine($"Garmin sync OK: scanned={result.Scanned}, inserted={result.Inserted}, updated={result.Updated}, mode={(fullSync ? "full" : "14d")}.");

var recoveryDays = args.Contains("--recovery-backfill", StringComparer.OrdinalIgnoreCase) ? 30 : 7;
var recovery = new RecoverySyncService(
    new GarminRecoveryClient(http, key),
    new GoogleRecoveryRepository(sheetsClient, spreadsheetId), TimeProvider.System);
var importedDays = await recovery.RunAsync(recoveryDays, CancellationToken.None);
Console.WriteLine($"Garmin recovery sync OK: days={importedDays}.");
