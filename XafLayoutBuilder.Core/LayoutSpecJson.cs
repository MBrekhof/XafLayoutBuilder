using System.Text.Json;
using System.Text.Json.Serialization;

namespace XafLayoutBuilder.Core;

/// <summary>JSON form of the specs: the contract a generator emits against and a handy test fixture format.</summary>
public static class LayoutSpecJson {
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<TSpec>(TSpec spec) => JsonSerializer.Serialize(spec, Options);

    /// <summary>Malformed JSON, and a node without its <c>$type</c>, throw <see cref="LayoutSpecException"/> like any layout error.</summary>
    public static TSpec Deserialize<TSpec>(string json) {
        try {
            return JsonSerializer.Deserialize<TSpec>(json, Options) ?? throw new LayoutSpecException("JSON deserialised to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) {
            throw new LayoutSpecException($"Invalid layout JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// Both halves of one type's layout as one JSON document: what the JSON export writes and
/// <c>LayoutRegistry.RegisterJson</c> reads. A null half is left out of the JSON and leaves that view to XAF.
/// </summary>
public sealed record LayoutSpecs(DetailLayoutSpec? Detail, ListColumnsSpec? Columns);
