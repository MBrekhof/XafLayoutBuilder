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

    /// <summary>Every member the layout places, depth first. A tree walk, never <see cref="Members"/> minus the hidden ones.</summary>
    public IEnumerable<string> PlacedMembers() => Nodes.SelectMany(Walk);

    /// <summary>Every member the layout references, placed then hidden, depth first.</summary>
    public IEnumerable<string> Members() => PlacedMembers().Concat(HiddenMembers);

    static IEnumerable<string> Walk(LayoutNodeSpec n) => n switch {
        LayoutItemSpec i => [i.Member],
        LayoutGroupSpec g => g.Children.SelectMany(Walk),
        TabbedGroupSpec t => t.Tabs.SelectMany(Walk),
        _ => [],
    };
}

/// <summary>
/// A listed column. <see cref="SortIndex"/> is this sorted column's sort priority (0 first) when it differs from column
/// order; null means sorted columns take priority in column order. Set on every sorted column of a list or on none.
/// <see cref="GroupIndex"/> (GROUP-001) groups the list by this column when it opens, 0 outermost; a grouped column takes no sort index.
/// </summary>
public sealed record ColumnSpec(
    string Member,
    int? Width = null,
    ColumnSortOrder SortOrder = ColumnSortOrder.None,
    string? Caption = null,
    int? SortIndex = null,
    string? Band = null,
    int? GroupIndex = null);

/// <summary>A band (BAND-001): a header over a run of adjacent columns. A null <see cref="Caption"/> shows the id, as XAF does.</summary>
public sealed record BandSpec(string Id, string? Caption = null);

/// <summary>
/// ListView columns for one type. Hidden columns stay available in the column chooser (applier sets Index = -1).
/// <see cref="Bands"/> (BAND-001) are headers over adjacent columns; a column names its band in <see cref="ColumnSpec.Band"/>.
/// </summary>
public sealed record ListColumnsSpec(
    string TypeName,
    IReadOnlyList<ColumnSpec> Columns,
    IReadOnlyList<string> HiddenMembers,
    ListColumnsSpec? Lookup = null,
    IReadOnlyList<BandSpec>? Bands = null,
    // GROUP-001: left out of the JSON while false, like the other optional fields.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool ShowGroupPanel = false) {
    readonly IReadOnlyList<ColumnSpec> columns = Columns.Frozen();
    readonly IReadOnlyList<string> hiddenMembers = HiddenMembers.Frozen();
    readonly IReadOnlyList<BandSpec>? bands = Bands?.Frozen();
    public IReadOnlyList<ColumnSpec> Columns { get => columns; init => columns = value.Frozen(); }
    public IReadOnlyList<string> HiddenMembers { get => hiddenMembers; init => hiddenMembers = value.Frozen(); }
    public IReadOnlyList<BandSpec>? Bands { get => bands; init => bands = value?.Frozen(); }

    /// <summary>Every member referenced, including the lookup's.</summary>
    public IEnumerable<string> Members() =>
        Columns.Select(c => c.Member).Concat(HiddenMembers).Concat(Lookup?.Members() ?? []);
}

public static partial class LayoutSpecChecks {
    /// <summary>
    /// Throws when a spec names a property or field that <paramref name="type"/> does not have. A dotted column path
    /// ("Customer.City") is followed segment by segment through each member's type. Used by the registry.
    /// </summary>
    public static void EnsureMembersExist(Type type, IEnumerable<string> members) {
        var missing = members.Distinct().Where(m => !Resolves(type, m)).ToList();
        if (missing.Count > 0)
            throw new LayoutSpecException($"{type.Name} has no member(s) {string.Join(", ", missing)} named in its layout spec.");

        static bool Resolves(Type type, string path) {
            Type? current = type;
            foreach (var segment in path.Split('.')) {
                if (current is null) return false;
                current = current.GetProperty(segment)?.PropertyType ?? current.GetField(segment)?.FieldType;
            }
            return current is not null;
        }
    }

