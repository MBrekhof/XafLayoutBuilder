using System.Drawing;
using System.Globalization;

namespace XafLayoutBuilder.Core;

// APPEAR-001: conditional appearance rules, plain records like the layout specs. XAF's Conditional Appearance module applies
// them; the add-on XafLayoutBuilder.Appearance writes them into the Application Model.

/// <summary>What a rule targets: members (their property editors and grid cells) or layout nodes by id (groups, tabs, items).</summary>
public enum AppearanceTargetKind { Items, Layout }

/// <summary>DevExpress.Drawing.DXFontStyle by name, so Core needs no DevExpress reference.</summary>
[Flags]
public enum AppearanceFontStyle { Regular = 0, Bold = 1, Italic = 2, Underline = 4, Strikeout = 8 }

/// <summary>DevExpress.ExpressApp.Editors.ViewItemVisibility by name.</summary>
public enum AppearanceVisibility { Show, Hide, ShowEmptySpace }

/// <summary>
/// One rule. Colours are names (Red) or #RRGGBB (<see cref="AppearanceColors"/>). <see cref="Context"/> is XAF's context
/// string: ListView, DetailView and view ids, separated by ';'; null means every view. A null value leaves that aspect alone.
/// </summary>
public sealed record AppearanceRuleSpec(
    string Id,
    AppearanceTargetKind TargetKind,
    IReadOnlyList<string> Targets,
    string? Criteria = null,
    string? Context = null,
    string? FontColor = null,
    string? BackColor = null,
    AppearanceFontStyle? FontStyle = null,
    bool? Enabled = null,
    AppearanceVisibility? Visibility = null,
    int? Priority = null) {
    readonly IReadOnlyList<string> targets = Targets.Frozen();
    public IReadOnlyList<string> Targets { get => targets; init => targets = value.Frozen(); }
}

/// <summary>The appearance rules of one type.</summary>
public sealed record AppearanceSpec(string TypeName, IReadOnlyList<AppearanceRuleSpec> Rules) {
    readonly IReadOnlyList<AppearanceRuleSpec> rules = Rules.Frozen();
    public IReadOnlyList<AppearanceRuleSpec> Rules { get => rules; init => rules = value.Frozen(); }

    /// <summary>Every member a rule targets; layout node ids are not members.</summary>
    public IEnumerable<string> Members() => Rules.Where(r => r.TargetKind == AppearanceTargetKind.Items).SelectMany(r => r.Targets);
}

/// <summary>
/// Opt-in, like <see cref="ISupportViewLayoutCustomization"/> but on its own, so a layout class without rules changes nothing.
/// Registry entries win over this for the same type.
/// </summary>
public interface ISupportAppearanceRules {
    static abstract AppearanceSpec? BuildAppearanceRules();
}

/// <summary>The colour text a rule takes: a known colour name (Red, DarkOrange) or #RRGGBB, #AARRGGBB with transparency.</summary>
public static class AppearanceColors {
    public static bool TryParse(string? text, out Color color) {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s[0] != '#') {
            color = Color.FromName(s);
            return color.IsKnownColor;
        }
        if (s.Length is not (7 or 9) || !uint.TryParse(s.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var argb)) return false;
        color = Color.FromArgb(unchecked((int)(s.Length == 7 ? argb | 0xFF000000 : argb)));
        return true;
    }

    /// <summary>A named colour prints as its name, any other as #RRGGBB, or #AARRGGBB when it is not opaque.</summary>
    public static string Format(Color color) =>
        color.IsNamedColor ? color.Name
        : color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}

public static partial class LayoutSpecChecks {
    /// <summary>
    /// The rules' own checks, for a spec from any source: a blank or repeated rule id, a rule without targets or with a blank
    /// target (or an empty segment in a member path), a rule that sets no appearance (XAF skips such a rule), and a colour
    /// <see cref="AppearanceColors"/> cannot read. Whether the targets exist is checked against the model by the add-on.
    /// </summary>
    public static void Validate(AppearanceSpec spec) {
        var type = ShortName(spec.TypeName);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in spec.Rules) {
            if (string.IsNullOrWhiteSpace(rule?.Id)) throw new LayoutSpecException($"{type}: a rule has no id.");
            if (!ids.Add(rule.Id)) throw new LayoutSpecException($"{type}: rule id '{rule.Id}' is used twice.");
            if (rule.Targets is not { Count: > 0 }) throw new LayoutSpecException($"{type}: rule '{rule.Id}' has no target.");
            if (rule.Targets.Any(t => string.IsNullOrWhiteSpace(t)
                    || (rule.TargetKind == AppearanceTargetKind.Items && t.Split('.').Any(string.IsNullOrWhiteSpace))))
                throw new LayoutSpecException($"{type}: rule '{rule.Id}' has a blank target.");
            if (rule is { FontColor: null, BackColor: null, FontStyle: null, Enabled: null, Visibility: null })
                throw new LayoutSpecException($"{type}: rule '{rule.Id}' sets no appearance; give it a colour, a font style, Enabled or Visibility.");
            foreach (var colour in new[] { rule.FontColor, rule.BackColor })
                if (colour is not null && !AppearanceColors.TryParse(colour, out _))
                    throw new LayoutSpecException($"{type}: rule '{rule.Id}': '{colour}' is not a colour; use a colour name or #RRGGBB.");
        }
    }
}
