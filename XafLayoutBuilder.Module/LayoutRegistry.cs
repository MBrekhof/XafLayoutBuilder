using System.Collections.Concurrent;
using System.Reflection;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>Explicit registration for types you don't own. Entries here win over <see cref="ISupportViewLayoutCustomization"/>.</summary>
public static class LayoutRegistry {
    internal static readonly ConcurrentDictionary<Type, (Func<DetailLayoutSpec?>? Detail, Func<ListColumnsSpec?>? Columns)> Entries = new();
    static int version;

    /// <summary>
    /// Bumped by every registration change. <see cref="LayoutStartupCheck"/> keys its "already validated" memory on
    /// it, so a spec registered after an application has started is still checked for the next one.
    /// </summary>
    internal static int Version => Volatile.Read(ref version);

    /// <summary>
    /// Registers specs for <typeparamref name="T"/>. Nothing is checked here: the structural rules
    /// (<see cref="LayoutSpecChecks.Validate(DetailLayoutSpec)"/>) and whether every named member exists on
    /// <typeparamref name="T"/> are checked when a view is resolved, inside the updaters and the startup check, so
    /// <see cref="XafLayoutBuilderModule.FailFastOnLayoutErrors"/> decides whether a broken registration stops the
    /// application, exactly as for any other layout error.
    /// </summary>
    public static void Register<T>(DetailLayoutSpec? detail, ListColumnsSpec? columns) {
        Func<DetailLayoutSpec?>? detailFactory = detail is null ? null : () => detail;
        Func<ListColumnsSpec?>? columnsFactory = columns is null ? null : () => columns;
        Register<T>(detailFactory, columnsFactory);
    }

    /// <summary>
    /// Registers factories for <typeparamref name="T"/>, invoked when a view is resolved. Use this form when building the
    /// spec can throw: a <c>Build()</c> written in the arguments of the other overload throws in your own startup code,
    /// before the registry is reached, while an exception from a factory is governed by
    /// <see cref="XafLayoutBuilderModule.FailFastOnLayoutErrors"/> like any layout error.
    /// </summary>
    public static void Register<T>(Func<DetailLayoutSpec?>? detail, Func<ListColumnsSpec?>? columns) {
        Entries[typeof(T)] = (detail, columns);
        Interlocked.Increment(ref version);
    }

    /// <summary>
    /// Registers a <see cref="LayoutSpecs"/> JSON document, the form the JSON export writes, for <typeparamref name="T"/>.
    /// <paramref name="json"/> is read inside the factories, when a view is resolved, so malformed JSON or a half written
    /// for another type is a layout error governed by <see cref="XafLayoutBuilderModule.FailFastOnLayoutErrors"/>. Each half
    /// is read and checked on its own, like the two factories of <see cref="Register{T}(Func{DetailLayoutSpec?}?, Func{ListColumnsSpec?}?)"/>.
    /// </summary>
    public static void RegisterJson<T>(Func<string> json) => Register<T>(
        () => ForType<T, DetailLayoutSpec>(ReadHalf<T, DetailLayoutSpec>(json(), "detail"), s => s.TypeName),
        () => ForType<T, ListColumnsSpec>(ReadHalf<T, ListColumnsSpec>(json(), "columns"), s => s.TypeName));

    // Parses the document, then deserialises only the requested half, so a broken detail cannot cost the columns their
    // spec or the other way round. Malformed JSON still fails both halves: neither can be read from it.
    internal static TSpec? ReadHalf<T, TSpec>(string json, string half) where TSpec : class {
        try {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new LayoutSpecException($"Layout JSON registered for {typeof(T).FullName} is not an object with detail and columns.");
            foreach (var property in document.RootElement.EnumerateObject())
                if (string.Equals(property.Name, half, StringComparison.OrdinalIgnoreCase))
                    return System.Text.Json.JsonSerializer.Deserialize<TSpec>(property.Value, LayoutSpecJson.Options);
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException) {
            throw new LayoutSpecException($"Invalid layout JSON for the {half} of {typeof(T).FullName}: {ex.Message}");
        }
    }

    static TSpec? ForType<T, TSpec>(TSpec? spec, Func<TSpec, string> typeName) where TSpec : class =>
        spec is null || typeName(spec) == typeof(T).FullName
            ? spec
            : throw new LayoutSpecException($"Layout JSON registered for {typeof(T).FullName} describes {typeName(spec)}.");

    /// <summary>Views declared in code (VIEW-001), by view id.</summary>
    internal static readonly ConcurrentDictionary<string, DeclaredView> Views = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares a DetailView of <typeparamref name="T"/> with its own id and layout, next to the class's default one
    /// (VIEW-001). The module adds it to the generated layer, so a navigation item, a view variant or ShowViewParameters can
    /// open it by id, and its layout is checked at startup like any other. Register before the application model is built.
    /// A view that exists only in XAFML cannot take a builder layout: XAF never runs the layout generator, or its updaters,
    /// for such a view. Nothing is checked here; a blank or taken id is XLB005 when the view is added.
    /// </summary>
    public static void AddDetailView<T>(string viewId, Func<DetailLayoutSpec?> layout) => AddView(viewId, new(typeof(T), layout, null));

    /// <summary>
    /// Declares a ListView of <typeparamref name="T"/> with its own id and columns (VIEW-001), like
    /// <see cref="AddDetailView{T}"/>. A <c>Lookup(...)</c> in its spec is ignored: the lookup belongs to the class.
    /// </summary>
    public static void AddListView<T>(string viewId, Func<ListColumnsSpec?> columns) => AddView(viewId, new(typeof(T), null, columns));

