using System.Collections;
using System.ComponentModel;
using System.Globalization;
using DevExpress.Data.Filtering.Helpers;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Editors.Adapters;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.DomainLogics;
using DevExpress.ExpressApp.Utils;
using DevExpress.ExpressApp.Utils.Reflection;
using DevExpress.Persistent.Base;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>One value of a model node as the editor shows it.</summary>
/// <param name="Choices">What a bool, enum, reference or type value can be, for a drop-down; null for free text and for read-only values.</param>
/// <param name="Suggestions">Names offered for a free-text value (field names, languages); other text is still accepted.</param>
/// <param name="Description">FastModelEditorHelper's description: type, owner interfaces and [Description], with &lt;b&gt;/&lt;br&gt; markup.</param>
/// <param name="ReadOnlyMessage">Why a read-only value cannot be edited, when [ModelReadOnly] says.</param>
/// <param name="Editor">The special editor the value takes (MODELEDITOR-006).</param>
public sealed record ModelValueRow(string Name, Type Type, string Text, bool IsModified, bool CanEdit, IReadOnlyList<string>? Choices,
    string? Category = null, string? Description = null, bool IsRequired = false, bool IsLocalizable = false, string? ReadOnlyMessage = null,
    IReadOnlyList<string>? Suggestions = null, ModelValueEditor Editor = ModelValueEditor.Text);

/// <summary>The editors the WinForms Model Editor attaches to a value through [Editor] (docs/model-editor-scope.md, "Special editors").</summary>
public enum ModelValueEditor { Text, Criteria, Expression, Multiline, Image }