    /// <summary>
    /// The structural rules, for a spec from any source: a builder (Build() calls this), a record constructed by hand or
    /// reshaped with <c>with</c>, or JSON. Throws <see cref="LayoutSpecException"/> on a blank id or member name, a
    /// member placed twice, a member both placed and hidden, a group id used twice in the view, two siblings sharing an
    /// id (XAF requires unique ids among siblings; an item's id is its member name), a catch-all group whose id is
    /// already taken at the root, and a placed or hidden member with a nested path (those are for columns).
    /// Parent/child reuse is fine, which is exactly what TabFor produces: group "Lines" holding item "Lines".
    /// </summary>
    public static void Validate(DetailLayoutSpec spec) {
        var type = ShortName(spec.TypeName);
        if (spec.HiddenMembers.Any(string.IsNullOrWhiteSpace)) throw new LayoutSpecException($"{type}: a hidden member has no name.");
        if (spec.Members().FirstOrDefault(m => m?.Contains('.') == true) is { } nested)
            throw new LayoutSpecException($"{type}: '{nested}' is a nested path. Nested paths are for columns; a detail item names a member of {type} itself.");
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
    /// The column rules, for a spec from any source: a blank member name or an empty segment in a nested path, a column
    /// listed twice, a column both listed and hidden, a lookup inside a lookup, a sort index that is on an unsorted or a
    /// grouped column, negative, used twice, or given on some sorted ungrouped columns but not all, and a group index that
    /// is negative or used twice. The lookup's own columns are checked the same way.
    /// </summary>
    public static void Validate(ListColumnsSpec spec) {
        var type = ShortName(spec.TypeName);
        Check(spec);
        if (spec.Lookup is { } lookup) {
            if (lookup.Lookup is not null) throw new LayoutSpecException($"{type}: Lookup() cannot be nested inside Lookup().");
            if (lookup.Bands is { Count: > 0 } || lookup.Columns.Any(c => c?.Band is not null))
                throw new LayoutSpecException($"{type}: bands are for the ListView, not for Lookup().");
            Check(lookup);
        }

        void Check(ListColumnsSpec s) {
            if (s.Columns.Any(c => string.IsNullOrWhiteSpace(c?.Member)) || s.HiddenMembers.Any(string.IsNullOrWhiteSpace))
                throw new LayoutSpecException($"{type}: a column has no member name.");
            // A column may follow references ("Customer.City"); every segment of that path needs a name.
            if (s.Columns.Select(c => c.Member).Concat(s.HiddenMembers).FirstOrDefault(m => m.Split('.').Any(string.IsNullOrWhiteSpace)) is { } broken)
                throw new LayoutSpecException($"{type}: column '{broken}' has an empty segment in its path.");
            // SORT-001: an explicit sort priority belongs to a sorted column, is not negative and is unique, and is given on
            // every sorted column of the list or on none. GROUP-001: DxGrid sorts a grouped column by its group index before
            // the others and keeps no sort index for it (docs/api-notes.md), so it takes none and is left out of that rule.
            foreach (var c in s.Columns.Where(c => c.SortIndex is not null)) {
                if (c.SortOrder == ColumnSortOrder.None)
                    throw new LayoutSpecException($"{type}: column '{c.Member}' has a sort index but no sort order.");
                if (c.SortIndex < 0)
                    throw new LayoutSpecException($"{type}: column '{c.Member}' has a negative sort index.");
                if (c.GroupIndex is not null)
                    throw new LayoutSpecException($"{type}: column '{c.Member}' is grouped, so it takes no sort index; its group index orders it.");
            }
            if (s.Columns.FirstOrDefault(c => c.GroupIndex < 0) is { } negativeGroup)
                throw new LayoutSpecException($"{type}: column '{negativeGroup.Member}' has a negative group index.");
            if (s.Columns.Where(c => c.GroupIndex is not null).GroupBy(c => c.GroupIndex).FirstOrDefault(g => g.Count() > 1) is { } twiceGroup)
                throw new LayoutSpecException($"{type}: group index {twiceGroup.Key} is used twice.");
            var sorted = s.Columns.Where(c => c.SortOrder != ColumnSortOrder.None && c.GroupIndex is null).ToList();
            if (sorted.Any(c => c.SortIndex is not null)) {
                if (sorted.Any(c => c.SortIndex is null))
                    throw new LayoutSpecException($"{type}: set sortIndex on every sorted column or on none.");
                if (sorted.GroupBy(c => c.SortIndex).FirstOrDefault(g => g.Count() > 1) is { } twice)
                    throw new LayoutSpecException($"{type}: sort index {twice.Key} is used twice.");
            }
            var hidden = s.HiddenMembers.ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in s.Columns) {
                if (!seen.Add(c.Member)) throw new LayoutSpecException($"{type}: column '{c.Member}' is listed twice.");
                if (hidden.Contains(c.Member)) throw new LayoutSpecException($"{type}: column '{c.Member}' is both listed and hidden.");
            }
            // BAND-001: every band has an id of its own and at least one column, every column's band is declared, and a band's
            // columns are next to each other (one level of bands over adjacent columns, which is what XAF Blazor renders).
            var bands = s.Bands ?? [];
            if (bands.Any(b => string.IsNullOrWhiteSpace(b?.Id))) throw new LayoutSpecException($"{type}: a band has no id.");
            if (bands.GroupBy(b => b.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twiceBand)
                throw new LayoutSpecException($"{type}: band id '{twiceBand.Key}' is used twice.");
            var declared = bands.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
            if (s.Columns.FirstOrDefault(c => c.Band is not null && !declared.Contains(c.Band)) is { } orphan)
                throw new LayoutSpecException($"{type}: column '{orphan.Member}' names band '{orphan.Band}', which is not declared.");
            foreach (var band in bands) {
                var positions = s.Columns.Select((c, i) => (c.Band, i)).Where(x => x.Band == band.Id).Select(x => x.i).ToList();
                if (positions.Count == 0) throw new LayoutSpecException($"{type}: band '{band.Id}' has no columns.");
                if (positions[^1] - positions[0] + 1 != positions.Count)
                    throw new LayoutSpecException($"{type}: the columns of band '{band.Id}' are not next to each other.");
            }
        }
    }

    /// <summary>
    /// Checks a detail spec against the view it is about to be applied to, changing nothing, and returns the visible
    /// editors the layout neither places nor hides, in view-item order: what a catch-all group collects.
    /// <paramref name="viewItems"/> are the ids of the view's items, <paramref name="visibleEditors"/> the ids of its
    /// property editors whose member is visible in the DetailView. Throws XLB001 when a placed member has no view item,
    /// and XLB002 when something is left over and the spec has no catch-all group.
    /// </summary>
    public static IReadOnlyList<string> CheckAgainstView(DetailLayoutSpec spec, string viewId, IEnumerable<string> viewItems, IEnumerable<string> visibleEditors) {
        var items = viewItems.ToHashSet(StringComparer.Ordinal);
        if (spec.PlacedMembers().FirstOrDefault(m => !items.Contains(m)) is { } missing)
            throw new LayoutSpecException(
                $"XLB001 {viewId}: member '{missing}' has no Items entry, so XAF generated no editor to place. " +
                "Is it [Browsable(false)], hidden with [HideInUI], or the reference back to its owner?");

        var placed = spec.PlacedMembers().ToHashSet(StringComparer.Ordinal);
        var hidden = spec.HiddenMembers.ToHashSet(StringComparer.Ordinal);
        var unplaced = visibleEditors.Where(id => !placed.Contains(id) && !hidden.Contains(id)).Distinct().ToList();
        if (unplaced.Count > 0 && spec.UnplacedGroupId is null)
            throw new LayoutSpecException(
                $"XLB002 {viewId}: members not placed and not hidden: {string.Join(", ", unplaced)}. " +
                $"Add .Item(x => x.{unplaced[0]}) or .Hide(x => x.{unplaced[0]}) to {ShortName(spec.TypeName)}'s layout, " +
                $"or relax it for this class with .Unplaced(UnplacedMembers.AppendToGroup(\"Other\")).");
        return unplaced;
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
