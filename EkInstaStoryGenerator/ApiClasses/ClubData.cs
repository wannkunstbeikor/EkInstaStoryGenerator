using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EkInstaStoryGenerator.ApiClasses;

public class ClubData
{
    [JsonPropertyName("club")]
    public Club Club { get; set; }

    [JsonPropertyName("matches")]
    public List<Match> Matches { get; set; }
}