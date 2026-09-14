using DevExpress.ExpressApp.ConditionalAppearance;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Appearance;

/// <summary>
/// APPEAR-001: a class's appearance rules as the builder's spec, for the Module's exports (<c>LayoutCodePrinter.AppearanceExport</c>).
/// Rules the generator made from [Appearance] attributes stay attributes and are only named, so adopting the export cannot clash
/// with them (XLB006); so are rules the builder cannot express: an Action target, a method instead of criteria, the "*" target.
/// </summary>
internal static class AppearanceExporter {
    public static (AppearanceSpec? Spec, IReadOnlyList<string> Notes) Export(IModelClass modelClass) {
        var notes = new List<string>();
        var rules = new List<AppearanceRuleSpec>();
        // The export hook is process-wide (LayoutCodePrinter.AppearanceExport), so the model of an application without the Conditional
        // Appearance module can reach it; its classes have no rules node (Codex review 3).
        if (modelClass is not IModelConditionalAppearance appearance) return (null, notes);
        // In the spec's order (AppearanceRulesUpdater sets Index); rules without one keep the model's order after them.
        foreach (var rule in appearance.AppearanceRules.OrderBy(r => r.Index ?? int.MaxValue)) {
            var id = ((ModelNode)rule).Id;
            // TargetItems is split the way the module splits it (AppearanceRule.cs 57, 88-96).
            var targets = (rule.TargetItems ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var leftOut = rule.Attribute is not null ? "comes from an [Appearance] attribute and stays there"
                // Build() rejects a rule without targets; the Model Editor lets a user clear them (Codex review 3).
                : targets.Length == 0 ? "has no targets"
                // XAF skips a rule that sets nothing (AppearanceController.cs 172-186), and Build() rejects one.
                : rule is { FontColor: null, BackColor: null, FontStyle: null, Enabled: null, Visibility: null } ? "sets no appearance, so XAF ignores it"
                : rule.AppearanceItemType == nameof(AppearanceItemType.Action) ? "targets Actions, which the builder does not"
                : !string.IsNullOrEmpty(rule.Method) ? $"uses the method '{rule.Method}' instead of criteria, which the builder does not"
                : targets.Contains("*") ? "targets \"*\" (all except), which the builder does not"
                : rule.AppearanceItemType != nameof(AppearanceItemType.LayoutItem) && NotMembers(modelClass.TypeInfo.Type, targets)
                    ? $"targets view items that are not members of {modelClass.TypeInfo.Type.Name} ({string.Join(", ", targets)}), such as static text, which a member lambda cannot name"
                : null;
            if (leftOut is not null) {
                notes.Add($"note: appearance rule '{id}' was not exported; it {leftOut}.");
                continue;
            }
            rules.Add(new AppearanceRuleSpec(
                id,
                rule.AppearanceItemType == nameof(AppearanceItemType.LayoutItem) ? AppearanceTargetKind.Layout : AppearanceTargetKind.Items,
                targets,
                string.IsNullOrEmpty(rule.Criteria) ? null : rule.Criteria,
                // "Any" is XAF's default context, what a rule without InListView/InDetailView/InView gets.
                string.IsNullOrEmpty(rule.Context) || rule.Context == "Any" ? null : rule.Context,
                rule.FontColor is { } fontColor ? AppearanceColors.Format(fontColor) : null,
                rule.BackColor is { } backColor ? AppearanceColors.Format(backColor) : null,
                rule.FontStyle is { } style ? Enum.Parse<AppearanceFontStyle>(style.ToString()) : null,
                rule.Enabled,
                rule.Visibility is { } visibility ? Enum.Parse<AppearanceVisibility>(visibility.ToString()) : null,
                rule.Priority == 0 ? null : rule.Priority));
        }
        return (rules.Count == 0 ? null : new AppearanceSpec(modelClass.TypeInfo.Type.FullName!, rules), notes);
    }

    // A ViewItem target need not be a member (a static text item); only members print as .On(x => x.M) and import again.
    static bool NotMembers(Type type, IEnumerable<string> targets) {
        try {
            LayoutSpecChecks.EnsureMembersExist(type, targets);
            return false;
        }
        catch (LayoutSpecException) {
            return true;
        }
    }
}
