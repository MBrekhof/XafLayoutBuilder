using System.Linq.Expressions;

namespace XafLayoutBuilder.Core;

/// <summary>
/// APPEAR-001: fluent conditional appearance rules for <typeparamref name="T"/>, applied by the XafLayoutBuilder.Appearance
/// add-on through XAF's Conditional Appearance module.
/// </summary>
public sealed class AppearanceBuilder<T> {
    readonly List<AppearanceRuleSpec> rules = [];

    AppearanceBuilder() { }

    public static AppearanceBuilder<T> Create() => new();

    /// <summary>A rule with an <paramref name="id"/> unique in the class and not taken by an [Appearance] attribute on it (XLB006).</summary>
    public AppearanceBuilder<T> Rule(string id, Action<AppearanceRuleBuilder<T>> configure) {
        var rule = new AppearanceRuleBuilder<T>();
        configure(rule);
        rules.Add(rule.Build(id));
        return this;
    }

    /// <summary>Freezes and validates with <see cref="LayoutSpecChecks.Validate(AppearanceSpec)"/>, which lists the rules.</summary>
    public AppearanceSpec Build() {
        var spec = new AppearanceSpec(typeof(T).FullName!, rules.ToArray());
        LayoutSpecChecks.Validate(spec);
        return spec;
    }
}

/// <summary>One rule: its targets, an optional criteria, what it changes, and the views it applies in (every view by default).</summary>
public sealed class AppearanceRuleBuilder<T> {
    readonly List<string> targets = [];
    readonly List<string> contexts = [];
    AppearanceTargetKind? kind;
    string? criteria;
    string? fontColor;
    string? backColor;
    AppearanceFontStyle? fontStyle;
    bool? enabled;
    AppearanceVisibility? visibility;
    int? priority;

    internal AppearanceRuleBuilder() { }

    /// <summary>XAF criteria over the object, such as <c>[OrderDate] &lt; LocalDateTimeToday()</c>. Without it the rule always applies.</summary>
    public AppearanceRuleBuilder<T> When(string criteria) {
        this.criteria = criteria;
        return this;
    }

    /// <summary>Members whose property editors and grid cells the rule styles. A member may follow references.</summary>
    public AppearanceRuleBuilder<T> On(params Expression<Func<T, object?>>[] members) =>
        Target(AppearanceTargetKind.Items, members.Select(m => MemberPath.ChainOf(m)));

    /// <summary>DetailView layout nodes by id: groups, tabs and layout items. A rule targets members or layout nodes, not both.</summary>
    public AppearanceRuleBuilder<T> OnLayout(params string[] ids) => Target(AppearanceTargetKind.Layout, ids);

    /// <summary>A colour name (Red) or #RRGGBB.</summary>
    public AppearanceRuleBuilder<T> FontColor(string color) {
        fontColor = color;
        return this;
    }

    /// <summary>A colour name or #RRGGBB.</summary>
    public AppearanceRuleBuilder<T> BackColor(string color) {
        backColor = color;
        return this;
    }

    public AppearanceRuleBuilder<T> FontStyle(AppearanceFontStyle style) {
        fontStyle = style;
        return this;
    }

    public AppearanceRuleBuilder<T> Enabled(bool enabled) {
        this.enabled = enabled;
        return this;
    }

    public AppearanceRuleBuilder<T> Visibility(AppearanceVisibility visibility) {
        this.visibility = visibility;
        return this;
    }

    /// <summary>Decides between rules that colour the same item; the higher wins.</summary>
    public AppearanceRuleBuilder<T> Priority(int priority) {
        this.priority = priority;
        return this;
    }

    public AppearanceRuleBuilder<T> InListView() => In("ListView");

    public AppearanceRuleBuilder<T> InDetailView() => In("DetailView");

    /// <summary>A view by id. Combines with the other In calls: <c>.InListView().InView("Order_Compact_DetailView")</c>.</summary>
    public AppearanceRuleBuilder<T> InView(string viewId) => In(viewId);

    AppearanceRuleBuilder<T> Target(AppearanceTargetKind targetKind, IEnumerable<string> names) {
        if (kind is not null && kind != targetKind)
            throw new LayoutSpecException($"{typeof(T).Name}: a rule targets either members or layout nodes, not both.");
        kind = targetKind;
        targets.AddRange(names);
        return this;
    }

    AppearanceRuleBuilder<T> In(string context) {
        contexts.Add(context);
        return this;
    }

    internal AppearanceRuleSpec Build(string id) =>
        new(id, kind ?? AppearanceTargetKind.Items, targets.ToArray(), criteria, contexts.Count == 0 ? null : string.Join(";", contexts),
            fontColor, backColor, fontStyle, enabled, visibility, priority);
}
