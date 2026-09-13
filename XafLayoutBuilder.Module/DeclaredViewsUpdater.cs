using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Adds the views declared with <see cref="LayoutRegistry.AddDetailView{T}"/> and <see cref="LayoutRegistry.AddListView{T}"/>
/// to the generated layer (VIEW-001), the way DevExpress's Dashboards and ReportsV2 modules add their own views. XAF then
/// generates their items, layout and columns, and the layout and columns updaters apply each view's own spec by its id.
/// A view that exists only in XAFML never runs those generators (docs/api-notes.md), which is why it is declared here.
/// </summary>
public sealed class DeclaredViewsUpdater : ModelNodesGeneratorUpdater<ModelViewsNodesGenerator> {
    /// <summary>Model value marking a view this updater added; only such a view takes a declared spec.</summary>
    internal const string Marker = "XafLayoutBuilder.DeclaredView";

    public override void UpdateNode(ModelNode node) {
        if (node is not IModelViews views) return;
        foreach (var (id, view) in LayoutRegistry.Views) {
            // Degrades like the other updaters: logged, and the remaining declared views are still added.
            try {
                AddView(views, id, view);
            }
            catch (Exception ex) when (!XafLayoutBuilderModule.FailFastOnLayoutErrors) {
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
            }
        }
    }

    internal static void AddView(IModelViews views, string id, DeclaredView view) {
        if (string.IsNullOrWhiteSpace(id))
            throw new LayoutSpecException($"XLB005 a view declared for {view.Type.Name} has no id.");
        if (view.Conflict is not null) throw ConflictError(id, view);
        if (views[id] is not null) throw TakenIdError(id);
        var modelClass = views.Application.BOModel.GetClass(view.Type)
            ?? throw new LayoutSpecException($"XLB004 {id} is declared for {view.Type.Name}, which is not in the application model.");
        IModelView added;
        if (view.Detail is not null) {
            var detailView = views.AddNode<IModelDetailView>(id);
            detailView.ModelClass = modelClass;
            added = detailView;
        }
        else {
            var listView = views.AddNode<IModelListView>(id);
            listView.ModelClass = modelClass;
            added = listView;
        }
        ((ModelNode)added).SetValue(Marker, true);
    }

    internal static LayoutSpecException TakenIdError(string id) =>
        new($"XLB005 {id}: another view already has this id; a declared view needs an id of its own.");

    internal static LayoutSpecException ConflictError(string id, DeclaredView view) =>
        new($"XLB005 {id}: declared for {view.Conflict} and again for {view.Describe}; each declared view needs an id of its own.");
}
