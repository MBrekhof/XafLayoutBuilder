using DxSort = DevExpress.Data.ColumnSortOrder;
using DevExpress.ExpressApp.DC;
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
    internal const string GeneratedIndex = "GeneratedIndex";

    /// <summary>Model value marking a column this updater hid because the spec said Hide (EXPORT-001).</summary>
    internal const string HiddenMarker = "XafLayoutBuilder.HiddenColumn";

    /// <summary>RECHECK-001: model value marking a column set this updater applied (see DetailViewLayoutUpdater.AppliedMarker).</summary>
    internal const string AppliedMarker = "XafLayoutBuilder.ColumnsApplied";

    internal static bool WasApplied(IModelListView view) => ((ModelNode)view.Columns).GetValue<bool>(AppliedMarker);

    public override void UpdateNode(ModelNode node) {
        // Degrades like DetailViewLayoutUpdater: logged, and the view keeps XAF's generated columns.
        try {
            Apply(node);
        }
        catch (Exception ex) when (!XafLayoutBuilderModule.FailFastOnLayoutErrors) {
            DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
        }
    }

    /// <summary>
    /// XLB003, the check that needs the view's columns, shared by <see cref="Apply"/> and the startup check (RECHECK-001):
    /// every member without a column must be able to have one, so it has to be a member of the type and not a collection.
    /// </summary>
    internal static void CheckAgainstView(IModelListView view, IModelColumns columns, ListColumnsSpec spec, Type type) {
        foreach (var member in spec.Columns.Select(c => c.Member).Concat(spec.HiddenMembers)) {
            if (columns[member] is not null) continue;
            var modelMember = view.ModelClass.FindMember(member)
                ?? throw new LayoutSpecException($"XLB003 {view.Id}: '{member}' is not a member of {type.Name}.");
            if (modelMember.MemberInfo.MemberTypeInfo.IsListType)
                throw new LayoutSpecException($"XLB003 {view.Id}: '{member}' is a collection and cannot be a column.");
        }
    }

    static void Apply(ModelNode node) {
        if (node.Parent is not IModelListView view || view.ModelClass?.TypeInfo?.Type is not { } type) return;
        // VIEW-001: a ListView declared in code has its own columns; every other view takes the type's.
        IMemberInfo? nestedIn = null;
        var spec = LayoutSpecResolver.DeclaredColumns(view, type);
        if (spec is null) {
            spec = LayoutSpecResolver.Columns(type);
            if (spec is null) return;
            var isLookup = view.GetValue<bool>(ModelViewsNodesGenerator.IsLookupListView);
            // A nested ListView, the grid of a collection of this type inside another class, takes the type's spec too.
            nestedIn = view.GetValue<IMemberInfo>(ModelViewsNodesGenerator.NestedListViewMemberInfo);
            if (isLookup) {
                if (view.Id != type.Name + "_LookupListView" || spec.Lookup is null) return; // no Lookup(): XAF's default stays
                spec = spec.Lookup;
            }
            else if (view.Id != type.Name + "_ListView" && nestedIn is null) return; // ponytail: variants and XAFML views are XAF's
        }

        var columns = (IModelColumns)node;
        // Check first, change second (see DetailViewLayoutUpdater): every member that will need a new column has to be
        // able to have one before any existing column is reindexed.
        CheckAgainstView(view, columns, spec, type);

        // XAF leaves the reference back to the owner out of a nested view (ModelListViewNodesGenerator.IsParentProperty); a
        // spec written for the type's own ListView that lists it must not bring it back there.
        var backReference = nestedIn is { IsAssociation: true } ? nestedIn.AssociatedMemberInfo?.Name : null;
        var shownColumns = spec.Columns.Where(c => c.Member != backReference).ToList();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var sortIndex = 0;
        for (var i = 0; i < shownColumns.Count; i++) {
            var c = shownColumns[i];
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
            // An explicit sort priority (SORT-001) wins; validation guarantees every sorted column has one or none does. A grouped
            // column (GROUP-001) is sorted by its group index before the others and has no sort index, as DxGrid stores it.
            column.SortIndex = c.SortOrder == Core.ColumnSortOrder.None || c.GroupIndex is not null ? -1 : c.SortIndex ?? sortIndex++;
            if (c.GroupIndex is { } groupIndex) column.GroupIndex = groupIndex;
        }
        // GROUP-001: like the columns, a generated-layer default the user's own layout can change.
        if (spec.ShowGroupPanel) view.IsGroupPanelVisible = true;
        // Hidden members must exist as columns so the chooser can offer them, even where the generator made none. Stamped,
        // because the model cannot otherwise tell a column the spec hid from one it never mentioned (EXPORT-001).
        foreach (var hidden in spec.HiddenMembers)
            ((ModelNode)(columns[hidden] ?? AddColumn(hidden))).SetValue(HiddenMarker, true);
        // BAND-001: the spec's bands, each at its first column's position and over its columns. Written from this updater, the
        // band nodes still land in the generated layer: XAF counts generators in progress per layer (ModelNode.cs 430, 460-463).
        if (spec.Bands is { Count: > 0 } bands) {
            view.BandsLayout.Enable = true;
            foreach (var bandSpec in bands) {
                var first = shownColumns.FindIndex(c => c.Band == bandSpec.Id);
                if (first < 0) continue; // a nested view left out the band's only column, the owner back-reference
                var band = view.BandsLayout.AddNode<IModelBand>(bandSpec.Id);
                band.Index = first;
                if (bandSpec.Caption is not null) band.Caption = bandSpec.Caption;
                foreach (var c in shownColumns.Where(c => c.Band == bandSpec.Id))
                    ((IModelBandedColumn)columns[c.Member]!).OwnerBand = band;
            }
        }
        foreach (var column in columns) {
            if (listed.Contains(column.Id)) continue;
            // Hidden and unmentioned alike: available in the column chooser, not shown, and never sorted by default
            // (the stock generator sorts the display member ascending, which would fight the spec's sort).
            SetGeneratedIndex(column, -1);
            column.SortOrder = DxSort.None;
            column.SortIndex = -1;
        }
        node.SetValue(AppliedMarker, true);

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
        IModelColumn AddColumn(string member) {
            var column = columns.AddNode<IModelColumn>(member);
            column.PropertyName = member;
            return column;
        }
    }
}
