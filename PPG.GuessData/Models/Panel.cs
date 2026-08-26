using System.Text.Json.Serialization;

namespace PPG.GuessData.Models;

public sealed class Panel
{
    [JsonPropertyName("WEEK_DATE")]
    public string WeekDate { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, object?> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetValue(string header) =>
        Values.TryGetValue(header, out var value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
}
