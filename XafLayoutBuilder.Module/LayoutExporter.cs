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
    /// Every column with an index is listed. An unshown column is exported as hidden only when the spec hid it or a later
    /// layer hid it (EXPORT-001); one the spec never mentioned stays out, as in hand-written code, and so does the key.
    /// A hidden column's own sort order is not exported: the applier clears it, so it cannot round-trip.
    /// </summary>
    public static (ListColumnsSpec Spec, IReadOnlyList<string> Skipped) ExportColumns(IModelListView view, IModelListView? lookupView) {
        var skipped = new List<string>();
        var type = view.ModelClass.TypeInfo.Type;
        var key = view.ModelClass.KeyProperty;
        var listed = Columns(view, withBands: true);
        if (lookupView is { BandsLayout: { Enable: true, Count: > 0 } })
            skipped.Add("note: the lookup's bands were not exported; Lookup() takes no bands");
        if (view.BandsLayout is { Enable: true } bandsLayout && bandsLayout.Any(b => b.OwnerBand is not null))
            skipped.Add("note: nested bands were flattened into their outermost band; the builder has one band level");
        var spec = new ListColumnsSpec(type.FullName!, listed, Hidden(view),
            lookupView is null ? null : new ListColumnsSpec(type.FullName!, Columns(lookupView, withBands: false), Hidden(lookupView)),
            Bands(view, listed));
        return (spec, skipped);

        // BAND-001: the order the grid shows. Without bands that is the column Index. With bands, XAF Blazor numbers the root
        // bands and unbanded columns, and each band's own columns, separately (SynchronizeVisibleIndexesToBands,
        // DxGridColumnsListEditorModelSynchronizer.cs 79-97), so every level is ordered on its own and a band's columns take
        // its place. Each level sorts with XAF's own comparer: index, then id, then bands before columns (Codex re-review).
        IEnumerable<IModelColumn> DisplayOrder(IModelListView v) {
            var visible = v.Columns.Where(c => c.Index is >= 0).ToList();
            if (!v.BandsLayout.Enable) return visible.OrderBy(c => c.Index);
            return Level(null);

            IEnumerable<IModelColumn> Level(string? bandId) {
                var items = visible
                    .Where(c => ((IModelBandedColumn)c).OwnerBand?.Id == bandId)
                    .Cast<IModelBandedLayoutItem>()
                    .Concat(v.BandsLayout.Where(b => b.Index is not < 0 && b.OwnerBand?.Id == bandId))
                    .ToList();
                items.Sort(new ModelBandedLayoutItemComparer(true));
                return items.SelectMany(i => i is IModelBand band ? Level(band.Id) : new[] { (IModelColumn)i });
            }
        }

        // One band level is all the builder has and all XAF Blazor renders: a column in a nested band exports under the
        // outermost band, which keeps that band's columns adjacent.
        static IModelBand? Outermost(IModelBand? band) {
            while (band?.OwnerBand is { } parent) band = parent;
            return band;
        }

        // BAND-001: the bands over at least one exported column, in column order, with a caption only when it is not the id,
        // which is XAF's default (ModelBandDomainLogic.Get_Caption).
        List<BandSpec>? Bands(IModelListView v, List<ColumnSpec> exported) {
            if (!v.BandsLayout.Enable) return null;
            var used = exported.Select(c => c.Band).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            return used.Count == 0
                ? null
                : used.Select(id => v.BandsLayout[id] is { } band && band.Caption != id ? new BandSpec(id, band.Caption) : new BandSpec(id)).ToList();
        }

        List<ColumnSpec> Columns(IModelListView v, bool withBands) {
            var shown = DisplayOrder(v)
                .Select(c => (Column: c, Member: Simple(c)))
                .Where(x => x.Member is not null)
                .ToList();
            // SORT-001: sort priority ranked the way the Blazor grid sorts, grouped columns first by GroupIndex, then the rest
            // by SortIndex. A grouped column keeps its SortOrder but has SortIndex -1 (docs/api-notes.md), so the stored
            // index cannot be exported as it is. Grouping itself has no builder form and is not exported.
            var sorted = shown.Where(x => x.Column.SortOrder != DxSort.None).Select(x => x.Column).ToList();
            var priority = sorted
                .OrderByDescending(c => c.GroupIndex >= 0)
                .ThenBy(c => c.GroupIndex >= 0 ? c.GroupIndex : c.SortIndex)
                .Select((c, rank) => (c, rank))
                .ToDictionary(p => p.c, p => p.rank);
            // Printed only when it differs from column order, so a spec that sorts in column order round-trips to the same text.
            var explicitSortPriority = sorted.Where((c, position) => priority[c] != position).Any();
            return shown.Select(x => new ColumnSpec(
                    x.Member!,
                    Explicit(x.Column, "Width") ? x.Column.Width : null,
                    x.Column.SortOrder switch { DxSort.Ascending => ColumnSortOrder.Ascending, DxSort.Descending => ColumnSortOrder.Descending, _ => ColumnSortOrder.None },
                    // Localizable like a group caption: compare with the member caption XAF falls back to.
                    x.Column.Caption != x.Column.ModelMember?.Caption ? x.Column.Caption : null,
                    explicitSortPriority && priority.TryGetValue(x.Column, out var rank) ? rank : null,
                    withBands && v.BandsLayout.Enable ? Outermost(((IModelBandedColumn)x.Column).OwnerBand)?.Id : null))
                .ToList();
        }

        // Left out is only a generated leftover: a column the generated layer made unshown (GeneratedIndex -1; the stock
        // generator and the updater give every column they make one) that the spec did not hide, or ListViewColumnsUpdater
        // would have stamped it. A column the generated layer showed was hidden by a later layer; a column with no generated
        // index was added by one, and leaving it out would lose it from the chooser when the export is applied. Asking
        // whether some layer stored an Index would also catch a layer that stores one for every column (FreezeColumnIndices).
        List<string> Hidden(IModelListView v) => v.Columns
            .Where(c => c.Index is null or < 0)
            .Where(c => ((ModelNode)c).GetValue<bool>(ListViewColumnsUpdater.HiddenMarker)
                        || !(((ModelNode)c).GetValue<int?>(ListViewColumnsUpdater.GeneratedIndex) < 0))
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
