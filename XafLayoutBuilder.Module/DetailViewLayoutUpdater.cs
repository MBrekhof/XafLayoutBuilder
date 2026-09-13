using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;
using XafFlow = DevExpress.ExpressApp.Layout.FlowDirection;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Replaces the generated layout of {Type}_DetailView with the type's <see cref="DetailLayoutSpec"/>.
/// Runs at the generated (zero) layer: module XAFML, admin and user differences still apply on top.
/// Verified API: docs/api-notes.md.
/// </summary>
public sealed class DetailViewLayoutUpdater : ModelNodesGeneratorUpdater<ModelDetailViewLayoutNodesGenerator> {
    /// <summary>Model value marking the group this updater created for <see cref="UnplacedMembers.AppendToGroup"/>.</summary>
    internal const string CatchAllMarker = "XafLayoutBuilder.UnplacedGroup";

    /// <summary>
    /// RECHECK-001: model value marking a layout this updater applied. The startup check re-checks only a view without it,
    /// one whose updater failed, perhaps in an earlier check, and that XAF will not generate again.
    /// </summary>
    internal const string AppliedMarker = "XafLayoutBuilder.LayoutApplied";

    internal static bool WasApplied(IModelDetailView view) => ((ModelNode)view.Layout).GetValue<bool>(AppliedMarker);

    public override void UpdateNode(ModelNode node) {
        // With XafLayoutBuilderModule.FailFastOnLayoutErrors off (the default) a spec that cannot be applied is logged and
        // the view keeps XAF's generated layout, which is intact because Apply checks everything before it changes anything.
        // ponytail: an exception from the rebuild itself, after the checks passed, would still leave a partial layout;
        // the checks cover every failure known to occur.
        try {
            Apply(node);
        }
        catch (Exception ex) when (!XafLayoutBuilderModule.FailFastOnLayoutErrors) {
            DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
        }
    }

    /// <summary>
    /// XLB001 and XLB002, the checks that need the view's items, shared by <see cref="Apply"/> and the startup check
    /// (RECHECK-001). Returns the visible editors the layout neither places nor hides, which a catch-all group collects.
    /// </summary>
    internal static IReadOnlyList<string> CheckAgainstView(IModelDetailView view, DetailLayoutSpec spec) =>
        LayoutSpecChecks.CheckAgainstView(spec, view.Id,
            view.Items.Select(item => item.Id),
            view.Items.OfType<IModelPropertyEditor>()
                .Where(pe => pe.ModelMember?.IsVisibleInDetailView != false)
                .Select(pe => ((IModelViewItem)pe).Id));

    static void Apply(ModelNode node) {
        if (node.Parent is not IModelDetailView view || view.ModelClass?.TypeInfo?.Type is not { } type) return;
        // {Type}_DetailView takes the type's layout, a view declared in code its own (VIEW-001); anything else stays XAF's.
        var spec = LayoutSpecResolver.DetailForView(view, type);
        if (spec is null) return;

        var layout = (IModelViewLayout)node;
        var viewItems = view.Items;

        // Check first, change second. XAF marks the layout as generated even when an updater throws
        // (ModelNode._RunNodesGenerator1, docs/api-notes.md), so a failure halfway through the rebuild would leave a
        // half-applied layout behind for good. A rejected spec leaves XAF's own generated layout as it was.
        // Every visible member must be placed or hidden. The default is to fail (XLB002) so a property added to the
        // class cannot silently vanish from the form; .Unplaced(UnplacedMembers.AppendToGroup(id)) relaxes it.
        var unplaced = CheckAgainstView(view, spec);

        foreach (var element in layout.ToList()) element.Remove();
        var main = node.AddNode<IModelLayoutGroup>(ModelDetailViewLayoutNodesGenerator.MainLayoutGroupName);
        main.Index = 0;
        main.Direction = XafFlow.Vertical;
        main.ShowCaption = false;
        for (var i = 0; i < spec.Nodes.Count; i++) Add(main, spec.Nodes[i], i, inTab: false);

        if (unplaced.Count > 0 && spec.UnplacedGroupId is { } catchAll) {
            var catchAllGroup = main.AddNode<IModelLayoutGroup>(catchAll);
            catchAllGroup.Index = spec.Nodes.Count;
            catchAllGroup.Direction = XafFlow.Vertical;
            catchAllGroup.ShowCaption = true;
            // Stamp it so the exporter can tell this group apart from one the layout author wrote, and print
            // .Unplaced(...) again instead of freezing today's leftovers into explicit items.
            ((ModelNode)catchAllGroup).SetValue(CatchAllMarker, true);
            // Set the caption rather than leaving XAF to compute it: a group holding one item takes that item's
            // caption, so a catch-all called "Other" with a single leftover member would be headed "City".
            catchAllGroup.Caption = catchAll;
            for (var i = 0; i < unplaced.Count; i++) {
                var item = catchAllGroup.AddNode<IModelLayoutViewItem>(unplaced[i]);
                item.ViewItem = viewItems[unplaced[i]];
                item.Index = i;
            }
        }
        node.SetValue(AppliedMarker, true);

        void Add(IModelNode parent, LayoutNodeSpec n, int index, bool inTab) {
            switch (n) {
                case LayoutItemSpec item: {
                    var li = parent.AddNode<IModelLayoutViewItem>(item.Member);
                    li.ViewItem = viewItems[item.Member]; // present: CheckAgainstView threw XLB001 otherwise
                    li.Index = index;
                    if (inTab) li.ShowCaption = false; // same as the stock generator for a collection on its own tab
                    if (item.RelativeSize is { } size) li.RelativeSize = size;
                    break;
                }
                case LayoutGroupSpec g: {
                    var lg = parent.AddNode<IModelLayoutGroup>(g.Id);
                    lg.Index = index;
                    lg.Direction = g.Direction == FlowDirection.Horizontal ? XafFlow.Horizontal : XafFlow.Vertical;
                    // Blazor renders the collapse toggle in the group header, so a collapsible group must show its caption
                    // (defaults to the id). Verified against the DOM in session 3, see docs/api-notes.md.
                    lg.ShowCaption = inTab || g.Caption is not null || g.Collapsible;
                    if (g.Caption is not null) lg.Caption = g.Caption;
                    if (g.Collapsible) lg.IsCollapsibleGroup = true;
                    if (g.RelativeSize is { } size) lg.RelativeSize = size;
                    if (g.ImageName is not null) lg.ImageName = g.ImageName;
                    for (var i = 0; i < g.Children.Count; i++) Add(lg, g.Children[i], i, inTab: inTab && g.Children.Count == 1);
                    break;
                }
                case TabbedGroupSpec t: {
                    var tg = parent.AddNode<IModelTabbedGroup>(t.Id);
                    tg.Index = index;
                    for (var i = 0; i < t.Tabs.Count; i++) Add(tg, t.Tabs[i], i, inTab: true);
                    break;
                }
            }
        }
    }
}
