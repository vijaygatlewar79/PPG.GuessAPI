using System.Text.Json.Serialization;

namespace PPG.GuessData.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PanelPatternType
{
    AI,
    ThreeTouch,
    Sequence,
    Cross,
    Weekly,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
    Sunday
}
