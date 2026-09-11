using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.Core;
using DxSort = DevExpress.Data.ColumnSortOrder;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Walks a merged model view (all layers applied) back into a spec. Only values a layer actually set are exported
/// (ModelNode.HasValue), so XAF's computed defaults (a group caption derived from its single item, generated column
/// widths) do not show up as builder calls. Items whose ViewItem is not a property editor are skipped and reported.
/// </summary>
public static class LayoutExporter {
    public static (DetailLayoutSpec Spec, IReadOnlyList<string> Skipped) ExportDetail(IModelDetailView view) {
        var skipped = new List<string>();
        var type = view.ModelClass.TypeInfo.Type;
        // The stock root is a single "Main" group; the builder's nodes are its children.
        IEnumerable<IModelViewLayoutElement> roots = view.Layout;
        if (view.Layout.Count == 1 && view.Layout[0] is IModelLayoutGroup main) roots = main;
        var nodes = roots.OrderBy(e => e.Index ?? int.MaxValue).Select(Convert).OfType<LayoutNodeSpec>().ToList();
        var placed = new HashSet<string>(new DetailLayoutSpec(type.FullName!, nodes, []).Members(), StringComparer.Ordinal);
        var hidden = view.Items.OfType<IModelPropertyEditor>()
            .Where(pe => pe.ModelMember?.IsVisibleInDetailView != false)
            .Select(pe => ((IModelViewItem)pe).Id)
            .Where(id => !placed.Contains(id))
            .ToList();
        return (new DetailLayoutSpec(type.FullName!, nodes, hidden), skipped);

        LayoutNodeSpec? Convert(IModelViewLayoutElement element) {
            switch (element) {
                case IModelTabbedGroup tabs:
                    return new TabbedGroupSpec(tabs.Id, tabs.OrderBy(t => t.Index ?? int.MaxValue).Select(ConvertGroup).ToList());
                case IModelLayoutGroup group:
                    return ConvertGroup(group);
                case IModelLayoutViewItem item:
                    if (item.ViewItem is IModelPropertyEditor pe)
                        return new LayoutItemSpec(((IModelViewItem)pe).Id, Explicit(item, "RelativeSize") ? item.RelativeSize : null);
                    skipped.Add($"skipped: {item.ViewItem?.GetType().Name ?? "layout item"} \"{item.Id}\" is not a property editor");
                    return null;
                default:
                    skipped.Add($"skipped: {element.GetType().Name} \"{element.Id}\"");
                    return null;
            }
        }

        LayoutGroupSpec ConvertGroup(IModelLayoutGroup group) => new(
            group.Id,
            group.OrderBy(e => e.Index ?? int.MaxValue).Select(Convert).OfType<LayoutNodeSpec>().ToList(),
            // Caption is localizable, so HasValue looks in the language aspect and misses the generated value.
            // Compare against XAF's default instead (ModelLayoutGroupLogic.Get_Caption: the single item's caption, else the id).
            Caption: group.ShowCaption == true && group.Caption != DefaultCaption(group) ? group.Caption : null,
            Direction: group.Direction == DevExpress.ExpressApp.Layout.FlowDirection.Horizontal ? FlowDirection.Horizontal : FlowDirection.Vertical,
            Collapsible: group.IsCollapsibleGroup,
            RelativeSize: Explicit(group, "RelativeSize") ? group.RelativeSize : null,
            ImageName: string.IsNullOrEmpty(group.ImageName) ? null : group.ImageName);
    }

    static string DefaultCaption(IModelLayoutGroup group) =>
        group.Count == 1 && group[0] is IModelLayoutViewItem { ViewItem: { } item } ? item.Caption : group.Id;

    /// <summary>
    /// Every column with an index is listed; every other column is exported as hidden, except the key, which XAF never
    /// shows by default. Hidden and merely unmentioned columns are indistinguishable in the model, so the export is
    /// explicit where the original builder may have been silent; the rendered result is identical.
    /// </summary>
    public static ListColumnsSpec ExportColumns(IModelListView view, IModelListView? lookupView) {
        var type = view.ModelClass.TypeInfo.Type;
        var key = view.ModelClass.KeyProperty;
        return new ListColumnsSpec(type.FullName!, Columns(view), Hidden(view, key), lookupView is null ? null : new ListColumnsSpec(type.FullName!, Columns(lookupView), Hidden(lookupView, key)));

        static List<ColumnSpec> Columns(IModelListView v) => v.Columns
            .Where(c => c.Index is >= 0)
            .OrderBy(c => c.Index)
            .Select(c => new ColumnSpec(
                c.Id,
                Explicit(c, "Width") ? c.Width : null,
                c.SortOrder switch { DxSort.Ascending => ColumnSortOrder.Ascending, DxSort.Descending => ColumnSortOrder.Descending, _ => ColumnSortOrder.None },
                // Localizable, so not HasValue (see ConvertGroup): compare with the member caption XAF falls back to.
                c.Caption != c.ModelMember?.Caption ? c.Caption : null))
            .ToList();

        static List<string> Hidden(IModelListView v, string? key) => v.Columns.Where(c => c.Index is null or < 0 && c.Id != key).Select(c => c.Id).ToList();
    }

    static bool Explicit(IModelNode node, string valueName) => ((ModelNode)node).HasValue(valueName);
}
