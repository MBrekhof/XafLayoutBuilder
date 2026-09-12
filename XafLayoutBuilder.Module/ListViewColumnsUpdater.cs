using DxSort = DevExpress.Data.ColumnSortOrder;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Applies the type's <see cref="ListColumnsSpec"/> to {Type}_ListView and, when the spec has a Lookup, to
/// {Type}_LookupListView. Listed columns get index 0..n-1 plus width, caption and sort; hidden and unmentioned
/// columns get index -1 (not shown, still offered by the column chooser) and no sort. Missing columns are added.
/// Verified API: docs/api-notes.md.
/// </summary>
public sealed class ListViewColumnsUpdater : ModelNodesGeneratorUpdater<ModelListViewColumnsNodesGenerator> {
    // ModelListViewColumnsNodesGeneratorBase.GeneratedIndexValueName (internal). The stock generator stores each
    // generated column's order here and clears Index; ModelColumnDomainLogic.Get_Index reads it while Index is null
    // and returns -1 instead when IModelListView.FreezeColumnIndices is set. Going through the same value keeps
    // that freeze, and every explicit Index in a diff layer, working exactly as for stock columns.
    // This literal is the one thing here that a DevExpress release can rename. If it ever happens, hidden columns
    // reappear and the order goes natural, which fails the E2E gate's "columns are Number, Customer, Order Date in
    // that order" assertion in E2E 2 (XafLayoutBuilder.E2ETests/Program.cs). Start there.
    const string GeneratedIndex = "GeneratedIndex";

    public override void UpdateNode(ModelNode node) {
        // Degrades like DetailViewLayoutUpdater: logged, and the view keeps XAF's generated columns.
        try {
            Apply(node);
        }
        catch (Exception ex) when (!XafLayoutBuilderModule.FailFastOnLayoutErrors) {
            DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
        }
    }

    static void Apply(ModelNode node) {
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
        // Check first, change second (see DetailViewLayoutUpdater): every member that will need a new column has to be
        // able to have one before any existing column is reindexed.
        foreach (var member in spec.Columns.Select(c => c.Member).Concat(spec.HiddenMembers))
            if (columns[member] is null) RequireColumnMember(member);

        var listed = new HashSet<string>(StringComparer.Ordinal);
        var sortIndex = 0;
        for (var i = 0; i < spec.Columns.Count; i++) {
            var c = spec.Columns[i];
            var column = columns[c.Member] ?? AddColumn(c.Member);
            listed.Add(c.Member);
            SetGeneratedIndex(column, i);
            if (c.Width is { } w) column.Width = w;
            if (c.Caption is not null) column.Caption = c.Caption;
            column.SortOrder = c.SortOrder switch {
                Core.ColumnSortOrder.Ascending => DxSort.Ascending,
                Core.ColumnSortOrder.Descending => DxSort.Descending,
                _ => DxSort.None,
            };
            column.SortIndex = c.SortOrder == Core.ColumnSortOrder.None ? -1 : sortIndex++;
        }
        // Hidden members must exist as columns so the chooser can offer them, even where the generator made none.
        foreach (var hidden in spec.HiddenMembers)
            if (columns[hidden] is null) AddColumn(hidden);
        foreach (var column in columns) {
            if (listed.Contains(column.Id)) continue;
            // Hidden and unmentioned alike: available in the column chooser, not shown, and never sorted by default
            // (the stock generator sorts the display member ascending, which would fight the spec's sort).
            SetGeneratedIndex(column, -1);
            column.SortOrder = DxSort.None;
            column.SortIndex = -1;
        }

        static void SetGeneratedIndex(IModelColumn column, int index) {
            var n = (ModelNode)column;
            n.SetValue<int?>(GeneratedIndex, index);
            n.ClearValue(ModelValueNames.Index);
        }

        // The lookup generator only creates columns for the display property and [VisibleInLookupListView(true)]
        // members (falling back to the full set when that yields nothing), and neither view gets a column for a member
        // hidden by attribute. A listed or hidden member gets its column the way the generator makes one:
        // AddNode<IModelColumn>(name) + PropertyName (ModelListViewNodesGenerator.cs 437-442; View_ID only matters
        // for list-property editors, which cannot be columns).
        void RequireColumnMember(string member) {
            var modelMember = view.ModelClass.FindMember(member)
                ?? throw new LayoutSpecException($"XLB003 {view.Id}: '{member}' is not a member of {type.Name}.");
            if (modelMember.MemberInfo.MemberTypeInfo.IsListType)
                throw new LayoutSpecException($"XLB003 {view.Id}: '{member}' is a collection and cannot be a column.");
        }

        IModelColumn AddColumn(string member) {
            var column = columns.AddNode<IModelColumn>(member);
            column.PropertyName = member;
            return column;
        }
    }
}
