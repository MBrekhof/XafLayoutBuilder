using System.Collections.Concurrent;
using System.Reflection;
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

    /// <summary>Test hook. Not needed by applications.</summary>
    public static void Clear() {
        Entries.Clear();
        Interlocked.Increment(ref version);
    }
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
