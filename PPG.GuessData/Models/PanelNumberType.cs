using System.Text.Json.Serialization;

namespace PPG.GuessData.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PanelNumberType
{
    Open,
    Close
}
