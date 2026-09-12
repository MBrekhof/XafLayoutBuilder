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

/// <summary>
/// What the applier does with a visible member the layout neither places nor hides. The default is to fail at
/// startup (XLB002), so a property added to the class cannot silently disappear from the form. A team that would
/// rather keep moving can relax it per class with <see cref="AppendToGroup"/>.
/// </summary>
public sealed class UnplacedMembers {
    UnplacedMembers(string? groupId) => GroupId = groupId;

    /// <summary>The default: a member that is neither placed nor hidden fails the application at startup.</summary>
    public static UnplacedMembers Fail { get; } = new(null);

    /// <summary>Put whatever the layout did not mention into a group with this id, at the end of the form.</summary>
    public static UnplacedMembers AppendToGroup(string groupId) =>
        string.IsNullOrWhiteSpace(groupId)
            ? throw new LayoutSpecException("UnplacedMembers.AppendToGroup needs a group id.")
            : new UnplacedMembers(groupId);

    internal string? GroupId { get; }
}

/// <summary>
/// DetailView layout for one type. Members in <see cref="HiddenMembers"/> are simply not placed.
/// <see cref="UnplacedGroupId"/> is null for the strict default and otherwise names the catch-all group.
/// </summary>
public sealed record DetailLayoutSpec(
    string TypeName,
    IReadOnlyList<LayoutNodeSpec> Nodes,
    IReadOnlyList<string> HiddenMembers,
    string? UnplacedGroupId = null) {
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

    /// <summary>
    /// The structural rules, for a spec from any source: a builder (Build() calls this), a record constructed by hand or
    /// reshaped with <c>with</c>, or JSON. Throws <see cref="LayoutSpecException"/> on a blank id or member name, a
    /// member placed twice, a member both placed and hidden, a group id used twice in the view, two siblings sharing an
    /// id (XAF requires unique ids among siblings; an item's id is its member name), and a catch-all group whose id is
    /// already taken at the root. Parent/child reuse is fine, which is exactly what TabFor produces: group "Lines"
    /// holding item "Lines".
    /// </summary>
    public static void Validate(DetailLayoutSpec spec) {
        var type = ShortName(spec.TypeName);
        if (spec.HiddenMembers.Any(string.IsNullOrWhiteSpace)) throw new LayoutSpecException($"{type}: a hidden member has no name.");
        var hidden = spec.HiddenMembers.ToHashSet(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        Walk(spec.Nodes);

        // The catch-all group is created next to the layout's own top-level nodes, so its id has to be free there:
        // an item at the root counts too, because XAF only requires ids to be unique among siblings.
        if (spec.UnplacedGroupId is { } catchAll) {
            if (string.IsNullOrWhiteSpace(catchAll)) throw new LayoutSpecException($"{type}: the catch-all group from Unplaced needs an id.");
            if (!groupIds.Add(catchAll) || spec.Nodes.Any(n => IdOf(n) == catchAll))
                throw new LayoutSpecException(
                    $"{type}: id '{catchAll}' is already used in this layout; the catch-all group from Unplaced needs an id of its own.");
        }

        void Walk(IEnumerable<LayoutNodeSpec> siblings) {
            var siblingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in siblings) {
                var id = IdOf(node);
                if (string.IsNullOrWhiteSpace(id)) throw new LayoutSpecException($"{type}: a layout node has no id or member name.");
                switch (node) {
                    case LayoutItemSpec:
                        if (!members.Add(id)) throw new LayoutSpecException($"{type}: member '{id}' is placed twice.");
                        if (hidden.Contains(id)) throw new LayoutSpecException($"{type}: member '{id}' is both placed and hidden.");
                        break;
                    case LayoutGroupSpec g:
                        if (!groupIds.Add(id)) throw new LayoutSpecException($"{type}: group id '{id}' is used twice.");
                        Walk(g.Children);
                        break;
                    case TabbedGroupSpec t:
                        if (!groupIds.Add(id)) throw new LayoutSpecException($"{type}: group id '{id}' is used twice.");
                        Walk(t.Tabs);
                        break;
                }
                // Same-kind duplicates were caught above, so a repeat here is a group next to an item with the same id.
                if (!siblingIds.Add(id))
                    throw new LayoutSpecException($"{type}: '{id}' names both a group and an item under the same parent; ids must be unique among siblings.");
            }
        }

        static string IdOf(LayoutNodeSpec? node) => node switch {
            LayoutItemSpec i => i.Member,
            LayoutGroupSpec g => g.Id,
            TabbedGroupSpec t => t.Id,
            _ => "",
        };
    }

    /// <summary>
    /// The column rules, for a spec from any source: a blank member name, a column listed twice, a column both listed
    /// and hidden, and a lookup inside a lookup. The lookup's own columns are checked the same way.
    /// </summary>
    public static void Validate(ListColumnsSpec spec) {
        var type = ShortName(spec.TypeName);
        Check(spec);
        if (spec.Lookup is { } lookup) {
            if (lookup.Lookup is not null) throw new LayoutSpecException($"{type}: Lookup() cannot be nested inside Lookup().");
            Check(lookup);
        }

        void Check(ListColumnsSpec s) {
            if (s.Columns.Any(c => string.IsNullOrWhiteSpace(c?.Member)) || s.HiddenMembers.Any(string.IsNullOrWhiteSpace))
                throw new LayoutSpecException($"{type}: a column has no member name.");
            var hidden = s.HiddenMembers.ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in s.Columns) {
                if (!seen.Add(c.Member)) throw new LayoutSpecException($"{type}: column '{c.Member}' is listed twice.");
                if (hidden.Contains(c.Member)) throw new LayoutSpecException($"{type}: column '{c.Member}' is both listed and hidden.");
            }
        }
    }

    // Messages name the type the way typeof(T).Name does: "Order" for "Sample.Order", "Inner" for "Sample.Outer+Inner".
    static string ShortName(string? typeName) =>
        string.IsNullOrEmpty(typeName) ? "(no type name)" : typeName[(typeName.LastIndexOfAny(['.', '+']) + 1)..];
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
