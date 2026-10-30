using System.Globalization;
using System.Text.Json;

namespace Hermes.Garmin;

public sealed record Run(string Id, string StartUtc, string LocalDate, string Type,
    double? DistanceKm, double? DurationSec, double? PaceMinKm,
    double? AverageHr, double? MaxHr, double? ElevationM);

public static class GarminRunMapper
{
    public static Run? Map(JsonElement activity)
    {
        var type = activity.TryGetProperty("activityType", out var activityType)
            ? Text(activityType, "typeKey") : "";
        if (!type.Contains("running", StringComparison.OrdinalIgnoreCase)) return null;
        if (!activity.TryGetProperty("activityId", out var idElement)) return null;

        var startText = Text(activity, "startTimeGMT");
        if (!DateTime.TryParse(startText, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
            return null;
        var local = Text(activity, "startTimeLocal");
        var distance = Number(activity, "distance") is { } meters ? meters / 1000 : (double?)null;
        var duration = Number(activity, "duration");
        var pace = distance is > 0 && duration.HasValue ? duration / 60 / distance : null;
        return new Run(idElement.ToString(), start.ToUniversalTime().ToString("O"),
            local.Length >= 10 ? local[..10] : "", type, Round(distance, 3), duration,
            Round(pace, 3), Number(activity, "averageHR"), Number(activity, "maxHR"),
            Number(activity, "elevationGain"));
    }

    public static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString() : "";

    public static double? Number(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble() : null;

    private static double? Round(double? number, int decimals) =>
        number.HasValue ? Math.Round(number.Value, decimals) : null;
}
