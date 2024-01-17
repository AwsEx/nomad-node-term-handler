using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nomad.NodeTermHandler.Models;

public class EventBridgeEvent
{
    [JsonProperty("version")]
    public string? Version { get; set; }

    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("detail-type")]
    public string? DetailType { get; set; }

    [JsonProperty("source")]
    public string? Source { get; set; }

    [JsonProperty("account")]
    public string? Account { get; set; }

    [JsonProperty("time")]
    public DateTime Time { get; set; }

    [JsonProperty("region")]
    public string? Region { get; set; }

    [JsonProperty("detail")]
    [JsonConverter(typeof(DetailConverter))]
    public string? Detail { get; set; }
}

public class DetailConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(string);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        return JRaw.Create(reader).ToString();
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        writer.WriteRawValue((string?)value);
    }
}