using System.Text.Json.Serialization;

namespace EkInstaStoryGenerator.ApiClasses;

public class Stat
{
    [JsonPropertyName("made")]
    public double Made { get; set; }

    [JsonPropertyName("attempted")]
    public double Attempted { get; set; }

    [JsonPropertyName("quota")]
    public object Quota { get; set; }
}