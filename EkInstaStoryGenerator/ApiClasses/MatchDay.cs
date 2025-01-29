using System.Text.Json.Serialization;

namespace EkInstaStoryGenerator.ApiClasses;

public class MatchDay
{
    [JsonPropertyName("spieltag")]
    public int Spieltag { get; set; }
    
    [JsonPropertyName("bezeichnung")]
    public string Bezeichnung { get; set; }
}