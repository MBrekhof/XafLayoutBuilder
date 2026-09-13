using System.ComponentModel;
using System.Globalization;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>One value of a model node as the editor shows it.</summary>
/// <param name="Choices">What a bool or enum value can be, for a drop-down; null for free text and for read-only values.</param>
public sealed record ModelValueRow(string Name, Type Type, string Text, bool IsModified, bool CanEdit, IReadOnlyList<string>? Choices);

/// <summary>
/// The editor's model logic, kept out of the component so it runs against an in-process model in tests. Reads go through
/// every layer; writes and resets go to the node's writable layer, which in a running application is the current user's
/// differences (ModelNode.SetValue 2598-2601, ClearValue 2368-2383, IsValueModified 899-903). Verified API: docs/api-notes.md.
/// </summary>
public static class ModelEditing {
    // Bookkeeping every node carries (ModelNodeInfo.CreateValuesInfo, ModelNodeInfo.cs 175-181). Index stays: it orders siblings.
    static readonly HashSet<string> Bookkeeping = [ModelValueNames.Id, ModelValueNames.IsNewNode, ModelValueNames.IsRemovedNode];

    // The WinForms Model Editor's attribute rules (Browsable, HideInUI, ModelBrowsable calculators, ModelReadOnly), public.
    static readonly FastModelEditorHelper Helper = new();

    public static IReadOnlyList<IModelNode> Children(IModelNode node) =>
        Enumerable.Range(0, node.NodeCount).Select(i => node.GetNode(i)).ToList();

    public static string Id(IModelNode node) => ((ModelNode)node).Id;

    /// <summary>The ids from below the root down to the node, joined with '/': "Views/Order_ListView".</summary>
    public static string Path(IModelNode node) {
        var ids = new Stack<string>();
        for (var n = node; n.Parent is not null; n = n.Parent) ids.Push(Id(n));
        return string.Join("/", ids);
    }

    public static IReadOnlyList<ModelValueRow> Values(IModelNode node) {
        var modelNode = (ModelNode)node;
        return modelNode.NodeInfo.ValuesInfo
            .Where(v => !Bookkeeping.Contains(v.Name) && Helper.IsPropertyModelBrowsableVisible(modelNode, v.Name))
            .OrderBy(v => v.Name, StringComparer.Ordinal)
            .Select(v => Row(modelNode, v))
            .ToList();
    }

    /// <summary>Writes a value typed as text. Empty text clears a value that is not a string, so the layers below apply again.</summary>
    public static void SetText(IModelNode node, string name, string text) {
        var modelNode = (ModelNode)node;
        if (Parse(modelNode, name, text) is (true, var value)) modelNode.SetValue(name, value);
        else modelNode.ClearValue(name);
    }

    public static void Reset(IModelNode node, string name) => ((ModelNode)node).ClearValue(name);

    /// <summary>Throws when the text is no value of the named value's type; changes nothing.</summary>
    internal static void Validate(IModelNode node, string name, string text) => Parse((ModelNode)node, name, text);

    // (false, _): clear the value.
    static (bool Set, object? Value) Parse(ModelNode node, string name, string text) {
        var info = node.GetValueInfo(name) ?? throw new ArgumentException($"'{name}' is not a value of {Path(node)}.", nameof(name));
        var type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        if (type == typeof(string)) return (true, text);
        if (text.Length == 0) return (false, null);
        return (true, TypeDescriptor.GetConverter(type).ConvertFromInvariantString(text));
    }

    // ponytail: strings, primitives and enums are editable; references to other nodes, types and criteria stay read-only
    // until they get their own editors.
    static ModelValueRow Row(ModelNode node, ModelValueInfo info) {
        var type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        var editable = (type == typeof(string) || type.IsPrimitive || type.IsEnum) && !Helper.IsReadOnly(node, info.Name);
        string text;
        try {
            text = Format(node.GetValue(info.Name));
        }
        catch (Exception ex) {
            // A value calculator can throw on a node it does not expect; show that instead of failing the whole grid.
            text = $"({ex.GetType().Name}: {ex.Message})";
            editable = false;
        }
        string[]? choices = !editable ? null : type == typeof(bool) ? ["True", "False"] : type.IsEnum ? Enum.GetNames(type) : null;
        return new(info.Name, info.PropertyType, text, node.IsValueModified(info.Name), editable, choices);
    }

    static string Format(object? value) => value switch {
        null => "",
        IModelNode n => Path(n),
        Type t => t.FullName ?? t.Name,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

/// <summary>
/// One editor's edits, pending until <see cref="Apply"/> (Codex review). Written to the running model at once they would be
/// persisted by any later model save, and they cannot be rolled back there: XAF Blazor's warmed-up model answers GetValue
/// from a cache that ClearValue does not update (docs/api-notes.md). An editor closed without Save just drops its session.
/// </summary>
public sealed class ModelEditSession {
    // Text null: reset the value on Apply. Error set: the text is no value of that type, and Apply refuses to run.
    readonly Dictionary<(IModelNode Node, string Name), (string? Text, string? Error)> pending = [];

    /// <summary>
    /// Keeps the edit pending. Text that is no value of that type is kept too, with its error, so it supersedes an earlier
    /// valid edit of the same value instead of letting Save write that one (Codex re-review); then it throws.
    /// </summary>
    public void SetText(IModelNode node, string name, string text) {
        try {
            ModelEditing.Validate(node, name, text);
        }
        catch (Exception ex) {
            pending[(node, name)] = (text, ex.Message);
            throw;
        }
        pending[(node, name)] = (text, null);
    }

    public void Reset(IModelNode node, string name) => pending[(node, name)] = (null, null);

    public bool HasErrors => pending.Values.Any(p => p.Error is not null);

    /// <param name="text">The pending text, or null for a pending reset.</param>
    /// <param name="error">Why the text is no value of that type, or null.</param>
    public bool TryGetPending(IModelNode node, string name, out string? text, out string? error) {
        var found = pending.TryGetValue((node, name), out var entry);
        (text, error) = entry;
        return found;
    }

    /// <summary>
    /// Writes the pending edits to the model's writable layer. ponytail: in a warmed-up model a reset keeps showing the old
    /// value until the model is built again (the next page load), because ClearValue skips the value cache; what Save stores
    /// is right. Undo() would refresh the cache but takes back every other modification of the node.
    /// </summary>
    public void Apply() {
        // Checked before anything is written, so a refused Save leaves the model and every pending edit as they were.
        if (pending.FirstOrDefault(p => p.Value.Error is not null) is { Value.Error: { } error } invalid)
            throw new InvalidOperationException($"{ModelEditing.Path(invalid.Key.Node)}: {invalid.Key.Name} is not saved: {error}");
        foreach (var ((node, name), (text, _)) in pending) {
            if (text is null) ModelEditing.Reset(node, name);
            else ModelEditing.SetText(node, name, text);
        }
        pending.Clear();
    }
}
