using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>
/// XAF generates a view's layout and columns lazily, on first use, so the XLB diagnostics thrown by the updaters
/// would otherwise surface when a user first opens the view. This forces generation for every type that has a spec
/// as soon as the application model exists (XafApplication.SetupComplete), turning them into startup failures.
/// </summary>
public static class LayoutStartupCheck {
    public static void Run(XafApplication application) {
        foreach (var modelClass in application.Model.BOModel) {
            var type = modelClass.TypeInfo?.Type;
            if (type is null) continue;
            if (LayoutSpecResolver.Detail(type) is not null) {
                var detail = modelClass.DefaultDetailView
                    ?? throw new LayoutSpecException($"XLB004 {type.Name} has a DetailView layout spec but no default DetailView.");
                Touch(detail.Layout);
            }
            if (LayoutSpecResolver.Columns(type) is { } columns) {
                var list = modelClass.DefaultListView
                    ?? throw new LayoutSpecException($"XLB004 {type.Name} has a ListView columns spec but no default ListView.");
                Touch(list.Columns);
                if (columns.Lookup is not null && modelClass.DefaultLookupListView is { } lookup) Touch(lookup.Columns);
            }
        }
    }

    // Enumerating a node's children is what makes ModelNode generate them (EnsureNodes).
    static void Touch(IModelNode node) => _ = node.NodeCount;
}
