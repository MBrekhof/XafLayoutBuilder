using System.Collections.Concurrent;
using System.Reflection;
using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Appearance;

/// <summary>APPEAR-001: explicit registration of appearance rules for types you don't own. Entries win over <see cref="ISupportAppearanceRules"/>.</summary>
public static class AppearanceRegistry {
    internal static readonly ConcurrentDictionary<Type, Func<AppearanceSpec?>> Entries = new();
    static int version;

    /// <summary>Bumped by every registration change; <see cref="AppearanceStartupCheck"/> keys its "already validated" memory on it.</summary>
    internal static int Version => Volatile.Read(ref version);

    /// <summary>
    /// Registers a factory for <typeparamref name="T"/>'s rules. Nothing is checked here: the spec's rules and whether every
    /// targeted member exists are checked when the class's rules are generated, inside the updater and the startup check, so
    /// <see cref="XafLayoutBuilderModule.FailFastOnLayoutErrors"/> governs a broken registration like any layout error.
    /// </summary>
    public static void Register<T>(Func<AppearanceSpec?> rules) {
        Entries[typeof(T)] = rules;
        Interlocked.Increment(ref version);
    }

    /// <summary>The appearance half of a <see cref="LayoutSpecs"/> JSON document, read when the rules are generated.</summary>
    public static void RegisterJson<T>(Func<string> json) => Register<T>(() => {
        var spec = LayoutRegistry.ReadHalf<T, AppearanceSpec>(json(), "appearance");
        return spec is null || spec.TypeName == typeof(T).FullName
            ? spec
            : throw new LayoutSpecException($"Layout JSON registered for {typeof(T).FullName} describes {spec.TypeName}.");
    });

    /// <summary>Test hook. Not needed by applications.</summary>
    public static void Clear() {
        Entries.Clear();
        Interlocked.Increment(ref version);
    }
}

/// <summary>
/// Registry first, then the static member of <see cref="ISupportAppearanceRules"/>. Both are checked here: the spec's own rules
/// and that every targeted member exists on the type. A spec from the interface is cached per type; a factory that throws is not.
/// </summary>
internal static class AppearanceSpecResolver {
    static readonly ConcurrentDictionary<Type, AppearanceSpec?> fromInterface = new();

    public static AppearanceSpec? For(Type type) =>
        AppearanceRegistry.Entries.TryGetValue(type, out var factory)
            ? Checked(type, factory())
            : fromInterface.GetOrAdd(type, static t => {
                if (!typeof(ISupportAppearanceRules).IsAssignableFrom(t)) return null;
                var map = t.GetInterfaceMap(typeof(ISupportAppearanceRules));
                // Unwrapped, so a factory's LayoutSpecException reaches every catch that expects one.
                var spec = (AppearanceSpec?)map.TargetMethods[0].Invoke(null, BindingFlags.DoNotWrapExceptions, null, null, null);
                // A derived class inherits the base class's implementation; a spec applies only to the type it was built for.
                // XAF applies the base class's rules to the derived class anyway (AppearanceController.GetRulesFromModel).
                return spec is null || spec.TypeName != t.FullName ? null : Checked(t, spec);
            });

    static AppearanceSpec? Checked(Type type, AppearanceSpec? spec) {
        if (spec is null) return null;
        LayoutSpecChecks.Validate(spec);
        LayoutSpecChecks.EnsureMembersExist(type, spec.Members());
        return spec;
    }
}
