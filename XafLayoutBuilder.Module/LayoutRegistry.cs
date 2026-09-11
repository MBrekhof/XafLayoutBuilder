using System.Collections.Concurrent;
using System.Reflection;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>Explicit registration for types you don't own. Entries here win over <see cref="ISupportViewLayoutCustomization"/>.</summary>
public static class LayoutRegistry {
    internal static readonly ConcurrentDictionary<Type, (DetailLayoutSpec? Detail, ListColumnsSpec? Columns)> Entries = new();

    /// <summary>Throws <see cref="LayoutSpecException"/> when a spec names a member <typeparamref name="T"/> does not have,
    /// so a spec built for one type cannot be registered for an unrelated one by accident.</summary>
    public static void Register<T>(DetailLayoutSpec? detail, ListColumnsSpec? columns) {
        if (detail is not null) LayoutSpecChecks.EnsureMembersExist(typeof(T), detail.Members());
        if (columns is not null) LayoutSpecChecks.EnsureMembersExist(typeof(T), columns.Members());
        Entries[typeof(T)] = (detail, columns);
    }

    /// <summary>Test hook. Not needed by applications.</summary>
    public static void Clear() => Entries.Clear();
}

/// <summary>Registry first, then the static abstract members of <see cref="ISupportViewLayoutCustomization"/>. Cached per type.</summary>
internal static class LayoutSpecResolver {
    static readonly ConcurrentDictionary<Type, (DetailLayoutSpec? Detail, ListColumnsSpec? Columns)> cache = new();

    public static DetailLayoutSpec? Detail(Type type) => Resolve(type).Detail;
    public static ListColumnsSpec? Columns(Type type) => Resolve(type).Columns;

    static (DetailLayoutSpec? Detail, ListColumnsSpec? Columns) Resolve(Type type) {
        if (LayoutRegistry.Entries.TryGetValue(type, out var registered)) return registered;
        return cache.GetOrAdd(type, static t => {
            if (!typeof(ISupportViewLayoutCustomization).IsAssignableFrom(t)) return (null, null);
            var map = t.GetInterfaceMap(typeof(ISupportViewLayoutCustomization));
            var detail = (DetailLayoutSpec?)Invoke(map, nameof(ISupportViewLayoutCustomization.BuildDetailViewLayout));
            var columns = (ListColumnsSpec?)Invoke(map, nameof(ISupportViewLayoutCustomization.BuildListViewColumns));
            // A derived class inherits the base class's implementation; only apply a spec to the type it was
            // built for. Hierarchy composition is phase 2 (start document section 2).
            return (detail?.TypeName == t.FullName ? detail : null, columns?.TypeName == t.FullName ? columns : null);
        });
    }

    static object? Invoke(InterfaceMapping map, string name) {
        var i = Array.FindIndex(map.InterfaceMethods, m => m.Name == name);
        return map.TargetMethods[i].Invoke(null, null);
    }
}
