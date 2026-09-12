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
    // XAF Blazor builds one XafApplication per circuit, so without this the whole forced generation would run again
    // for every user session. The memory is keyed by application type *and* registry version, so a spec registered
    // after one application has started is still validated for the next one. With FailFastOnLayoutErrors on, only a
    // completed run is recorded: an application that fails the check keeps failing loudly instead of passing quietly
    // the second time. With it off, a degraded run is recorded too; ASP.NET Core shares one Application Model per
    // process, so a second circuit would have nothing new to log.
    static readonly object Gate = new();
    static readonly HashSet<(Type Application, int RegistryVersion)> Completed = [];

    public static void Run(XafApplication application) {
        var key = (application.GetType(), LayoutRegistry.Version);
        // The lock is held across the run so two circuits starting together cannot both do the work.
        lock (Gate) {
            if (Completed.Contains(key)) return;
            try {
                Check(application);
            }
            catch (Exception ex) when (!XafLayoutBuilderModule.FailFastOnLayoutErrors) {
                // The updaters already logged and degraded what they could not apply; this catches the rest (XLB004,
                // a spec factory that throws) so it cannot stop the host either.
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
            }
            Completed.Add(key);
        }
    }

    /// <summary>Test hook: forget what has already been validated.</summary>
    public static void Reset() {
        lock (Gate) Completed.Clear();
    }

    // Every view is attempted and the failures are reported together, so one broken layout cannot hide the next one
    // behind another application start. One attempt per view, not per class: a broken DetailView must not keep the
    // same class's ListView and lookup from being checked.
    static void Check(XafApplication application) {
        var views = application.Model.Views;
        var failures = new List<string>();
        foreach (var modelClass in application.Model.BOModel) {
            if (modelClass.TypeInfo?.Type is not { } type) continue;
            DetailLayoutSpec? detail = null;
            ListColumnsSpec? columns = null;
            if (!Attempt(() => { detail = LayoutSpecResolver.Detail(type); columns = LayoutSpecResolver.Columns(type); })) continue;
            if (detail is not null)
                Attempt(() => Touch(Required<IModelDetailView>(views, type.Name + "_DetailView", type, "a DetailView layout spec").Layout));
            if (columns is not null) {
                Attempt(() => Touch(Required<IModelListView>(views, type.Name + "_ListView", type, "a ListView columns spec").Columns));
                if (columns.Lookup is not null)
                    Attempt(() => Touch(Required<IModelListView>(views, type.Name + "_LookupListView", type, "a lookup columns spec").Columns));
            }
        }
        if (failures.Count == 1) throw new LayoutSpecException(failures[0]);
        if (failures.Count > 1)
            throw new LayoutSpecException($"{failures.Count} layout problems:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");

        // Layout errors are always collected. With fail-fast off anything else is collected too (a spec factory that throws,
        // say), so one broken factory cannot end the check early and leave the remaining types undiagnosed while the run is
        // recorded as done. With it on, anything else propagates at once, as before.
        bool Attempt(Action check) {
            try {
                check();
                return true;
            }
            catch (Exception ex) when (ex is LayoutSpecException || !XafLayoutBuilderModule.FailFastOnLayoutErrors) {
                failures.Add(ex is LayoutSpecException ? ex.Message : ex.ToString());
                return false;
            }
        }
    }

    static TView Required<TView>(IModelViews views, string id, Type type, string what) where TView : class, IModelView =>
        views[id] as TView ?? throw new LayoutSpecException($"XLB004 {type.Name} has {what} but the application model has no view '{id}'.");

    // Enumerating a node's children is what makes ModelNode generate them (EnsureNodes).
    static void Touch(IModelNode node) => _ = node.NodeCount;
}
