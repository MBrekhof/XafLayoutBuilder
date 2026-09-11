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

        // XLB002: every visible member must be placed or hidden, so a new property cannot silently vanish.
        var unplaced = viewItems.OfType<IModelPropertyEditor>()
            .Where(pe => pe.ModelMember?.IsVisibleInDetailView != false)
            .Select(pe => ((IModelViewItem)pe).Id)
            .Where(id => !placed.Contains(id) && !spec.HiddenMembers.Contains(id))
            .ToList();
        if (unplaced.Count > 0)
            throw new LayoutSpecException(
                $"XLB002 {view.Id}: members not placed and not hidden: {string.Join(", ", unplaced)}. " +
                $"Add .Item(x => x.{unplaced[0]}) or .Hide(x => x.{unplaced[0]}) to {type.Name}'s layout.");

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
