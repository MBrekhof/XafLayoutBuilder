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

    public override void UpdateNode(ModelNode node) {
        if (node.Parent is not IModelDetailView view || view.ModelClass?.TypeInfo?.Type is not { } type) return;
        if (view.Id != type.Name + "_DetailView") return; // ponytail: default DetailView only; variants/nested ids are phase 2
        var spec = LayoutSpecResolver.Detail(type);
        if (spec is null) return;

        var layout = (IModelViewLayout)node;
        var viewItems = view.Items;
        var placed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in layout.ToList()) element.Remove();
        var main = node.AddNode<IModelLayoutGroup>(ModelDetailViewLayoutNodesGenerator.MainLayoutGroupName);
        main.Index = 0;
        main.Direction = XafFlow.Vertical;
        main.ShowCaption = false;
        for (var i = 0; i < spec.Nodes.Count; i++) Add(main, spec.Nodes[i], i, inTab: false);

        // Every visible member must be placed or hidden. The default is to fail (XLB002) so a property added to the
        // class cannot silently vanish from the form; .Unplaced(UnplacedMembers.AppendToGroup(id)) relaxes it.
        var unplaced = viewItems.OfType<IModelPropertyEditor>()
            .Where(pe => pe.ModelMember?.IsVisibleInDetailView != false)
            .Select(pe => ((IModelViewItem)pe).Id)
            .Where(id => !placed.Contains(id) && !spec.HiddenMembers.Contains(id))
            .ToList();
        if (unplaced.Count > 0) {
            if (spec.UnplacedGroupId is not { } catchAll)
                throw new LayoutSpecException(
                    $"XLB002 {view.Id}: members not placed and not hidden: {string.Join(", ", unplaced)}. " +
                    $"Add .Item(x => x.{unplaced[0]}) or .Hide(x => x.{unplaced[0]}) to {type.Name}'s layout, " +
                    $"or relax it for this class with .Unplaced(UnplacedMembers.AppendToGroup(\"Other\")).");
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

        void Add(IModelNode parent, LayoutNodeSpec n, int index, bool inTab) {
            switch (n) {
                case LayoutItemSpec item: {
                    var editor = viewItems[item.Member]
                        ?? throw new LayoutSpecException(
                            $"XLB001 {view.Id}: member '{item.Member}' has no Items entry. Is it [Browsable(false)] or [VisibleInDetailView(false)]?");
                    var li = parent.AddNode<IModelLayoutViewItem>(item.Member);
                    li.ViewItem = editor;
                    li.Index = index;
                    if (inTab) li.ShowCaption = false; // same as the stock generator for a collection on its own tab
                    if (item.RelativeSize is { } size) li.RelativeSize = size;
                    placed.Add(item.Member);
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
