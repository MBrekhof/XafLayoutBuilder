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

    /// <summary><paramref name="member"/> may follow references: <c>x => x.Customer.City</c> is the column "Customer.City".</summary>
    public ListViewColumnsBuilder<T> Column(Expression<Func<T, object?>> member, int? width = null,
        ColumnSortOrder sort = ColumnSortOrder.None, string? caption = null) {
        columns.Add(new ColumnSpec(MemberPath.ChainOf(member), width, sort, caption));
        return this;
    }

    /// <summary>Not shown by default, but still offered in the column chooser (applier sets Index = -1). May follow references too.</summary>
    public ListViewColumnsBuilder<T> Hide(Expression<Func<T, object?>> member) {
        hidden.Add(MemberPath.ChainOf(member));
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

    /// <summary>Freezes and validates with <see cref="LayoutSpecChecks.Validate(ListColumnsSpec)"/>, which lists the rules.</summary>
    public ListColumnsSpec Build() {
        var spec = new ListColumnsSpec(typeof(T).FullName!, columns.ToArray(), hidden.ToArray(), lookup);
        LayoutSpecChecks.Validate(spec);
        return spec;
    }
}