/// <summary>A field a criteria value's filter builder offers: a reference's fields by full path, a collection's by their own name.</summary>
public sealed record FilterField(string FieldName, string Caption, Type Type, bool IsCollection, IReadOnlyList<FilterField> Fields);

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

    /// <summary>
    /// The child node types that may be added under the node, keyed by caption: FastModelEditorHelper.GetChildNodeTypes,
    /// filtered as LinksNodeHelper.FilterCreatableItems does (DevExpress.ExpressApp.Win/Core/ModelEditor/LinkCollection/
    /// LinksNodeHelper.cs 76-134).
    /// </summary>
    public static IReadOnlyDictionary<string, Type> CreatableTypes(IModelNode node) {
        var target = (ModelNode)node;
        return Helper.GetChildNodeTypes(target)
            .Where(item => AllowedByFilter(target, item.Value) && AllowedByRequiredPath(target, item.Value))
            .ToDictionary(item => item.Key, item => item.Value);

        static bool AllowedByFilter(ModelNode target, Type type) =>
            !AttributeHelper.GetAttributesConsideringInterfaces<ModelVirtualTreeCreatableItemsFilterAttribute>(type, true).Any(a =>
                (a.FilteredTypes ?? []).Any(t => t.IsAssignableFrom(target.GetType()))
                || (a.FilteredTypesName ?? []).Contains(target.GetType().Name));

        static bool AllowedByRequiredPath(ModelNode target, Type type) {
            var canAdd = true;
            foreach (var attribute in AttributeHelper.GetAttributesConsideringInterfaces<ModelVirtualTreeCreatableItemsRequiredPathFilterAttribute>(type, true)) {
                canAdd = !attribute.AllowNew;
                for (ModelNode? parent = target; parent is not null; parent = parent.Parent) {
                    if ((attribute.ParentNodeType?.IsAssignableFrom(parent.GetType()) ?? false) || attribute.ParentNodeTypeName == parent.GetType().Name) {
                        canAdd = attribute.AllowNew;
                        break;
                    }
                }
            }
            return canAdd;
        }
    }

    /// <summary>
    /// Adds a child node (ModelNode.AddNode, ModelNode.cs 473-476); a new member is marked custom and calculated. AddNode returns
    /// the node in the writable layer, so the merged node, the one the tree shows, is looked up by its id.
    /// </summary>
    public static IModelNode AddChild(IModelNode parent, Type type, string id) {
        // A virtual tree item, a band under a band, is created under its real parent and owned by the selected node, as the
        // WinForms editor's add action does (ModelEditorViewController.cs 2232-2250; IModelBandsLayout.cs 71) (Codex review).
        if (RealParent(parent) is not { } realParent) {
            ThrowIfTaken(parent, id);
            ((ModelNode)parent).AddNode(id, type);
            return MarkNew(parent.GetNode(id));
        }
        ThrowIfTaken(realParent, id);
        ((ModelNode)realParent).AddNode(id, type);
        var node = MarkNew(Resolve(realParent.GetNode(id), out _)!);
        if (node is IModelBandedLayoutItem item && parent is IModelBand band) item.OwnerBand = band;
        return node;
    }

    // The node a ModelVirtualTreeAddItemAttribute on the node's type names as the real parent of the items added under it
    // (ModelAttributes.cs 373-378), the nearest such ancestor; null when the type has none.
    static IModelNode? RealParent(IModelNode node) {
        var realType = AttributeHelper.GetAttributesConsideringInterfaces<ModelVirtualTreeAddItemAttribute>(node.GetType(), true)
            .FirstOrDefault()?.RealParentNode;
        for (var n = realType is null ? null : node.Parent; n is not null; n = n.Parent) {
            if (realType!.IsInstanceOfType(n)) return n;
        }
        return null;
    }

    /// <summary>Adds a copy of the node next to it (ModelNode.AddClonedNode, ModelNode.cs 1361-1368), returned as the merged node.</summary>
    public static IModelNode Clone(IModelNode source, string id) {
        ThrowIfTaken(source.Parent, id);
        ((ModelNode)source.Parent).AddClonedNode((ModelNode)source, id);
        return MarkNew(source.Parent.GetNode(id));
    }

    // Checked before AddNode: a generated node counts too (a ListView has a hidden column for every property), and a
    // failed AddNode in the running model left the editor's selection unusable (MODELEDITOR-004 gate).
    static void ThrowIfTaken(IModelNode parent, string id) {
        if (parent.GetNode(id) is not null) throw new InvalidOperationException($"{Path(parent)} already has a node '{id}'.");
    }

    // As the WinForms editor's UpdateNewNode (ModelEditorViewController.cs 881-886).
    static IModelNode MarkNew(IModelNode node) {
        if (node is IModelMember member) {
            member.IsCustom = true;
            member.IsCalculated = true;
        }
        return node;
    }

    public static bool CanDelete(IModelNode node) => Helper.CanDeleteNode((ModelNode)node, false);

    /// <summary>
    /// Whether a copy of the node can be added next to it (FastModelEditorHelper.CanAddNode, FastModelEditorHelper.cs 312). Not
    /// tied to CanDelete: a generated member cannot be deleted, but a custom copy of it can be made (Codex review).
    /// </summary>
    public static bool CanClone(IModelNode node) => node.Parent is not null && Helper.CanAddNode((ModelNode)node.Parent, (ModelNode)node);

    /// <summary>
    /// Whether the node is still part of the model. A removed node is no longer found under its parent. The nodes Parent returns
    /// further up are other instances than GetNode hands out (measured in the test model), so a node under a removed one is
    /// caught by resolving its path again from the root.
    /// </summary>
    public static bool IsInModel(IModelNode node) {
        if (node.Parent is null) return node is IModelApplication;
        if (!ReferenceEquals(node.Parent.GetNode(Id(node)), node)) return false;
        return Resolve(node, out var root) is not null && root is IModelApplication;
    }

    /// <summary>The lookup item a drop-down shows as the text, or null.</summary>
    internal static object? LookupItem(IModelNode node, string name, string text) {
        var modelNode = (ModelNode)node;
        return modelNode.GetValueInfo(name) is { } info && LookupItems(modelNode, info) is { } items
            ? items.FirstOrDefault(i => Format(i) == text)
            : null;
    }

    /// <summary>The instance the tree hands out for the node: its path resolved again from the root.</summary>
    internal static IModelNode Canonical(IModelNode node) => Resolve(node, out _) ?? node;

    /// <summary>
    /// The node a reference value points to, for Go to (the WinForms editor's Open Related Object reads the value as a node,
    /// docs/model-editor-scope.md); null for other values and for a value that cannot be read.
    /// </summary>
    public static IModelNode? Referenced(IModelNode node, string name) {
        try {
            return ((ModelNode)node).GetValue(name) is IModelNode target ? Canonical(target) : null;
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// The value a calculated value comes from, for Source: [ModelValueCalculator]'s LinkValue on the same node, or its
    /// NodeName and PropertyName on another (ModelAttributes.cs 70-95), as ModelAttributesPropertyGridHelper.RefValue reads it
    /// (339-387). ponytail: values calculated by a calculator type or by the node path helper have no source here.
    /// </summary>
    public static (IModelNode Node, string Name)? ValueSource(IModelNode node, string name) {
        if (Helper.GetPropertyAttribute<ModelValueCalculatorAttribute>((ModelNode)node, name) is not { } calculator) return null;
        if (!string.IsNullOrEmpty(calculator.LinkValue)) return (node, calculator.LinkValue);
        // NodeName is a value of the node, "this" (IModelListView.cs 68) or a path from the root ("Application.Options",
        // IModelView.cs 64); the persistent path helper reads all three (ModelValueCalculator.cs 113-141) (Codex review).
        if (!string.IsNullOrEmpty(calculator.NodeName) && !string.IsNullOrEmpty(calculator.PropertyName)
            && SourceNode((ModelNode)node, calculator.NodeName) is { } source)
            return (source, calculator.PropertyName);
        return null;
    }

    static IModelNode? SourceNode(ModelNode node, string path) {
        try {
            return ModelNodePersistentPathHelper.FindValueByPath(node, path) is IModelNode found ? Canonical(found) : null;
        }
        catch (Exception) {
            return null;
        }
    }

    // The node's path resolved again from its root, the instance the tree hands out; null when a node on the way is gone.
    static IModelNode? Resolve(IModelNode node, out IModelNode root) {
        var ids = new Stack<string>();
        root = node;
        for (; root.Parent is not null; root = root.Parent) ids.Push(Id(root));
        IModelNode? found = root;
        while (found is not null && ids.Count > 0) found = found.GetNode(ids.Pop());
        return found;
    }

    /// <summary>
    /// Writes that put the node and the nodes under it back to the values they hold in the writable layer now, for the replay
    /// of an added or cloned node after Save (ModelEditSession.ReplaySaved): the stored values are set again (a clone carries
    /// values nobody edited) and any other value is cleared (an old grid sets Index -1 on a column saved without an Index,
    /// Codex review). Reading the children generates the ones not generated yet.
    /// </summary>
    /// <summary>The node and every node under it, depth first. Reading the children generates the ones not generated yet.</summary>
    internal static IEnumerable<IModelNode> Subtree(IModelNode root) {
        var stack = new Stack<IModelNode>();
        stack.Push(root);
        while (stack.Count > 0) {
            var node = stack.Pop();
            yield return node;
            for (var i = 0; i < node.NodeCount; i++) stack.Push(node.GetNode(i));
        }
    }

    internal static IReadOnlyList<(IModelNode Node, Action Write)> StoredValueWrites(IModelNode root) {
        var writes = new List<(IModelNode, Action)>();
        foreach (var node in Subtree(root).Cast<ModelNode>()) {
            var stored = node.NodeInfo.ValuesInfo
                .Where(info => !Bookkeeping.Contains(info.Name) && node.IsValueModified(info.Name))
                .ToDictionary(info => info.Name, info => node.GetValue(info.Name));
            writes.Add((node, () => {
                foreach (var info in node.NodeInfo.ValuesInfo) {
                    if (Bookkeeping.Contains(info.Name)) continue;
                    if (stored.TryGetValue(info.Name, out var value)) node.SetValue(info.Name, value);
                    else if (node.IsValueModified(info.Name)) node.ClearValue(info.Name);
                }
            }));
        }
        return writes;
    }

    /// <summary>The required values ([Required] or an IModelIsRequired calculator) the node has no value for.</summary>
    /// <param name="cleared">Values cleared on the node. A warmed-up model keeps returning a cleared value from its cache
    /// (ClearValue, ModelNode.cs 2368-2390), so these count as missing. ponytail: a required value XAF would calculate counts
    /// too; setting it explicitly clears the error.</param>
    public static IReadOnlyList<string> MissingRequired(IModelNode node, IEnumerable<string>? cleared = null) {
        var modelNode = (ModelNode)node;
        var clearedNames = cleared?.ToHashSet() ?? [];
        return modelNode.NodeInfo.ValuesInfo
            .Where(v => !Bookkeeping.Contains(v.Name) && Helper.IsRequired(modelNode, v.Name))
            .Where(v => clearedNames.Contains(v.Name) || modelNode.GetValue(v.Name) is null or "")
            .Select(v => v.Name)
            .ToList();
    }

    /// <summary>
    /// The Index changes that move the node one place up or down among its shown siblings, renumbering them as
    /// ModelEditorControllerBase.ChangeNodeIndex does (70-93): siblings with a negative Index are not shown and keep it.
    /// Only the indexes that change are returned; at either end nothing is.
    /// </summary>
    /// <param name="index">The Index to count for a node, when it is not the model's: an edit not applied yet.</param>
    public static IReadOnlyList<(IModelNode Node, int Index)> IndexesForMove(IModelNode node, bool up, Func<IModelNode, int?>? index = null) {
        index ??= n => n.Index;
        // Children is in the model's order and OrderBy is stable, so siblings the index does not tell apart keep that order.
        var shown = Children(node.Parent)
            .Where(n => index(n) is null or >= 0)
            .Where(n => OwnerBandId(n) == OwnerBandId(node))
            .OrderBy(n => index(n) is null)
            .ThenBy(n => index(n) ?? 0)
            .ToList();
        var position = shown.IndexOf(node);
        var target = up ? position - 1 : position + 1;
        if (position < 0 || target < 0 || target >= shown.Count) return [];
        (shown[position], shown[target]) = (shown[target], shown[position]);
        return shown.Select((n, i) => (Node: n, Index: i)).Where(p => index(p.Node) != p.Index).ToList();
    }

    // With the bands layout enabled each band numbers its own items (DxGridColumnsListEditorModelSynchronizer.cs 79-97), so a
    // move stays among the items of one owner band, the way LayoutExporter groups them (Codex review). ponytail: top-level
    // columns and top-level bands share one sequence in the grid but live under different parents; each moves among its own.
    static string? OwnerBandId(IModelNode node) =>
        node is IModelBandedLayoutItem { OwnerBand: { } band } && BandsEnabled(node) ? band.Id : null;

    static bool BandsEnabled(IModelNode node) => node.Parent switch {
        IModelBandsLayout layout => layout.Enable,
        { Parent: IModelListView view } => view.BandsLayout.Enable,
        _ => false,
    };

    /// <summary>The ids from below the root down to the node, joined with '/': "Views/Order_ListView".</summary>
    public static string Path(IModelNode node) => string.Join("/", Ids(node));

    /// <summary>The ids from below the root down to the node. Compare these, not paths: an id may contain '/' (Codex review).</summary>
    internal static IReadOnlyList<string> Ids(IModelNode node) {
        var ids = new List<string>();
        for (var n = node; n.Parent is not null; n = n.Parent) ids.Add(Id(n));
        ids.Reverse();
        return ids;
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

    /// <summary>
    /// The special editor of a value, from the type name its [Editor] attribute gives the WinForms Model Editor (for example
    /// IModelListView.cs 102, CommonInterfaces.cs 295 and 562, IModelView.cs 59); nothing of WinForms is loaded.
    /// </summary>
    public static ModelValueEditor SpecialEditor(IModelNode node, string name) =>
        Helper.GetPropertyAttribute<EditorAttribute>((ModelNode)node, name)?.EditorTypeName switch {
            Constants.MultilineStringEditorType => ModelValueEditor.Multiline,
            { } editor when editor.Contains("CriteriaModelEditorControl", StringComparison.Ordinal) => ModelValueEditor.Criteria,
            { } editor when editor.Contains("ExpressionModelEditorControl", StringComparison.Ordinal) => ModelValueEditor.Expression,
            { } editor when editor.Contains("ImageGalleryModelEditorControl", StringComparison.Ordinal) => ModelValueEditor.Image,
            _ => ModelValueEditor.Text,
        };

    /// <summary>
    /// The type a criteria value filters: [CriteriaOptions].ObjectTypeMemberName, comma-separated paths from the node, the first
    /// that resolves, as CriteriaModelEditorControl.CalculateFilteredTypeInfo does (DevExpress.ExpressApp.Win/Core/ModelEditor/
    /// AttributeList/CriteriaModelEditorControl.cs 91-137). Null for other values and when no path resolves.
    /// </summary>
    public static ITypeInfo? CriteriaTypeInfo(IModelNode node, string name) {
        if (Helper.GetPropertyAttribute<CriteriaOptionsAttribute>((ModelNode)node, name)?.ObjectTypeMemberName is not { Length: > 0 } paths) return null;
        foreach (var path in paths.Split(',')) {
            object? value;
            try {
                value = ModelNodePersistentPathHelper.FindValueByPath((ModelNode)node, path.Trim());
            }
            catch (Exception) {
                continue;
            }
            if (value switch {
                ITypeInfo typeInfo => typeInfo,
                Type type => XafTypesInfo.Instance.FindTypeInfo(type),
                string typeName => XafTypesInfo.Instance.FindTypeInfo(typeName),
                IMemberInfo member => member.IsList ? member.ListElementTypeInfo : member.MemberTypeInfo,
                _ => null,
            } is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// The fields a filter builder offers for the type: the members XAF Blazor's criteria editor shows (the public
    /// DxFilterBuilderHelper.GetMembers, DxFilterBuilderAdapter.cs 205-259), a reference's fields by full path and a collection's
    /// by their own name, as DxFilterBuilderField nests them. ponytail: <paramref name="depth"/> levels, since references can cycle.
    /// </summary>
    public static IReadOnlyList<FilterField> FilterFields(ITypeInfo typeInfo, int depth = 2) {
        using var helper = new DxFilterBuilderHelper(typeInfo, null, null, null);
        return Fields(typeInfo, "", depth);

        // XAF's rule (DxFilterBuilderHelper.GetFieldModel, DxFilterBuilderAdapter.cs 301-308, 335-337): the type without Nullable,
        // a list is a collection field over its elements, and any other member of a non-simple type that is no image has nested
        // fields, a persistent EF Core entity as much as a domain component (Codex review 2).
        IReadOnlyList<FilterField> Fields(ITypeInfo owner, string prefix, int levels) =>
            helper.GetMembers(owner).Select(member => {
                var type = Nullable.GetUnderlyingType(member.MemberType) ?? member.MemberType;
                var element = member.IsList ? member.ListElementTypeInfo : member.MemberTypeInfo;
                var nests = member.IsList || (!SimpleTypes.IsSimpleType(type) && !typeof(System.Drawing.Image).IsAssignableFrom(type));
                IReadOnlyList<FilterField> nested = levels > 1 && element is not null && nests
                    ? Fields(element, member.IsList ? "" : prefix + member.Name + ".", levels - 1)
                    : [];
                return new FilterField(prefix + member.Name, CaptionHelper.GetMemberCaption(owner, member.Name), type, member.IsList, nested);
            }).ToList();
    }

    // The names of the SVG and PNG images every image source offers, as the WinForms image picker lists them
    // (ImageSource.GetImages(ImagePickerMode), ImageLoader.cs 79, 391, 1454; the DevExpress images source overrides only
    // GetImages, 1021). ponytail: read once per process, since GetImages loads the images; none before ImageLoader is
    // initialized (the in-process model), and a source added later is not listed.
    static IReadOnlyList<string>? ImageNames() => ImageLoader.IsInitialized ? imageNames.Value : null;

    static readonly Lazy<IReadOnlyList<string>> imageNames = new(() => ImageLoader.Instance.ImageSources
        .SelectMany(source => new[] { ImagePickerMode.SvgImages, ImagePickerMode.PngImages }.SelectMany(mode => source.GetImages(mode).Values))
        .SelectMany(images => images)
        .Select(image => image.ImageName)
        .Where(name => !string.IsNullOrEmpty(name))
        .Distinct()
        .Order(StringComparer.Ordinal)
        .ToList());

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

    /// <summary>
    /// Writes a value typed as text. Empty text sets an optional reference to none and clears any other value that is not a
    /// string, so the layers below apply again.
    /// </summary>
    public static void SetText(IModelNode node, string name, string text) {
        var modelNode = (ModelNode)node;
        if (Parse(modelNode, name, text) is (true, var value)) modelNode.SetValue(name, value);
        else Reset(node, name);
    }

    /// <summary>
    /// Clears the value. A reference value read from stored differences is kept under its helper name only ({Name}_ID,
    /// ModelValuePersistentPathCalculator.GetHelperValueName, ModelValueCalculator.cs 58, 96-98; ModelNode.cs 3149-3166), which
    /// ClearValue of the value's own name does not reach (ModelNode.cs 2368-2390), so that one is cleared too (MODELEDITOR-005
    /// gate, docs/devexpress-support-request.md item 10).
    /// </summary>
    public static void Reset(IModelNode node, string name) {
        var modelNode = (ModelNode)node;
        modelNode.ClearValue(name);
        if (!string.IsNullOrEmpty(modelNode.GetValueInfo(name)?.PersistentPath))
            modelNode.ClearValue(ModelValuePersistentPathCalculator.GetHelperValueName(name));
    }

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

    /// <summary>The value the text stands for, (false, _) for a clear; throws when it is none. Changes nothing.</summary>
    internal static (bool Set, object? Value) ParseText(IModelNode node, string name, string text) => Parse((ModelNode)node, name, text);

    internal static void SetValue(IModelNode node, string name, object? value) => ((ModelNode)node).SetValue(name, value);

    /// <summary>Whether the value is chosen from a lookup list ([DataSourceProperty]).</summary>
    internal static bool IsLookupValue(IModelNode node, string name) =>
        !string.IsNullOrEmpty(((ModelNode)node).GetValueInfo(name)?.PersistentPath);

    /// <summary>Throws when the text is no value of the named value's type; changes nothing.</summary>
    internal static void Validate(IModelNode node, string name, string text) => Parse((ModelNode)node, name, text);

    // (false, _): clear the value.
    static (bool Set, object? Value) Parse(ModelNode node, string name, string text) {
        var info = node.GetValueInfo(name) ?? throw new ArgumentException($"'{name}' is not a value of {Path(node)}.", nameof(name));
        var type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        if (type == typeof(string)) return (true, text);
        // The empty choice of an optional reference is "none", which the differences keep as an empty helper value (ModelNode.cs
        // 3115-3126); a clear would bring the calculated or inherited value back (Codex review). A required one is cleared.
        if (text.Length == 0)
            return (typeof(IModelNode).IsAssignableFrom(type) && !string.IsNullOrEmpty(info.PersistentPath) && !Helper.IsRequired(node, name), null);
        // A reference or type value takes one of its lookup items, by the text the drop-down shows (MODELEDITOR-005).
        if (LookupItems(node, info) is { } items)
            return (true, items.FirstOrDefault(i => Format(i) == text) ?? throw new FormatException($"'{text}' is not one of the values {name} can take."));
        return (true, TypeDescriptor.GetConverter(type).ConvertFromInvariantString(text));
    }

    // Strings, primitives and enums are editable, and references and types that have a lookup list (MODELEDITOR-005).
    // ponytail: a type without a list and criteria stay read-only until they get their own editors (MODELEDITOR-006).
    static ModelValueRow Row(ModelNode node, ModelValueInfo info) {
        var type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        var readOnly = Helper.IsReadOnly(node, info.Name);
        var editor = SpecialEditor(node, info.Name);
        var editable = !readOnly;
        IReadOnlyList<object>? lookup = null;
        string text;
        try {
            text = Format(node.GetValue(info.Name));
            if (editable) lookup = LookupItems(node, info);
        }
        catch (Exception ex) {
            // A value calculator or a lookup criteria can throw on a node it does not expect; show that instead of failing the grid.
            text = $"({ex.GetType().Name}: {ex.Message})";
            editable = false;
        }
        editable &= type == typeof(string) || type.IsPrimitive || type.IsEnum || lookup is not null;
        string[]? choices = !editable ? null
            : lookup is not null ? lookup.Select(Format).ToArray()
            : type == typeof(bool) ? ["True", "False"] : type.IsEnum ? Enum.GetNames(type) : null;
        return new(info.Name, info.PropertyType, text, node.IsValueModified(info.Name), editable, choices,
            Category: Helper.GetPropertyAttribute<CategoryAttribute>(node, info.Name)?.Category,
            Description: Helper.GetPropertyDescription(node, info.Name),
            IsRequired: Helper.IsRequired(node, info.Name),
            IsLocalizable: info.IsLocalizable,
            ReadOnlyMessage: readOnly ? Helper.GetPropertyAttribute<ModelReadOnlyAttribute>(node, info.Name)?.Message : null,
            Suggestions: editable && choices is null ? (editor == ModelValueEditor.Image ? ImageNames() : Suggestions(node, info.Name)) : null,
            Editor: editor);
    }

    /// <summary>
    /// What a value with a [DataSourceProperty] can take, for a drop-down; null when it has none. Built as the WinForms editor
    /// builds its lookup (ModelAttributesPropertyGridHelper.cs 800-849, 448-478, 696-705): the list the path names, split at its
    /// last dot into a node and one of its list members; the items of the value's type that fit [DataSourceCriteria]; nodes
    /// in tree order, and views of a class or list view by class inheritance (ViewNamesCalculator, ModelViewLogic.cs 344).
    /// </summary>
    internal static IReadOnlyList<object>? LookupItems(ModelNode node, ModelValueInfo info) {
        if (string.IsNullOrEmpty(info.PersistentPath)) return null;
        IEnumerable? list;
        Type? elementType = null;
        if (info.PersistentPath == "this") list = node as IEnumerable;
        else {
            var dot = info.PersistentPath.LastIndexOf('.');
            var source = dot < 0 ? node : ModelNodePersistentPathHelper.FindValueByPath(node, info.PersistentPath[..dot]) as ModelNode;
            var member = source is null ? null : XafTypesInfo.Instance.FindTypeInfo(source.GetType()).FindMember(info.PersistentPath[(dot + 1)..]);
            if (member is not { IsList: true }) return null;
            elementType = member.ListElementType;
            list = member.GetValue(source) as IEnumerable;
        }
        if (list is null) return null;
        ExpressionEvaluator? evaluator = null;
        if (elementType is not null && Helper.GetPropertyAttribute<DataSourceCriteriaAttribute>(node, info.Name) is { } criteria) {
            var wrapper = new CriteriaWrapper(criteria.Value.ToString(), node);
            wrapper.UpdateParametersValues(node);
            evaluator = new ExpressionEvaluator(new EvaluatorContextDescriptorDefault(elementType), wrapper.CriteriaOperator, false, null);
        }
        var type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        var items = list.Cast<object>().Where(i => type.IsInstanceOfType(i) && (evaluator is null || evaluator.Fit(i))).ToList();
        if (items.All(i => i is IModelNode)) items = items.Cast<IModelNode>().Order(TreeOrder).Cast<object>().ToList();
        var ownerClass = node switch { IModelClass c => c, IModelListView v => v.ModelClass, _ => null };
        if (ownerClass is not null && items.Count > 0 && items.All(i => i is IModelObjectView))
            items = ViewNamesCalculator.SortByInheritanceHierarchy(items.Cast<IModelView>().ToList(), ownerClass).Cast<object>().ToList();
        return items;
    }

    // Free-text suggestions where the WinForms editor offers a field picker or a language combo (ModelAttributesPropertyGridHelper.cs
    // 388-447, 850-856). ponytail: the class's member names; a path through a reference (Customer.Name) is typed in.
    static IReadOnlyList<string>? Suggestions(ModelNode node, string name) => name switch {
        "PreferredLanguage" when node.Root is ModelApplicationBase root =>
            [CaptionHelper.DefaultLanguage, CaptionHelper.UserLanguage, .. root.GetAspectNames().Order(StringComparer.Ordinal)],
        "PropertyName" => MemberNames(node switch {
            IModelMemberViewItem item => (item.ParentView as IModelObjectView)?.ModelClass,
            IModelSortProperty { Parent: IModelSorting { Parent: IModelObjectView view } } => view.ModelClass,
            _ => null,
        }),
        "LookupProperty" when node is IModelMemberViewItem item && (item.ParentView as IModelObjectView)?.ModelClass is { } owner =>
            MemberNames(owner.TypeInfo.FindMember(item.PropertyName)?.MemberTypeInfo.Type is { } memberType
                ? item.Application.BOModel.GetClass(memberType) : null),
        "TargetPropertyName" when node.GetValueInfo("TargetType") is not null && node.GetValue("TargetType") is Type target =>
            MemberNames(((IModelNode)node).Application.BOModel.GetClass(target)),
        _ => null,
    };

    static IReadOnlyList<string>? MemberNames(IModelClass? modelClass) =>
        modelClass?.AllMembers.Select(m => m.Name).Order(StringComparer.Ordinal).ToList();

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
        ThrowIfUnderPendingReset(node);
        ThrowIfLookupConflict(node, name);
        // A node added in this session is removed again unless saved, so its values are written at once: a required value XAF
        // calculates from another one is there only then (the gate: a new column's PropertyEditorType stayed missing while
        // its PropertyName was pending).
        try {
            ModelEditing.Validate(node, name, text);
        }
        catch (Exception ex) {
            // On an added node too: the invalid text blocks Save until a valid value replaces it (Codex review).
            pending[(node, name)] = (text, ex.Message);
            throw;
        }
        if (IsAddedOrUnder(node)) {
            pending.Remove((node, name));
            ModelEditing.SetText(node, name, text);
            // Written at once, yet chosen from a model without the pending edits: later edits check it like a pending one.
            if (ModelEditing.IsLookupValue(node, name)) addedLookups.Add((node, name));
            // Empty text clears the value, or sets an optional reference to none (ModelEditing.SetText); cleared only counts
            // for required values, so the reference entry is harmless.
            if (text.Length == 0) cleared.Add((node, name));
            else cleared.Remove((node, name));
        }
        else pending[(node, name)] = (text, null);
    }

    public void Reset(IModelNode node, string name) {
        ThrowIfUnderPendingReset(node);
        ThrowIfLookupConflict(node, name);
        // ponytail: on an added node a warmed-up model shows the cleared value until the reload (ClearValue skips the cache).
        if (IsAddedOrUnder(node)) {
            pending.Remove((node, name));
            ModelEditing.Reset(node, name);
            cleared.Add((node, name));
        }
        else pending[(node, name)] = (null, null);
    }

    // An error on a node marked for deletion does not count: the edit goes with the node (Codex review).
    public bool HasErrors => pending.Any(p => p.Value.Error is not null && !IsDeleted(p.Key.Node));

    /// <summary>
    /// The node a reference value points to as the editor shows it, for Go to: a pending selection before the model's value
    /// (Codex review). A pending reset has no target until the model is built again.
    /// </summary>
    public IModelNode? Referenced(IModelNode node, string name) {
        if (!pending.TryGetValue((node, name), out var entry)) return ModelEditing.Referenced(node, name);
        return entry is { Error: null, Text.Length: > 0 } && ModelEditing.LookupItem(node, name, entry.Text) is IModelNode target
            ? ModelEditing.Canonical(target)
            : null;
    }

    /// <param name="text">The pending text, or null for a pending reset.</param>
    /// <param name="error">Why the text is no value of that type, or null.</param>
    public bool TryGetPending(IModelNode node, string name, out string? text, out string? error) {
        var found = pending.TryGetValue((node, name), out var entry);
        (text, error) = entry;
        return found;
    }

    // MODELEDITOR-004. Nodes added or cloned exist in the live model at once, and their values are written at once; they are
    // removed again unless saved. Deletes and node resets wait for Apply like value edits.
    readonly List<IModelNode> added = [];
    readonly HashSet<IModelNode> deletes = [];
    readonly HashSet<IModelNode> nodeResets = [];
    // Values the editor cleared on added nodes or nodes under them, which a warmed-up model still returns from its cache (Codex review).
    readonly HashSet<(IModelNode Node, string Name)> cleared = [];
    // Lookup values the editor set on added nodes or nodes under them (Codex review 5).
    readonly HashSet<(IModelNode Node, string Name)> addedLookups = [];

    public IModelNode AddChild(IModelNode parent, Type type, string id) {
        ThrowIfUnderPendingReset(parent);
        var node = ModelEditing.AddChild(parent, type, id);
        added.Add(node);
        return node;
    }

    public IModelNode Clone(IModelNode source, string id) {
        ThrowIfUnderPendingReset(source.Parent);
        // The clone copies the model as it is, while the editor shows the source with its pending edits, and what a pending
        // reset leaves is known only once the model is built again. So a source with pending edits is saved first (Codex review).
        if (pending.Keys.Any(k => IsAtOrUnder(k.Node, [source])) || deletes.Any(d => IsAtOrUnder(d, [source]))
            || nodeResets.Any(r => IsAtOrUnder(r, [source])))
            throw new InvalidOperationException($"Save the pending edits of {ModelEditing.Path(source)} before cloning it.");
        var node = ModelEditing.Clone(source, id);
        added.Add(node);
        return node;
    }

    public bool IsAdded(IModelNode node) => added.Contains(node);

    bool IsAddedOrUnder(IModelNode node) => IsAtOrUnder(node, added);

    // By ids: the instances Parent returns are not the tree's own (ModelEditing.IsInModel), and an id may contain '/'.
    static bool IsAtOrUnder(IModelNode node, IEnumerable<IModelNode> roots) {
        var ids = ModelEditing.Ids(node);
        return roots.Any(r => ModelEditing.Ids(r) is var prefix && prefix.Count <= ids.Count && prefix.SequenceEqual(ids.Take(prefix.Count)));
    }

    /// <summary>Marks the node for deletion on Apply, or takes that mark back.</summary>
    public void Delete(IModelNode node, bool delete = true) {
        if (delete) {
            ThrowIfUnderPendingReset(node);
            ThrowIfPendingLookup(deleting: node);
            deletes.Add(node);
        }
        else deletes.Remove(node);
    }

    public bool IsPendingDelete(IModelNode node) => deletes.Contains(node);

    /// <summary>Takes back every difference of the node on Apply (ModelNode.Undo, ModelNode.cs 609-637).</summary>
    public void ResetNode(IModelNode node) {
        ThrowIfPendingLookup();
        if (!CanResetNode(node))
            throw new InvalidOperationException($"{ModelEditing.Path(node)} exists only in your model differences; delete it instead of resetting it.");
        if (pending.Keys.Any(k => IsAtOrUnder(k.Node, [node])) || deletes.Any(d => IsAtOrUnder(d, [node]))
            || added.Any(a => IsAtOrUnder(a, [node])))
            throw new InvalidOperationException($"Save the pending edits under {ModelEditing.Path(node)} before resetting it.");
        nodeResets.Add(node);
    }

    // A lookup's choices are worked out from the model as it is, and can depend on other values, of the node or elsewhere (a
    // view's DetailView on its ModelClass, a column's PropertyEditorType on its PropertyName). Pending edits are not in the model
    // yet, so a lookup edit is the only pending edit until a Save, in either order (Codex review). ponytail: stricter than the
    // actual dependencies, which the model's domain logic knows and the editor does not.
    void ThrowIfLookupConflict(IModelNode node, string name) {
        // On an added node too: its lookup edit is written at once, but a view marked for deletion is still offered (Codex review 5).
        if (ModelEditing.IsLookupValue(node, name)
            && (pending.Keys.Any(k => !(ReferenceEquals(k.Node, node) && k.Name == name)) || deletes.Count > 0 || nodeResets.Count > 0))
            throw new InvalidOperationException($"Save the pending edits first: the choices of {name} are worked out from the saved model.");
        if (!IsAddedOrUnder(node)) ThrowIfPendingLookup((node, name));
    }

    // Pending lookup edits and the ones written to added nodes; one on or under the node being deleted goes with it.
    void ThrowIfPendingLookup((IModelNode Node, string Name)? except = null, IModelNode? deleting = null) {
        foreach (var (lookupNode, lookupName) in pending.Keys.Concat(addedLookups)) {
            if (except is { } e && ReferenceEquals(e.Node, lookupNode) && e.Name == lookupName) continue;
            if (deleting is not null && IsAtOrUnder(lookupNode, [deleting])) continue;
            if (ModelEditing.IsLookupValue(lookupNode, lookupName))
                throw new InvalidOperationException($"Save the edit of {ModelEditing.Path(lookupNode)} {lookupName} first: its choices were worked out before the other edits.");
        }
    }

    // A node reset takes back the node's whole subtree (ModelNode.Undo, 609-637), on Apply and again on the replay after Save,
    // so it does not combine with other edits in that subtree; the editor asks for a Save in between (Codex review).
    void ThrowIfUnderPendingReset(IModelNode node) {
        if (nodeResets.Any(r => IsAtOrUnder(node, [r])))
            throw new InvalidOperationException($"{ModelEditing.Path(node)} is reset on save; save before editing it.");
    }

    /// <summary>
    /// Undo clears the node's values but keeps the node (ModelNode.UndoCore, 615-637), so a node that exists only in the user's
    /// differences, one added in the editor or under one among them, would stay without its required values (Codex review).
    /// </summary>
    public bool CanResetNode(IModelNode node) => !IsAddedOrUnder(node) && !((ModelNode)node).IsNewNode;

    public bool IsPendingNodeReset(IModelNode node) => nodeResets.Contains(node);

    /// <summary>Moves the node one place up or down among its shown siblings, as Index edits that count earlier moves (Codex review).</summary>
    public void Move(IModelNode node, bool up) {
        var moves = ModelEditing.IndexesForMove(node, up, EffectiveIndex);
        // Every sibling is checked first, so a refused move leaves no partial renumbering behind: an added sibling's Index is
        // written at once (Codex reviews).
        foreach (var (sibling, _) in moves) {
            ThrowIfUnderPendingReset(sibling);
            ThrowIfLookupConflict(sibling, ModelValueNames.Index);
        }
        foreach (var (sibling, index) in moves)
            SetText(sibling, ModelValueNames.Index, index.ToString(CultureInfo.InvariantCulture));
    }

    // The Index a node has once the pending edits are applied; ponytail: a pending reset counts the current Index.
    int? EffectiveIndex(IModelNode node) =>
        pending.TryGetValue((node, ModelValueNames.Index), out var p)
        && int.TryParse(p.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : node.Index;

    /// <summary>
    /// Removes the nodes this session added: for an editor closed without Save, and before a model save the editor did not
    /// start (ModelEditorController). Last added first, so clones of clones go too. Edits of the removed nodes go with them.
    /// </summary>
    public void RollbackAdded() {
        for (var i = added.Count - 1; i >= 0; i--) {
            if (ModelEditing.IsInModel(added[i])) added[i].Remove();
        }
        added.Clear();
        cleared.Clear();
        addedLookups.Clear();
        foreach (var key in pending.Keys.Where(k => !ModelEditing.IsInModel(k.Node)).ToList()) pending.Remove(key);
        deletes.RemoveWhere(n => !ModelEditing.IsInModel(n));
        nodeResets.RemoveWhere(n => !ModelEditing.IsInModel(n));
    }

    /// <summary>Set by the editor around its own model save, which keeps the nodes it added.</summary>
    public bool Saving { get; set; }

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
        if (pending.FirstOrDefault(p => p.Value.Error is not null && !IsDeleted(p.Key.Node)) is { Value.Error: { } error } invalid)
            throw new InvalidOperationException($"{ModelEditing.Path(invalid.Key.Node)}: {invalid.Key.Name} is not saved: {error}");
        // Every node of an added or cloned subtree is saved only with its required values (MODELEDITOR-004); a clone copies its
        // source's children (Codex review). Their values are in the model already.
        var subtrees = added.Where(n => !IsDeleted(n) && ModelEditing.IsInModel(n)).SelectMany(ModelEditing.Subtree);
        foreach (var node in subtrees.Where(n => !IsDeleted(n))) {
            var ids = ModelEditing.Ids(node);
            var missing = ModelEditing.MissingRequired(node, cleared.Where(c => ModelEditing.Ids(c.Node).SequenceEqual(ids)).Select(c => c.Name));
            if (missing.Count > 0)
                throw new InvalidOperationException($"{ModelEditing.Path(node)}: {string.Join(", ", missing)} required");
        }
        if (userLayer is null) WritePending();
        else {
            emptiedAspects.UnionWith(ModelEditing.AspectsEmptiedBy(userLayer, WritePending));
            // An aspect a later edit filled again holds that edit now; blanking its row would lose it (Codex review 4).
            emptiedAspects.IntersectWith(ModelEditing.EmptyAspects(userLayer));
        }
    }

    /// <summary>The applied edits are saved and their emptied aspects blanked; the nodes added are part of the model now.</summary>
    public void Saved() {
        // An added or cloned node's own values count as saved edits: a clone carries values nobody edited (Codex review).
        foreach (var node in added) savedWrites.AddRange(ModelEditing.StoredValueWrites(node));
        savedWrites.AddRange(writes);
        writes.Clear();
        emptiedAspects.Clear();
        added.Clear();
        cleared.Clear();
        addedLookups.Clear();
    }

    // The session's writes to the model since the last Save, and the saved ones (ReplaySaved).
    readonly List<(IModelNode Node, Action Write)> writes = [];
    readonly List<(IModelNode Node, Action Write)> savedWrites = [];

    /// <summary>
    /// Writes the saved edits again. A later model save of this circuit, the deferred one XAF flushes after the reload, first
    /// lets the views built before the edits write their own state into the model; a grid hides every column it does not
    /// show (ColumnsListEditor.cs 230-232). ModelEditorController calls this right before such a save. Nodes no longer in
    /// the model are skipped.
    /// </summary>
    public void ReplaySaved() {
        foreach (var (node, write) in savedWrites) {
            if (!ModelEditing.IsInModel(node)) continue;
            try {
                write();
            }
            catch (Exception ex) {
                // Thrown here it would fail XAF's own save of the user model; the value stays as the view left it.
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
            }
        }
    }

    // A node marked for deletion, or one under such a node; by path, as IsAddedOrUnder (Codex review).
    bool IsDeleted(IModelNode node) => IsAtOrUnder(node, deletes);

    // Values first, then node resets, then deletes; nothing is written to a node that goes.
    void WritePending() {
        // Every text becomes its value before anything is written, so a refusal cannot come after a first write has reached
        // the live model (Codex review).
        var values = pending
            .Where(p => !IsDeleted(p.Key.Node))
            .Select(p => (p.Key.Node, p.Key.Name, Parsed: p.Value.Text is null ? (Set: false, Value: null) : ModelEditing.ParseText(p.Key.Node, p.Key.Name, p.Value.Text)))
            .ToList();
        foreach (var (node, name, (set, value)) in values) {
            Action write = set ? () => ModelEditing.SetValue(node, name, value) : () => ModelEditing.Reset(node, name);
            write();
            writes.Add((node, write));
        }
        pending.Clear();
        foreach (var node in nodeResets.Where(n => !IsDeleted(n))) {
            Action write = () => ((ModelNode)node).Undo();
            write();
            writes.Add((node, write));
        }
        nodeResets.Clear();
        // Only the outermost deleted nodes; their children go with them.
        foreach (var node in deletes.Where(n => n.Parent is null || !IsDeleted(n.Parent)).ToList()) {
            node.Remove();
            added.RemoveAll(a => IsAtOrUnder(a, [node]));
            cleared.RemoveWhere(c => IsAtOrUnder(c.Node, [node]));
            addedLookups.RemoveWhere(l => IsAtOrUnder(l.Node, [node]));
        }
        deletes.Clear();
    }
}
