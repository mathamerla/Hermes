using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Hermes.Storage;

public sealed class GoogleSheetsRepository(SheetsService sheets, string spreadsheetId)
{
    public async Task<IReadOnlyDictionary<string, (int Row, IList<object> Data)>> GetGarminRunsAsync(
        CancellationToken ct)
    {
        var read = sheets.Spreadsheets.Values.Get(spreadsheetId, "Runs!A2:N");
        var result = await read.ExecuteAsync(ct);
        var rows = result.Values ?? new List<IList<object>>();
        var dictionary = new Dictionary<string, (int, IList<object>)>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (RunRows.Value(row, 0) == "Garmin" && !string.IsNullOrEmpty(RunRows.Value(row, 1)))
                dictionary[RunRows.Value(row, 1)] = (i + 2, row);
        }
        return dictionary;
    }

    public async Task UpsertExistingAsync(int rowNumber, IList<object> data, CancellationToken ct)
    {
        var update = sheets.Spreadsheets.Values.Update(
            new ValueRange { Values = new List<IList<object>> { data } }, spreadsheetId,
            $"Runs!A{rowNumber}:N{rowNumber}");
        update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync(ct);
    }

    public async Task AppendAsync(IReadOnlyList<IList<object>> data, CancellationToken ct)
    {
        if (data.Count == 0) return;
        var append = sheets.Spreadsheets.Values.Append(
            new ValueRange { Values = data.ToList() }, spreadsheetId, "Runs!A:N");
        append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
        append.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        await append.ExecuteAsync(ct);
    }

    public async Task RecordSyncAsync(DateTime successUtc, int processed, string mode, CancellationToken ct)
    {
        // Sync Status columns: Source, LastSuccessUTC, Cursor, Records, Status, Notes.
        var update = sheets.Spreadsheets.Values.Update(
            new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object> { "Garmin", successUtc.ToString("O"), "", processed, "OK", mode }
                }
            }, spreadsheetId, "Sync Status!A2:F2");
        update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync(ct);
    }
}
