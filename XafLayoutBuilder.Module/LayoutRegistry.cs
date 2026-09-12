using System.Collections.Concurrent;
using System.Reflection;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>Explicit registration for types you don't own. Entries here win over <see cref="ISupportViewLayoutCustomization"/>.</summary>
public static class LayoutRegistry {
    internal static readonly ConcurrentDictionary<Type, (DetailLayoutSpec? Detail, ListColumnsSpec? Columns)> Entries = new();
    static int version;

    /// <summary>
    /// Bumped by every registration change. <see cref="LayoutStartupCheck"/> keys its "already validated" memory on
    /// it, so a spec registered after an application has started is still checked for the next one.
    /// </summary>
    internal static int Version => Volatile.Read(ref version);

    /// <summary>Throws <see cref="LayoutSpecException"/> when a spec breaks the structural rules
    /// (<see cref="LayoutSpecChecks.Validate(DetailLayoutSpec)"/>; a hand-built spec never went through Build()) or names a
    /// member <typeparamref name="T"/> does not have, so a spec built for one type cannot be registered for an unrelated
    /// one by accident.</summary>
    public static void Register<T>(DetailLayoutSpec? detail, ListColumnsSpec? columns) {
        if (detail is not null) {
            LayoutSpecChecks.Validate(detail);
            LayoutSpecChecks.EnsureMembersExist(typeof(T), detail.Members());
        }
        if (columns is not null) {
            LayoutSpecChecks.Validate(columns);
            LayoutSpecChecks.EnsureMembersExist(typeof(T), columns.Members());
        }
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
            ? registered.Detail
            : details.GetOrAdd(type, static t => FromInterface<DetailLayoutSpec>(
                t, nameof(ISupportViewLayoutCustomization.BuildDetailViewLayout), s => s.TypeName, LayoutSpecChecks.Validate));

    public static ListColumnsSpec? Columns(Type type) =>
        LayoutRegistry.Entries.TryGetValue(type, out var registered)
            ? registered.Columns
            : columnSpecs.GetOrAdd(type, static t => FromInterface<ListColumnsSpec>(
                t, nameof(ISupportViewLayoutCustomization.BuildListViewColumns), s => s.TypeName, LayoutSpecChecks.Validate));

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
