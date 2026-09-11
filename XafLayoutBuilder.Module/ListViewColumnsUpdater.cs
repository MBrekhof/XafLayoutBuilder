using DxSort = DevExpress.Data.ColumnSortOrder;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Applies the type's <see cref="ListColumnsSpec"/> to {Type}_ListView and, when the spec has a Lookup, to
/// {Type}_LookupListView. Columns the stock generator created are kept: listed ones get Index 0..n-1 plus width and
/// sort, every other column gets Index -1 (not shown, still offered by the column chooser) and no sort.
/// Verified API: docs/api-notes.md.
/// </summary>
public sealed class ListViewColumnsUpdater : ModelNodesGeneratorUpdater<ModelListViewColumnsNodesGenerator> {
    public override void UpdateNode(ModelNode node) {
        if (node.Parent is not IModelListView view || view.ModelClass?.TypeInfo?.Type is not { } type) return;
        var spec = LayoutSpecResolver.Columns(type);
        if (spec is null) return;

        var isLookup = view.GetValue<bool>(ModelViewsNodesGenerator.IsLookupListView);
        if (isLookup) {
            if (view.Id != type.Name + "_LookupListView" || spec.Lookup is null) return; // no Lookup(): XAF's default stays
            spec = spec.Lookup;
        }
        else if (view.Id != type.Name + "_ListView") return; // ponytail: default ListView only; nested/variants are phase 2

        var columns = (IModelColumns)node;
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var sortIndex = 0;
        for (var i = 0; i < spec.Columns.Count; i++) {
            var c = spec.Columns[i];
            var column = columns[c.Member] ?? AddColumn(c.Member);
            listed.Add(c.Member);
            column.Index = i;
            if (c.Width is { } w) column.Width = w;
            if (c.Caption is not null) column.Caption = c.Caption;
            column.SortOrder = c.SortOrder switch {
                Core.ColumnSortOrder.Ascending => DxSort.Ascending,
                Core.ColumnSortOrder.Descending => DxSort.Descending,
                _ => DxSort.None,
            };
            column.SortIndex = c.SortOrder == Core.ColumnSortOrder.None ? -1 : sortIndex++;
        }
        foreach (var column in columns) {
            if (listed.Contains(column.Id)) continue;
            // Hidden and unmentioned alike: available in the column chooser, not shown, and never sorted by default
            // (the stock generator sorts the display member ascending, which would fight the spec's sort).
            column.Index = -1;
            column.SortOrder = DxSort.None;
            column.SortIndex = -1;
        }

        // The stock generator only creates lookup columns for the display property and [VisibleInListView(true)]
        // members, and no column for [VisibleInListView(false)] ones. A listed member gets its column the same way
        // the generator makes one: AddNode<IModelColumn>(name) + PropertyName (ModelListViewNodesGenerator.cs 437-442).
        IModelColumn AddColumn(string member) {
            var modelMember = view.ModelClass.FindMember(member)
                ?? throw new LayoutSpecException($"XLB003 {view.Id}: '{member}' is not a member of {type.Name}.");
            if (modelMember.MemberInfo.MemberTypeInfo.IsListType)
                throw new LayoutSpecException($"XLB003 {view.Id}: '{member}' is a collection and cannot be a column.");
            var column = columns.AddNode<IModelColumn>(member);
            column.PropertyName = member;
            return column;
        }
    }
}
