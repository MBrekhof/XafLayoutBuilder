using System.Drawing;
using DevExpress.Data.Filtering;
using DevExpress.Drawing;
using DevExpress.ExpressApp.ConditionalAppearance;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Appearance;

/// <summary>
/// APPEAR-001: adds the type's <see cref="AppearanceSpec"/> rules to BOModel | Class | AppearanceRules, after the generator has
/// added the class's [Appearance] rules. Written from the generator's updater, the rules stay in the generated layer, so an
/// administrator's or user's differences still win. Verified API: docs/api-notes.md.
/// </summary>
public sealed class AppearanceRulesUpdater : ModelNodesGeneratorUpdater<AppearanceRulesModelNodesGenerator> {
    /// <summary>Model value marking a rules node this updater applied (RECHECK-001: XAF marks a node generated even when its updater threw).</summary>
    internal const string AppliedMarker = "XafLayoutBuilder.AppearanceApplied";

    internal static bool WasApplied(IModelAppearanceRules rules) => ((ModelNode)rules).GetValue<bool>(AppliedMarker);

    public override void UpdateNode(ModelNode node) {
        // Degrades like the layout updaters: logged, and the class keeps only its [Appearance] rules.
        try {
            Apply(node);
        }
        catch (Exception ex) when (!XafLayoutBuilderModule.FailFastOnLayoutErrors) {
            DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
        }
    }

    /// <summary>
    /// The checks run before any rule is added, shared with the startup check. XLB006: a spec rule whose id the generator already
    /// gave an [Appearance] rule of the class; adding it would throw DuplicateModelNodeIdException (ModelNode.cs 483-491). XLB008:
    /// criteria that do not parse; XAF parses them again when the view renders (AppearanceRule.cs 64-78) and would throw there,
    /// even with fail-fast off (Codex review).
    /// </summary>
    internal static void CheckAgainstClass(IModelAppearanceRules rules, AppearanceSpec spec, Type type) {
        foreach (var rule in spec.Rules) {
            if (rules[rule.Id] is { Attribute: not null })
                throw new LayoutSpecException($"XLB006 {type.Name}: appearance rule '{rule.Id}' has the id of an [Appearance] rule on the class; rename one of them.");
            if (rule.Criteria is null) continue;
            try {
                CriteriaOperator.Parse(rule.Criteria);
            }
            catch (Exception ex) {
                throw new LayoutSpecException($"XLB008 {type.Name}: rule '{rule.Id}' has criteria that do not parse: {ex.Message}");
            }
        }
    }

    static void Apply(ModelNode node) {
        if (node.Parent is not IModelClass modelClass || modelClass.TypeInfo?.Type is not { } type) return;
        if (AppearanceSpecResolver.For(type) is not { } spec) return;
        var rules = (IModelAppearanceRules)node;
        // Check first, change second (APPLY-001): a rejected spec adds none of its rules.
        CheckAgainstClass(rules, spec, type);
        for (var i = 0; i < spec.Rules.Count; i++) {
            var r = spec.Rules[i];
            var rule = rules.AddNode<IModelAppearanceRule>(r.Id);
            // The model lists rules by id; the index keeps the spec's order for the export. Priority, not order, decides at runtime.
            rule.Index = i;
            rule.AppearanceItemType = r.TargetKind == AppearanceTargetKind.Items ? nameof(AppearanceItemType.ViewItem) : nameof(AppearanceItemType.LayoutItem);
            rule.TargetItems = string.Join(", ", r.Targets);
            if (r.Criteria is not null) rule.Criteria = r.Criteria;
            if (r.Context is not null) rule.Context = r.Context;
            if (r.FontColor is not null) rule.FontColor = ToColor(r.FontColor);
            if (r.BackColor is not null) rule.BackColor = ToColor(r.BackColor);
            // Core's enums mirror DevExpress's by member name.
            if (r.FontStyle is { } style) rule.FontStyle = Enum.Parse<DXFontStyle>(style.ToString());
            if (r.Enabled is { } enabled) rule.Enabled = enabled;
            if (r.Visibility is { } visibility) rule.Visibility = Enum.Parse<ViewItemVisibility>(visibility.ToString());
            if (r.Priority is { } priority) rule.Priority = priority;
        }
        node.SetValue(AppliedMarker, true);
    }

    // The resolver validated every colour already.
    static Color ToColor(string text) =>
        AppearanceColors.TryParse(text, out var color) ? color : throw new LayoutSpecException($"'{text}' is not a colour.");
}