    // The same view declared again (a module constructor runs once per application instance) replaces the earlier factory,
    // like Register. The same id for another class or another kind of view is kept as a conflict and reported as XLB005
    // when the views are added, instead of the later declaration silently winning.
    static void AddView(string? viewId, DeclaredView view) {
        Views.AddOrUpdate(viewId ?? "", view, (_, earlier) =>
            earlier.Conflict is null && earlier.Type == view.Type && (earlier.Detail is null) == (view.Detail is null)
                ? view
                : view with { Conflict = earlier.Conflict ?? earlier.Describe });
        Interlocked.Increment(ref version);
    }

    /// <summary>Test hook. Not needed by applications.</summary>
    public static void Clear() {
        Entries.Clear();
        Views.Clear();
        Interlocked.Increment(ref version);
    }
}

/// <summary>
/// A view declared in code: the class it shows, and either a layout factory or a columns factory. <see cref="Conflict"/>
/// describes an earlier declaration of the same id for another class or kind of view.
/// </summary>
internal sealed record DeclaredView(Type Type, Func<DetailLayoutSpec?>? Detail, Func<ListColumnsSpec?>? Columns, string? Conflict = null) {
    public string Describe => $"a {(Detail is not null ? "DetailView" : "ListView")} of {Type.Name}";
}

/// <summary>
/// Registry first, then the static abstract members of <see cref="ISupportViewLayoutCustomization"/>. The DetailView and
/// ListView halves are resolved and cached on their own, so a broken factory for one never costs the other its spec.
/// A factory that throws is not cached: every caller sees the same failure.
/// </summary>
internal static class LayoutSpecResolver {
    static readonly ConcurrentDictionary<Type, DetailLayoutSpec?> details = new();
    static readonly ConcurrentDictionary<Type, ListColumnsSpec?> columnSpecs = new();

    public static DetailLayoutSpec? Detail(Type type) =>
        LayoutRegistry.Entries.TryGetValue(type, out var registered)
            ? FromRegistry(type, registered.Detail, LayoutSpecChecks.Validate, s => s.Members())
            : details.GetOrAdd(type, static t => FromInterface<DetailLayoutSpec>(
                t, nameof(ISupportViewLayoutCustomization.BuildDetailViewLayout), s => s.TypeName, LayoutSpecChecks.Validate));

    public static ListColumnsSpec? Columns(Type type) =>
        LayoutRegistry.Entries.TryGetValue(type, out var registered)
            ? FromRegistry(type, registered.Columns, LayoutSpecChecks.Validate, s => s.Members())
            : columnSpecs.GetOrAdd(type, static t => FromInterface<ListColumnsSpec>(
                t, nameof(ISupportViewLayoutCustomization.BuildListViewColumns), s => s.TypeName, LayoutSpecChecks.Validate));

    /// <summary>
    /// The layout for a DetailView by its id: a declared view's own (VIEW-001), else the type's own for {Type}_DetailView,
    /// else none (variants and views defined in XAFML keep XAF's).
    /// </summary>
    public static DetailLayoutSpec? DetailForView(IModelView view, Type type) =>
        IsDeclared(view, type, out var declared)
            ? FromRegistry(type, declared.Detail, LayoutSpecChecks.Validate, s => s.Members())
            : view.Id == type.Name + "_DetailView" ? Detail(type) : null;

    /// <summary>The columns of a ListView declared in code (VIEW-001), or null when the view is not a declared one.</summary>
    public static ListColumnsSpec? DeclaredColumns(IModelView view, Type type) =>
        IsDeclared(view, type, out var declared)
            ? FromRegistry(type, declared.Columns, LayoutSpecChecks.Validate, s => s.Members())
            : null;

    // Only a view DeclaredViewsUpdater added carries its marker: a declaration rejected for a taken id (XLB005) must not hand
    // its spec to the view that already had that id.
    static bool IsDeclared(IModelView view, Type type, out DeclaredView declared) =>
        LayoutRegistry.Views.TryGetValue(view.Id, out declared!) && declared.Type == type
        && ((ModelNode)view).GetValue<bool>(DeclaredViewsUpdater.Marker);

    // Registration checks nothing, so everything is checked here, inside the fail-fast policy: the factory itself, the
    // structural rules, and that every member the spec names exists on the registered type.
    // ponytail: registry entries are not cached, so a registered factory runs on every resolution (a few per view per
    // process); cache by LayoutRegistry.Version if a factory ever becomes expensive.
    static TSpec? FromRegistry<TSpec>(Type type, Func<TSpec?>? factory, Action<TSpec> validate, Func<TSpec, IEnumerable<string>> members)
        where TSpec : class {
        if (factory?.Invoke() is not { } spec) return null;
        validate(spec);
        LayoutSpecChecks.EnsureMembersExist(type, members(spec));
        return spec;
    }

    static TSpec? FromInterface<TSpec>(Type type, string factory, Func<TSpec, string> typeName, Action<TSpec> validate) where TSpec : class {
        if (!typeof(ISupportViewLayoutCustomization).IsAssignableFrom(type)) return null;
        var map = type.GetInterfaceMap(typeof(ISupportViewLayoutCustomization));
        var i = Array.FindIndex(map.InterfaceMethods, m => m.Name == factory);
        // Unwrapped, so a factory's LayoutSpecException reaches every catch that expects one.
        var spec = (TSpec?)map.TargetMethods[i].Invoke(null, BindingFlags.DoNotWrapExceptions, null, null, null);
        // A derived class inherits the base class's implementation; only apply a spec to the type it was
        // built for. Hierarchy composition is phase 2 (start document section 2).
        if (spec is null || typeName(spec) != type.FullName) return null;
        // A factory can return a hand-built spec that no Build() has checked.
        validate(spec);
        return spec;
    }
}
