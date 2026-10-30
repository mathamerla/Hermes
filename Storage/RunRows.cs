using System.Globalization;
using Hermes.Garmin;

namespace Hermes.Storage;

public static class RunRows
{
    public static IList<object> ToCells(Run run, IList<object>? previous, DateTime updatedUtc)
        => new List<object>
        {
            "Garmin", run.Id, run.StartUtc, run.LocalDate, run.Type,
            Cell(run.DistanceKm), Cell(run.DurationSec), Cell(run.PaceMinKm),
            Cell(run.AverageHr), Cell(run.MaxHr), Cell(run.ElevationM),
            Value(previous, 11), Value(previous, 12), updatedUtc.ToString("O")
        };

    public static string Value(IList<object>? row, int index)
        => row != null && index < row.Count
            ? Convert.ToString(row[index], CultureInfo.InvariantCulture) ?? "" : "";

    public static bool HasTrainingChanges(IList<object> expected, IList<object> previous)
        => Enumerable.Range(0, 13).Any(i => !Equivalent(Value(expected, i), Value(previous, i)));

    private static object Cell(double? value) => value.HasValue ? value.Value : "";

    private static bool Equivalent(string a, string b)
    {
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return Math.Abs(x - y) < 0.00001;
        return string.Equals(a, b, StringComparison.Ordinal);
    }
}
