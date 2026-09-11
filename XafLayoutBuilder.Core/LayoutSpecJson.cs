using System.Text.Json;
using System.Text.Json.Serialization;

namespace XafLayoutBuilder.Core;

/// <summary>JSON form of the specs: the contract BPG emits against (phase 2 loader) and a handy test fixture format.</summary>
public static class LayoutSpecJson {
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<TSpec>(TSpec spec) => JsonSerializer.Serialize(spec, Options);

    public static TSpec Deserialize<TSpec>(string json) =>
        JsonSerializer.Deserialize<TSpec>(json, Options) ?? throw new LayoutSpecException("JSON deserialised to null.");
}
