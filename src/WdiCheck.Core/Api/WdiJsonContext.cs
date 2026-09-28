using System.Text.Json.Serialization;

namespace WdiCheck.Core.Api;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(Envelope<List<Family>>))]
[JsonSerializable(typeof(Envelope<List<WaterLevel>>))]
[JsonSerializable(typeof(Envelope<List<Artefact>>))]
[JsonSerializable(typeof(Envelope<List<Board>>))]
[JsonSerializable(typeof(Envelope<List<InfRow>>))]
[JsonSerializable(typeof(Envelope<Manifest>))]
[JsonSerializable(typeof(BoardReport))]
[JsonSerializable(typeof(CacheMeta))]
public partial class WdiJsonContext : JsonSerializerContext
{
}
