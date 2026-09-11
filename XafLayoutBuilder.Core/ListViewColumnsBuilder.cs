using System.Linq.Expressions;

namespace XafLayoutBuilder.Core;

/// <summary>Fluent ListView columns for <typeparamref name="T"/>. Column order = call order; sort index = order of sorted columns.</summary>
public sealed class ListViewColumnsBuilder<T> {
    readonly List<ColumnSpec> columns = [];
    readonly HashSet<string> hidden = [];
    readonly bool isLookup;
    ListColumnsSpec? lookup;

    ListViewColumnsBuilder(bool isLookup) { this.isLookup = isLookup; }

    public static ListViewColumnsBuilder<T> Create() => new(isLookup: false);

    public ListViewColumnsBuilder<T> Column(Expression<Func<T, object?>> member, int? width = null,
        ColumnSortOrder sort = ColumnSortOrder.None, string? caption = null) {
        columns.Add(new ColumnSpec(MemberPath.Of(member), width, sort, caption));
        return this;
    }

    /// <summary>Not shown by default, but still offered in the column chooser (applier sets Index = -1).</summary>
    public ListViewColumnsBuilder<T> Hide(Expression<Func<T, object?>> member) {
        hidden.Add(MemberPath.Of(member));
        return this;
    }

    /// <summary>Separate column set for {Type}_LookupListView. Without it the lookup is left to XAF.</summary>
    public ListViewColumnsBuilder<T> Lookup(Action<ListViewColumnsBuilder<T>> configure) {
        if (isLookup) throw new LayoutSpecException($"{typeof(T).Name}: Lookup() cannot be nested inside Lookup().");
        var l = new ListViewColumnsBuilder<T>(isLookup: true);
        configure(l);
        lookup = l.Build();
        return this;
    }

    public ListColumnsSpec Build() {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in columns) {
            if (!seen.Add(c.Member)) throw new LayoutSpecException($"{typeof(T).Name}: column '{c.Member}' is listed twice.");
            if (hidden.Contains(c.Member)) throw new LayoutSpecException($"{typeof(T).Name}: column '{c.Member}' is both listed and hidden.");
        }
        return new ListColumnsSpec(typeof(T).FullName!, columns.ToArray(), hidden.ToArray(), lookup);
    }
}
