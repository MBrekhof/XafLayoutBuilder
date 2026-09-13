using System.Linq.Expressions;

namespace XafLayoutBuilder.Core;

/// <summary>Fluent ListView columns for <typeparamref name="T"/>. Column order = call order; sort index = order of sorted columns.</summary>
public sealed class ListViewColumnsBuilder<T> {
    readonly List<ColumnSpec> columns = [];
    readonly HashSet<string> hidden = [];
    readonly List<BandSpec> bands = [];
    readonly bool isLookup;
    ListColumnsSpec? lookup;
    string? currentBand;

    ListViewColumnsBuilder(bool isLookup) { this.isLookup = isLookup; }

    public static ListViewColumnsBuilder<T> Create() => new(isLookup: false);

    /// <summary>HIER-001: starts from <typeparamref name="TBase"/>'s columns, lookup and bands included; further calls add to them.</summary>
    public static ListViewColumnsBuilder<T> Extend<TBase>() where TBase : ISupportViewLayoutCustomization =>
        Extend(TBase.BuildListViewColumns()
            ?? throw new LayoutSpecException($"{typeof(T).Name}: {typeof(TBase).Name} has no ListView columns to extend."));

    /// <summary>HIER-001: starts from <paramref name="baseColumns"/>, the columns of a class <typeparamref name="T"/> derives from.</summary>
    public static ListViewColumnsBuilder<T> Extend(ListColumnsSpec baseColumns) {
        Hierarchy.EnsureDerives(typeof(T), baseColumns.TypeName);
        var builder = new ListViewColumnsBuilder<T>(isLookup: false);
        builder.columns.AddRange(baseColumns.Columns);
        builder.hidden.UnionWith(baseColumns.HiddenMembers);
        builder.bands.AddRange(baseColumns.Bands ?? []);
        builder.lookup = baseColumns.Lookup is { } lookup ? lookup with { TypeName = typeof(T).FullName! } : null;
        return builder;
    }

    /// <summary>
    /// <paramref name="member"/> may follow references: <c>x => x.Customer.City</c> is the column "Customer.City".
    /// <paramref name="sortIndex"/> is this sorted column's sort priority (0 first) when it should differ from column order;
    /// set it on every sorted column or on none.
    /// </summary>
    public ListViewColumnsBuilder<T> Column(Expression<Func<T, object?>> member, int? width = null,
        ColumnSortOrder sort = ColumnSortOrder.None, string? caption = null, int? sortIndex = null) {
        columns.Add(new ColumnSpec(MemberPath.ChainOf(member), width, sort, caption, sortIndex, currentBand));
        return this;
    }

    /// <summary>
    /// A band (BAND-001): a header over the columns <paramref name="columns"/> declares, which keep their place in the column
    /// order. A null <paramref name="caption"/> shows the id. One level only: XAF Blazor renders no band inside a band.
    /// </summary>
    public ListViewColumnsBuilder<T> Band(string id, Action<ListViewColumnsBuilder<T>> columns, string? caption = null) {
        if (currentBand is not null) throw new LayoutSpecException($"{typeof(T).Name}: Band() cannot be nested inside Band().");
        bands.Add(new BandSpec(id, caption));
        currentBand = id;
        try {
            columns(this);
        }
        finally {
            currentBand = null;
        }
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
        if (currentBand is not null) throw new LayoutSpecException($"{typeof(T).Name}: Lookup() cannot be declared inside Band().");
        var l = new ListViewColumnsBuilder<T>(isLookup: true);
        configure(l);
        lookup = l.Build();
        return this;
    }

    /// <summary>Freezes and validates with <see cref="LayoutSpecChecks.Validate(ListColumnsSpec)"/>, which lists the rules.</summary>
    public ListColumnsSpec Build() {
        var spec = new ListColumnsSpec(typeof(T).FullName!, columns.ToArray(), hidden.ToArray(), lookup, bands.Count == 0 ? null : bands.ToArray());
        LayoutSpecChecks.Validate(spec);
        return spec;
    }
}
