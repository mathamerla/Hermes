using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Hermes.Garmin;

namespace Hermes.Storage;

public sealed class GoogleRecoveryRepository(SheetsService sheets, string spreadsheetId)
{
    public async Task<Dictionary<string, (int Row, IList<object> Values)>> GetRowsAsync(CancellationToken ct)
    {
        var response = await sheets.Spreadsheets.Values.Get(spreadsheetId, "Recovery!A2:I").ExecuteAsync(ct);
        var data = response.Values ?? new List<IList<object>>();
        return data.Select((values, index) => (values, index))
            .Where(x => x.values.Count > 0)
            .GroupBy(x => Convert.ToString(x.values[0], CultureInfo.InvariantCulture) ?? "")
            .ToDictionary(x => x.Key, x => (x.Last().index + 2, x.Last().values));
    }

    public async Task UpsertAsync(RecoveryDay day,
        Dictionary<string, (int Row, IList<object> Values)> existing, CancellationToken ct)
    {
        var date = day.Date.ToString("yyyy-MM-dd");
        existing.TryGetValue(date, out var old);
        object Cell(double? value) => value.HasValue ? value.Value : "";
        string Previous(int index) => old.Values is not null && index < old.Values.Count
            ? Convert.ToString(old.Values[index], CultureInfo.InvariantCulture) ?? "" : "";
        IList<object> row = new List<object> { date,
            Cell(day.RestingHeartRate), Cell(day.HrvMs), Cell(day.SleepHours), Cell(day.SleepScore),
            Previous(5), Previous(6), Previous(7), Previous(8) };

        // Do not replace existing readings with blanks if Garmin has no data for this date.
        for (var i = 1; i <= 4; i++)
            if (row[i] is string s && s.Length == 0) row[i] = Previous(i);
        if (old.Row != 0 && Enumerable.Range(0, 9).All(i => Previous(i) == Convert.ToString(row[i], CultureInfo.InvariantCulture)))
            return;

        if (old.Row == 0)
        {
            var append = sheets.Spreadsheets.Values.Append(new ValueRange { Values = new List<IList<object>> { row } },
                spreadsheetId, "Recovery!A:I");
            append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            append.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
            await append.ExecuteAsync(ct);
            existing[date] = (-1, row);
        }
        else
        {
            var update = sheets.Spreadsheets.Values.Update(new ValueRange { Values = new List<IList<object>> { row } },
                spreadsheetId, $"Recovery!A{old.Row}:I{old.Row}");
            update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await update.ExecuteAsync(ct);
            existing[date] = (old.Row, row);
        }
    }
}
