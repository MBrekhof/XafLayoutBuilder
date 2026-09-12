using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;
using DxFlow = DevExpress.ExpressApp.Layout.FlowDirection;
using DxSort = DevExpress.Data.ColumnSortOrder;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Walks a merged model view (all layers applied) back into a spec. Only values a layer actually set are exported, so
/// XAF's computed defaults do not turn into builder calls. Anything the builder cannot express (a layout item that is
/// not a property editor, a nested property path) is skipped and reported to the caller instead of printed.
/// Member names come from <c>PropertyName</c>, not from the node id: the two usually match, but only the property
/// name is a CLR member the printed lambda can name.
/// </summary>
public static class LayoutExporter {
    public static (DetailLayoutSpec Spec, IReadOnlyList<string> Skipped) ExportDetail(IModelDetailView view) {
        var skipped = new List<string>();
        var type = view.ModelClass.TypeInfo.Type;
        // The stock root is one plain "Main" group holding everything. A root group with its own settings is a
        // customisation, so it is exported as an ordinary group rather than silently unwrapped.
        IEnumerable<IModelViewLayoutElement> roots = view.Layout;
        if (view.Layout.Count == 1 && view.Layout[0] is IModelLayoutGroup main && IsStockRoot(main)) roots = main;
        var rootElements = roots.OrderBy(e => e.Index ?? int.MaxValue).ToList();

        // A group this module generated for .Unplaced(...) is reconstructed as the policy, not as explicit items:
        // freezing today's leftovers into a group would quietly restore strict XLB002 for the next new property.
        string? unplacedGroupId = null;
        var fromCatchAll = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in rootElements.OfType<IModelLayoutGroup>().Where(IsCatchAll).ToList()) {
            if (unplacedGroupId is not null) {
                skipped.Add($"skipped: more than one catch-all group; \"{element.Id}\" was exported as an ordinary group");
                continue;
            }
            unplacedGroupId = element.Id;
            foreach (var member in element.OfType<IModelLayoutViewItem>()
                         .Select(i => (i.ViewItem as IModelPropertyEditor)?.PropertyName)
                         .OfType<string>())
                fromCatchAll.Add(member);
            if (element.Count != fromCatchAll.Count)
                skipped.Add($"note: the catch-all group \"{element.Id}\" holds something other than plain editors; only .Unplaced(...) was exported");
            rootElements.Remove(element);
        }

        var nodes = rootElements.Select(e => Convert(e, inTab: false)).OfType<LayoutNodeSpec>().ToList();
        var placed = new HashSet<string>(new DetailLayoutSpec(type.FullName!, nodes, []).Members(), StringComparer.Ordinal);
        var hidden = view.Items.OfType<IModelPropertyEditor>()
            .Where(pe => pe.ModelMember?.IsVisibleInDetailView != false)
            .Select(pe => Simple(pe.PropertyName, $"hidden member \"{((IModelViewItem)pe).Id}\""))
            .OfType<string>()
            // What sat in the catch-all group is not hidden: the policy will collect it again.
            .Where(name => !placed.Contains(name) && !fromCatchAll.Contains(name))
            .ToList();
        return (new DetailLayoutSpec(type.FullName!, nodes, hidden, unplacedGroupId), skipped);

        LayoutNodeSpec? Convert(IModelViewLayoutElement element, bool inTab) {
            switch (element) {
                case IModelTabbedGroup tabs:
                    return new TabbedGroupSpec(tabs.Id, tabs.OrderBy(t => t.Index ?? int.MaxValue).Select(t => ConvertGroup(t, inTab: true)).ToList());
                case IModelLayoutGroup group:
                    return ConvertGroup(group, inTab);
                case IModelLayoutViewItem item:
                    if (item.ViewItem is IModelPropertyEditor pe) {
                        var member = Simple(pe.PropertyName, $"layout item \"{item.Id}\"");
                        return member is null ? null : new LayoutItemSpec(member, Explicit(item, "RelativeSize") ? item.RelativeSize : null);
                    }
                    skipped.Add($"skipped: {item.ViewItem?.GetType().Name ?? "layout item"} \"{item.Id}\" is not a property editor");
                    return null;
                default:
                    skipped.Add($"skipped: {element.GetType().Name} \"{element.Id}\"");
                    return null;
            }
        }

        LayoutGroupSpec ConvertGroup(IModelLayoutGroup group, bool inTab) => new(
            group.Id,
            group.OrderBy(e => e.Index ?? int.MaxValue).Select(e => Convert(e, inTab: false)).OfType<LayoutNodeSpec>().ToList(),
            Caption: GroupCaption(group, inTab),
            Direction: group.Direction == DxFlow.Horizontal ? FlowDirection.Horizontal : FlowDirection.Vertical,
            Collapsible: group.IsCollapsibleGroup,
            RelativeSize: Explicit(group, "RelativeSize") ? group.RelativeSize : null,
            ImageName: string.IsNullOrEmpty(group.ImageName) ? null : group.ImageName);

