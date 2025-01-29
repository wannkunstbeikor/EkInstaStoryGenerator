using System.Text.Json.Serialization;

namespace EkInstaStoryGenerator.ApiClasses;

public class Person
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("vorname")]
    public string Vorname { get; set; }

    [JsonPropertyName("nachname")]
    public string Nachname { get; set; }

    [JsonPropertyName("anonym")]
    public bool Anonym { get; set; }
}