using System.Text.Json;
using System.Text.Json.Serialization;
using EkInstaStoryGenerator.ApiClasses;
using InstagramApiSharp.Classes;

namespace EkInstaStoryGenerator;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ApiResponse<ClubData>))]
[JsonSerializable(typeof(User))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