        string? Simple(string? name, string what) {
            if (string.IsNullOrEmpty(name)) { skipped.Add($"skipped: {what} has no property name"); return null; }
            if (name.Contains('.')) { skipped.Add($"skipped: {what} uses the nested path \"{name}\", which the builder cannot express"); return null; }
            return name;
        }
    }

    static bool IsCatchAll(IModelLayoutGroup group) => ((ModelNode)group).GetValue<bool>(DetailViewLayoutUpdater.CatchAllMarker);

    /// <summary>
    /// Caption is localizable, so `HasValue` looks in the current language aspect and misses a generated value.
    /// Compare with XAF's own default instead. A caption equal to that default still has to be printed unless the
    /// spec would show it anyway, because the applier only shows a caption for a captioned, collapsible or tab group.
    /// </summary>
    static string? GroupCaption(IModelLayoutGroup group, bool inTab) {
        if (group.ShowCaption != true) return null;
        if (group.Caption != DefaultCaption(group)) return group.Caption;
        return group.IsCollapsibleGroup || inTab ? null : group.Caption;
    }

    // ModelLayoutGroupLogic.Get_Caption: the single view item's caption for a one-item group, otherwise the group id.
    static string DefaultCaption(IModelLayoutGroup group) =>
        group.Count == 1 && group[0] is IModelLayoutViewItem { ViewItem: { } item } ? item.Caption : group.Id;

    static bool IsStockRoot(IModelLayoutGroup group) =>
        group.Id == ModelDetailViewLayoutNodesGenerator.MainLayoutGroupName
        && group.ShowCaption != true && !group.IsCollapsibleGroup && group.Direction == DxFlow.Vertical
        && !Explicit(group, "RelativeSize") && string.IsNullOrEmpty(group.ImageName);

    /// <summary>
    /// Every column with an index is listed; every other column is exported as hidden, except the key, which XAF never
    /// shows by default. Hidden and merely unmentioned columns are indistinguishable in the model, so the export is
    /// explicit where the original builder may have been silent. A hidden column's own sort order is not exported:
    /// the applier clears it, so it cannot round-trip.
    /// </summary>
    public static (ListColumnsSpec Spec, IReadOnlyList<string> Skipped) ExportColumns(IModelListView view, IModelListView? lookupView) {
        var skipped = new List<string>();
        var type = view.ModelClass.TypeInfo.Type;
        var key = view.ModelClass.KeyProperty;
        var spec = new ListColumnsSpec(type.FullName!, Columns(view), Hidden(view),
            lookupView is null ? null : new ListColumnsSpec(type.FullName!, Columns(lookupView), Hidden(lookupView)));
        return (spec, skipped);

        List<ColumnSpec> Columns(IModelListView v) => v.Columns
            .Where(c => c.Index is >= 0)
            .OrderBy(c => c.Index)
            .Select(c => new { Column = c, Member = Simple(c) })
            .Where(x => x.Member is not null)
            .Select(c => new ColumnSpec(
                c.Member!,
                Explicit(c.Column, "Width") ? c.Column.Width : null,
                c.Column.SortOrder switch { DxSort.Ascending => ColumnSortOrder.Ascending, DxSort.Descending => ColumnSortOrder.Descending, _ => ColumnSortOrder.None },
                // Localizable like a group caption: compare with the member caption XAF falls back to.
                c.Column.Caption != c.Column.ModelMember?.Caption ? c.Column.Caption : null))
            .ToList();

        List<string> Hidden(IModelListView v) => v.Columns
            .Where(c => c.Index is null or < 0)
            .Select(Simple)
            .OfType<string>()
            .Where(member => member != key)
            .ToList();

        string? Simple(IModelColumn column) {
            var name = column.PropertyName;
            if (string.IsNullOrEmpty(name)) { skipped.Add($"skipped: column \"{column.Id}\" has no property name"); return null; }
            // A column over a reference's member ("Customer.City") prints as a chained lambda (NEST-001). A path that casts
            // to a descendant class ("<Descendant>Member", ModelClassLogic.FindComplexMember) has no lambda form.
            if (name.Contains('<')) { skipped.Add($"skipped: column \"{column.Id}\" uses the cast path \"{name}\", which the builder cannot express"); return null; }
            return name;
        }
    }

    static bool Explicit(IModelNode node, string valueName) => ((ModelNode)node).HasValue(valueName);
}
