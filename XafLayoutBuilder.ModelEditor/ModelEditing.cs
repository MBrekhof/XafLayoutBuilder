using System.ComponentModel;
using System.Globalization;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Utils.Reflection;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>One value of a model node as the editor shows it.</summary>
/// <param name="Choices">What a bool or enum value can be, for a drop-down; null for free text and for read-only values.</param>
/// <param name="Description">FastModelEditorHelper's description: type, owner interfaces and [Description], with &lt;b&gt;/&lt;br&gt; markup.</param>
/// <param name="ReadOnlyMessage">Why a read-only value cannot be edited, when [ModelReadOnly] says.</param>
public sealed record ModelValueRow(string Name, Type Type, string Text, bool IsModified, bool CanEdit, IReadOnlyList<string>? Choices,
    string? Category = null, string? Description = null, bool IsRequired = false, bool IsLocalizable = false, string? ReadOnlyMessage = null);

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

    // The WinForms tree's order (ModelTreeListNodeComparer): DevExpress's ModelNodeComparerBase compares Index (negative
    // indexes last, Model/Core/ModelNodesComparer.cs 52-75), then the display value.
    sealed class TreeOrderComparer() : ModelNodeComparerBase<IModelNode>(true) {
        protected override string GetModelNodeDisplayValue(IModelNode node) => Caption(node);
    }

    static readonly TreeOrderComparer TreeOrder = new();

    public static IReadOnlyList<IModelNode> Children(IModelNode node) {
        var children = Enumerable.Range(0, node.NodeCount).Select(i => node.GetNode(i)).ToList();
        children.Sort(TreeOrder);
        return children;
    }

    public static string Id(IModelNode node) => ((ModelNode)node).Id;

    /// <summary>What the tree shows: the node's [DisplayProperty] value, or its id.</summary>
    public static string Caption(IModelNode node) {
        try {
            return Helper.GetModelNodeDisplayValue(node);
        }
        catch (Exception) {
            return Id(node); // a display property whose calculator throws must not break the tree
        }
    }

    /// <summary>Whether the node's type can have children at all; known without generating them.</summary>
    public static bool CanHaveChildren(IModelNode node) => ((ModelNode)node).NodeInfo.GetChildrenTypes().Count > 0;

    /// <summary>The node has differences in the writable layer (ModelNode.HasModification, ModelNode.cs 884-890).</summary>
    public static bool IsModified(IModelNode node) => ((ModelNode)node).HasModification;

    public static string NodeDescription(IModelNode node) => Helper.GetNodeDescription((ModelNode)node);

    /// <summary>
    /// A description as HTML: everything encoded except the &lt;b&gt;, &lt;/b&gt; and &lt;br&gt; DevExpress formats it with, so a generic type
    /// name such as System.Nullable&lt;System.Int32&gt; (ModelEditorHelper.GetFriendlyTypeName) shows as text (Codex review).
    /// </summary>
    public static string DescriptionHtml(string? description) =>
        System.Net.WebUtility.HtmlEncode(description ?? "")
            .Replace("&lt;b&gt;", "<b>")
            .Replace("&lt;/b&gt;", "</b>")
            .Replace("&lt;br&gt;", "<br>");

    /// <summary>The ids from below the root down to the node, joined with '/': "Views/Order_ListView".</summary>
    public static string Path(IModelNode node) {
        var ids = new Stack<string>();
        for (var n = node; n.Parent is not null; n = n.Parent) ids.Push(Id(n));
        return string.Join("/", ids);
    }

    /// <summary>
    /// Nodes whose caption or id contains the text, breadth first so shallow matches come first. ponytail: searching reads
    /// nodes and so generates what it passes, as the WinForms search does; it stops at <paramref name="limit"/> matches or
    /// <paramref name="maxNodes"/> nodes visited, which bounds the cost on a large model.
    /// </summary>
    public static IReadOnlyList<IModelNode> Search(IModelNode root, string text, int limit = 50, int maxNodes = 5000) {
        var found = new List<IModelNode>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        var queue = new Queue<IModelNode>(Children(root));
        for (var visited = 0; queue.Count > 0 && found.Count < limit && visited < maxNodes; visited++) {
            var node = queue.Dequeue();
            if (Caption(node).Contains(text, StringComparison.OrdinalIgnoreCase) || Id(node).Contains(text, StringComparison.OrdinalIgnoreCase))
                found.Add(node);
            foreach (var child in Children(node)) queue.Enqueue(child);
        }
        return found;
    }

    public static IReadOnlyList<ModelValueRow> Values(IModelNode node) {
        var modelNode = (ModelNode)node;
        // The WinForms grid's rules (ModelAttributesPropertyGridHelper.CalculatePropertyVisible, 505-540): Index is not
        // offered for the root or a node right under it, and [ModelHideProperties] hides named values.
        var hidden = AttributeHelper.GetAttributesConsideringInterfaces<ModelHidePropertiesAttribute>(modelNode.GetType(), true)
            .SelectMany(a => a.HideProperties ?? [])
            .ToHashSet();
        var indexHidden = node.Parent is null or { Parent: null };
        return modelNode.NodeInfo.ValuesInfo
            .Where(v => !Bookkeeping.Contains(v.Name)
                && !hidden.Contains(v.Name)
                && !(indexHidden && v.Name == ModelValueNames.Index)
                && Helper.IsPropertyModelBrowsableVisible(modelNode, v.Name))
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

    /// <summary>
    /// The aspects of a differences layer whose XML is empty, written the way ModelDifferenceDbStore.SaveDifference writes
    /// each aspect (ModelXmlWriter.WriteToString(model, aspectIndex), ModelDifferenceDbStore.cs 194-198).
    /// </summary>
    public static IReadOnlyList<string> EmptyAspects(ModelApplicationBase layer) {
        var writer = new ModelXmlWriter();
        return Enumerable.Range(0, layer.AspectCount)
            .Where(i => string.IsNullOrEmpty(writer.WriteToString(layer, i)))
            .Select(layer.GetAspect)
            .ToList();
    }

    /// <summary>
    /// The aspects a change empties: empty afterwards and not before. Only these may be blanked in the store, since an aspect
    /// already empty in this circuit's model may hold what another tab saved since (Codex re-review).
    /// </summary>
    public static IReadOnlyList<string> AspectsEmptiedBy(ModelApplicationBase layer, Action change) {
        var before = EmptyAspects(layer);
        change();
        return EmptyAspects(layer).Except(before).ToList();
    }

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
    // until they get their own editors (MODELEDITOR-005, -006).
    static ModelValueRow Row(ModelNode node, ModelValueInfo info) {
        var type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        var readOnly = Helper.IsReadOnly(node, info.Name);
        var editable = (type == typeof(string) || type.IsPrimitive || type.IsEnum) && !readOnly;
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
        return new(info.Name, info.PropertyType, text, node.IsValueModified(info.Name), editable, choices,
            Category: Helper.GetPropertyAttribute<CategoryAttribute>(node, info.Name)?.Category,
            Description: Helper.GetPropertyDescription(node, info.Name),
            IsRequired: Helper.IsRequired(node, info.Name),
            IsLocalizable: info.IsLocalizable,
            ReadOnlyMessage: readOnly ? Helper.GetPropertyAttribute<ModelReadOnlyAttribute>(node, info.Name)?.Message : null);
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

    // Aspects the applied edits emptied whose stored rows have not been blanked yet (StoredAspectCleanup).
    readonly HashSet<string> emptiedAspects = [];

    /// <summary>The aspects applied edits emptied, kept until <see cref="Saved"/> so a save that throws can be retried (Codex review 3).</summary>
    public IReadOnlyCollection<string> EmptiedAspects => emptiedAspects;

    /// <summary>
    /// Writes the pending edits to the model's writable layer. Given the user differences layer, the aspects this empties are
    /// added to <see cref="EmptiedAspects"/>. In a warmed-up model a reset keeps showing the old value until the model is built
    /// again, because ClearValue skips the value cache; the editor reloads the page after Save.
    /// </summary>
    public void Apply(ModelApplicationBase? userLayer = null) {
        // Checked before anything is written, so a refused Save leaves the model and every pending edit as they were.
        if (pending.FirstOrDefault(p => p.Value.Error is not null) is { Value.Error: { } error } invalid)
            throw new InvalidOperationException($"{ModelEditing.Path(invalid.Key.Node)}: {invalid.Key.Name} is not saved: {error}");
        if (userLayer is null) WritePending();
        else {
            emptiedAspects.UnionWith(ModelEditing.AspectsEmptiedBy(userLayer, WritePending));
            // An aspect a later edit filled again holds that edit now; blanking its row would lose it (Codex review 4).
            emptiedAspects.IntersectWith(ModelEditing.EmptyAspects(userLayer));
        }
    }

    /// <summary>The applied edits are saved and their emptied aspects blanked.</summary>
    public void Saved() => emptiedAspects.Clear();

    void WritePending() {
        foreach (var ((node, name), (text, _)) in pending) {
            if (text is null) ModelEditing.Reset(node, name);
            else ModelEditing.SetText(node, name, text);
        }
        pending.Clear();
    }
}
