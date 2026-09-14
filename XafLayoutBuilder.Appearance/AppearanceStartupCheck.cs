using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ConditionalAppearance;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Appearance;

/// <summary>
/// APPEAR-001: XAF generates a class's appearance rules lazily and ignores a target that does not exist, so this checks every
/// class with rules once the application model exists (XafApplication.SetupComplete), like LayoutStartupCheck does for layouts.
/// </summary>
public static class AppearanceStartupCheck {
    // Once per application type and registry version, as LayoutStartupCheck: XAF Blazor builds an application per circuit.
    static readonly object Gate = new();
    static readonly HashSet<(Type Application, int Rules, int Layouts)> Completed = [];

    // Keyed on LayoutRegistry too: XLB007 reads the builder layouts registered there (Codex re-review).
    internal static (Type Application, int Rules, int Layouts) Key(Type applicationType) =>
        (applicationType, AppearanceRegistry.Version, LayoutRegistry.Version);

    public static void Run(XafApplication application) {
        var key = Key(application.GetType());
        lock (Gate) {
            if (Completed.Contains(key)) return;
            try {
                Check(application.Model);
            }
            catch (Exception ex) when (!XafLayoutBuilderModule.FailFastOnLayoutErrors) {
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
            }
            Completed.Add(key);
        }
    }

    // Every class is attempted and the failures are reported together. XLB007 comes first: it needs no model node, so a class
    // whose rules the updater rejected still gets it. A class whose updater did not mark its rules applied is checked again
    // (XLB006, XLB008): XAF marks the node generated even when the updater threw.
    internal static void Check(IModelApplication model) {
        var failures = new List<string>();
        foreach (var modelClass in model.BOModel) {
            if (modelClass.TypeInfo?.Type is not { } type) continue;
            try {
                if (AppearanceSpecResolver.For(type) is not { } spec) continue;
                var layoutRules = spec.Rules.Where(r => r.TargetKind == AppearanceTargetKind.Layout).ToList();
                if (layoutRules.Count > 0 && BuilderLayoutIds(type) is { } ids)
                    foreach (var rule in layoutRules)
                        if (rule.Targets.FirstOrDefault(t => !ids.Contains(t)) is { } missing)
                            failures.Add($"XLB007 {type.Name}: rule '{rule.Id}' targets layout node '{missing}', which the builder layouts of {type.Name} do not have.");
                var rules = ((IModelConditionalAppearance)modelClass).AppearanceRules;
                _ = rules.NodeCount; // generates the rules, which runs the updater
                if (!AppearanceRulesUpdater.WasApplied(rules)) AppearanceRulesUpdater.CheckAgainstClass(rules, spec, type);
            }
            catch (Exception ex) when (ex is LayoutSpecException || !XafLayoutBuilderModule.FailFastOnLayoutErrors) {
                failures.Add(ex is LayoutSpecException ? ex.Message : ex.ToString());
            }
        }
        if (failures.Count == 1) throw new LayoutSpecException(failures[0]);
        if (failures.Count > 1)
            throw new LayoutSpecException($"{failures.Count} appearance problems:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    // The ids a DetailView layout of the class gets from the builder: the root Main group, every group, tab and item of the type's
    // layout and of its declared DetailViews, and with a catch-all group that group and every member it may collect. Judged against
    // the specs, not the merged model, where a later layer may have removed a node on purpose (Codex review). Null, so no XLB007,
    // for a class without a builder layout, whose ids XAF generates, and for one whose layout spec cannot be resolved: that is
    // LayoutStartupCheck's to report (gate, --break-layout).
    static HashSet<string>? BuilderLayoutIds(Type type) {
        try {
            var specs = LayoutRegistry.Views.Values
                .Where(v => v.Type == type && v.Detail is not null && v.Conflict is null)
                .Select(v => v.Detail!())
                .Prepend(LayoutSpecResolver.Detail(type))
                .OfType<DetailLayoutSpec>()
                .ToList();
            if (specs.Count == 0) return null;
            var ids = new HashSet<string>(StringComparer.Ordinal) { ModelDetailViewLayoutNodesGenerator.MainLayoutGroupName };
            foreach (var spec in specs) {
                ids.UnionWith(spec.Nodes.SelectMany(Ids));
                if (spec.UnplacedGroupId is not { } catchAll) continue;
                ids.Add(catchAll);
                ids.UnionWith(type.GetProperties().Select(p => p.Name));
            }
            return ids;
        }
        catch (Exception) {
            return null;
        }
    }

    static IEnumerable<string> Ids(LayoutNodeSpec node) => node switch {
        LayoutItemSpec item => new[] { item.Member },
        LayoutGroupSpec group => group.Children.SelectMany(Ids).Prepend(group.Id),
        TabbedGroupSpec tabs => tabs.Tabs.SelectMany(Ids).Prepend(tabs.Id),
        _ => Array.Empty<string>(),
    };
}
