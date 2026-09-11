using System.Text.Json.Serialization;

namespace XafLayoutBuilder.Core;

// The immutable IR. Plain records, no XAF types, JSON-serialisable (see LayoutSpecJson).
// Builder -> spec -> applier, and exporter -> spec -> printer both meet here.
// Every list property freezes in its init accessor, so the constructor, JSON deserialisation and
// `with { ... }` expressions all end up holding a private read-only copy.

public enum FlowDirection { Vertical, Horizontal }

public enum ColumnSortOrder { None, Ascending, Descending }

[JsonDerivedType(typeof(LayoutGroupSpec), "group")]
[JsonDerivedType(typeof(TabbedGroupSpec), "tabs")]
[JsonDerivedType(typeof(LayoutItemSpec), "item")]
public abstract record LayoutNodeSpec;

/// <summary>A property editor placed in the layout. <see cref="Member"/> is the simple member name.</summary>
public sealed record LayoutItemSpec(string Member, double? RelativeSize = null) : LayoutNodeSpec;

public sealed record LayoutGroupSpec(
    string Id,
    IReadOnlyList<LayoutNodeSpec> Children,
    string? Caption = null,
    FlowDirection Direction = FlowDirection.Vertical,
    bool Collapsible = false,
    double? RelativeSize = null,
    string? ImageName = null) : LayoutNodeSpec {
    readonly IReadOnlyList<LayoutNodeSpec> children = Children.Frozen();
    public IReadOnlyList<LayoutNodeSpec> Children { get => children; init => children = value.Frozen(); }
}

/// <summary>A tab control. Each tab is a <see cref="LayoutGroupSpec"/>; XAF requires tabbed-group children to be groups.</summary>
public sealed record TabbedGroupSpec(string Id, IReadOnlyList<LayoutGroupSpec> Tabs) : LayoutNodeSpec {
    readonly IReadOnlyList<LayoutGroupSpec> tabs = Tabs.Frozen();
    public IReadOnlyList<LayoutGroupSpec> Tabs { get => tabs; init => tabs = value.Frozen(); }
}

/// <summary>DetailView layout for one type. Members in <see cref="HiddenMembers"/> are simply not placed.</summary>
public sealed record DetailLayoutSpec(
    string TypeName,
    IReadOnlyList<LayoutNodeSpec> Nodes,
    IReadOnlyList<string> HiddenMembers) {
    readonly IReadOnlyList<LayoutNodeSpec> nodes = Nodes.Frozen();
    readonly IReadOnlyList<string> hiddenMembers = HiddenMembers.Frozen();
    public IReadOnlyList<LayoutNodeSpec> Nodes { get => nodes; init => nodes = value.Frozen(); }
    public IReadOnlyList<string> HiddenMembers { get => hiddenMembers; init => hiddenMembers = value.Frozen(); }

    /// <summary>Every member the layout references, placed then hidden, depth first.</summary>
    public IEnumerable<string> Members() => Nodes.SelectMany(Walk).Concat(HiddenMembers);

    static IEnumerable<string> Walk(LayoutNodeSpec n) => n switch {
        LayoutItemSpec i => [i.Member],
        LayoutGroupSpec g => g.Children.SelectMany(Walk),
        TabbedGroupSpec t => t.Tabs.SelectMany(Walk),
        _ => [],
    };
}

public sealed record ColumnSpec(
    string Member,
    int? Width = null,
    ColumnSortOrder SortOrder = ColumnSortOrder.None,
    string? Caption = null);

/// <summary>ListView columns for one type. Hidden columns stay available in the column chooser (applier sets Index = -1).</summary>
public sealed record ListColumnsSpec(
    string TypeName,
    IReadOnlyList<ColumnSpec> Columns,
    IReadOnlyList<string> HiddenMembers,
    ListColumnsSpec? Lookup = null) {
    readonly IReadOnlyList<ColumnSpec> columns = Columns.Frozen();
    readonly IReadOnlyList<string> hiddenMembers = HiddenMembers.Frozen();
    public IReadOnlyList<ColumnSpec> Columns { get => columns; init => columns = value.Frozen(); }
    public IReadOnlyList<string> HiddenMembers { get => hiddenMembers; init => hiddenMembers = value.Frozen(); }

    /// <summary>Every member referenced, including the lookup's.</summary>
    public IEnumerable<string> Members() =>
        Columns.Select(c => c.Member).Concat(HiddenMembers).Concat(Lookup?.Members() ?? []);
}

public static class LayoutSpecChecks {
    /// <summary>Throws when a spec names a property or field that <paramref name="type"/> does not have. Used by the registry.</summary>
    public static void EnsureMembersExist(Type type, IEnumerable<string> members) {
        var missing = members.Distinct().Where(m => type.GetProperty(m) is null && type.GetField(m) is null).ToList();
        if (missing.Count > 0)
            throw new LayoutSpecException($"{type.Name} has no member(s) {string.Join(", ", missing)} named in its layout spec.");
    }
}

internal static class FrozenList {
    /// <summary>Always a fresh read-only copy: a caller's ReadOnlyCollection still wraps the caller's mutable list.</summary>
    public static IReadOnlyList<T> Frozen<T>(this IReadOnlyList<T> list) => Array.AsReadOnly(list.ToArray());
}

public sealed class LayoutSpecException(string message) : Exception(message);

/// <summary>
/// Opt-in by convention. A class implementing this declares both views; return null from one
/// to leave that view to XAF. Registry entries win over this for the same type.
/// </summary>
public interface ISupportViewLayoutCustomization {
    static abstract DetailLayoutSpec? BuildDetailViewLayout();
    static abstract ListColumnsSpec? BuildListViewColumns();
}
