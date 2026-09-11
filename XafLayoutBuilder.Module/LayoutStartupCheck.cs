using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>
/// XAF generates a view's layout and columns lazily, on first use, so the XLB diagnostics thrown by the updaters
/// would otherwise surface when a user first opens the view. This forces generation for every type that has a spec
/// as soon as the application model exists (XafApplication.SetupComplete), turning them into startup failures.
/// The views are looked up by the same fixed ids the updaters handle, not through IModelClass.DefaultDetailView and
/// friends: those can point elsewhere through a model difference, which would validate a view nobody applies a spec
/// to and leave the real one unchecked.
/// </summary>
public static class LayoutStartupCheck {
    public static void Run(XafApplication application) {
        var views = application.Model.Views;
        foreach (var modelClass in application.Model.BOModel) {
            if (modelClass.TypeInfo?.Type is not { } type) continue;
            if (LayoutSpecResolver.Detail(type) is not null)
                Touch(Required<IModelDetailView>(views, type.Name + "_DetailView", type, "a DetailView layout spec").Layout);
            if (LayoutSpecResolver.Columns(type) is { } columns) {
                Touch(Required<IModelListView>(views, type.Name + "_ListView", type, "a ListView columns spec").Columns);
                if (columns.Lookup is not null)
                    Touch(Required<IModelListView>(views, type.Name + "_LookupListView", type, "a lookup columns spec").Columns);
            }
        }
    }

    static TView Required<TView>(IModelViews views, string id, Type type, string what) where TView : class, IModelView =>
        views[id] as TView ?? throw new LayoutSpecException($"XLB004 {type.Name} has {what} but the application model has no view '{id}'.");

    // Enumerating a node's children is what makes ModelNode generate them (EnsureNodes).
    static void Touch(IModelNode node) => _ = node.NodeCount;
}
