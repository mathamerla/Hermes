using System.Text.Json;

namespace Hermes.Garmin;

public sealed record RecoveryDay(DateOnly Date, double? RestingHeartRate, double? HrvMs,
    double? SleepHours, double? SleepScore);

public static class GarminRecoveryMapper
{
    public static RecoveryDay Map(RecoveryPayload data)
    {
        var sleep = Object(data.Sleep, "dailySleepDTO");
        var hrv = Object(data.Hrv, "hrvSummary");
        var seconds = Number(sleep, "sleepTimeSeconds");
        return new RecoveryDay(data.Date,
            Number(data.HeartRate, "restingHeartRate") ?? Number(sleep, "restingHeartRate"),
            Number(hrv, "lastNightAvg"),
            seconds.HasValue ? Math.Round(seconds.Value / 3600, 2) : null,
            Number(Object(sleep, "sleepScores"), "overallScore") ??
            Number(sleep, "sleepScore"));
    }

    private static JsonElement? Object(JsonElement? root, string property)
    {
        if (!root.HasValue || root.Value.ValueKind != JsonValueKind.Object ||
            !root.Value.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            return null;
        return value;
    }

    private static double? Number(JsonElement? root, string property)
    {
        if (!root.HasValue || root.Value.ValueKind != JsonValueKind.Object ||
            !root.Value.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        // sleepScores.overallScore can be a structured score with a value field.
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var nested) &&
            nested.ValueKind == JsonValueKind.Number && nested.TryGetDouble(out var score)) return score;
        return null;
    }
}
