using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Tests;

// The resolver calls the static spec factories through reflection. The Module's diagnostics are all
// LayoutSpecException, so a factory's exception must reach callers as itself, not wrapped.
public class LayoutSpecResolverTests {
    sealed class ThrowingLayout : ISupportViewLayoutCustomization {
        public static DetailLayoutSpec? BuildDetailViewLayout() => throw new LayoutSpecException("placed twice");
        public static ListColumnsSpec? BuildListViewColumns() => null;
    }

    // The two factories of one type are independent: a broken DetailView spec must not cost the ListView its columns,
    // and in degraded mode must not repeat its failure for every columns lookup (RESOLVE-002).
    sealed class BrokenDetailValidColumns : ISupportViewLayoutCustomization {
        public static DetailLayoutSpec? BuildDetailViewLayout() => throw new InvalidOperationException("detail factory broke");
        public static ListColumnsSpec? BuildListViewColumns() =>
            new(typeof(BrokenDetailValidColumns).FullName!, [new ColumnSpec("Name")], []);
    }

    sealed class ValidDetailBrokenColumns : ISupportViewLayoutCustomization {
        public static DetailLayoutSpec? BuildDetailViewLayout() =>
            new(typeof(ValidDetailBrokenColumns).FullName!, [new LayoutItemSpec("Name")], []);
        public static ListColumnsSpec? BuildListViewColumns() => throw new InvalidOperationException("columns factory broke");
    }

    [Fact]
    public void ABrokenDetailFactory_DoesNotTakeTheColumnsDown() {
        Assert.Throws<InvalidOperationException>(() => LayoutSpecResolver.Detail(typeof(BrokenDetailValidColumns)));
        Assert.Equal("Name", Assert.Single(LayoutSpecResolver.Columns(typeof(BrokenDetailValidColumns))!.Columns).Member);
    }

    [Fact]
    public void ABrokenColumnsFactory_DoesNotTakeTheDetailDown() {
        Assert.Throws<InvalidOperationException>(() => LayoutSpecResolver.Columns(typeof(ValidDetailBrokenColumns)));
        Assert.Equal("Name", Assert.IsType<LayoutItemSpec>(Assert.Single(LayoutSpecResolver.Detail(typeof(ValidDetailBrokenColumns))!.Nodes)).Member);
    }

    // RegisterJson reads its JSON inside the registry factories, so bad JSON is a layout error at resolution, governed
    // by FailFastOnLayoutErrors like any other (JSON-001).
    sealed class JsonRegistered {
        public string? Name { get; set; }
        public string? City { get; set; }
    }

    [Fact]
    public void RegisterJson_ResolvesBothHalves_AndReadsTheJsonOnlyWhenResolved() {
        var reads = 0;
        var type = typeof(JsonRegistered).FullName!;
        var json = LayoutSpecJson.Serialize(new LayoutSpecs(
            new DetailLayoutSpec(type, [new LayoutItemSpec("Name")], ["City"]),
            new ListColumnsSpec(type, [new ColumnSpec("Name")], [])));

        LayoutRegistry.RegisterJson<JsonRegistered>(() => { reads++; return json; });

        Assert.Equal(0, reads);
        Assert.Equal("Name", Assert.IsType<LayoutItemSpec>(Assert.Single(LayoutSpecResolver.Detail(typeof(JsonRegistered))!.Nodes)).Member);
        Assert.Equal("Name", Assert.Single(LayoutSpecResolver.Columns(typeof(JsonRegistered))!.Columns).Member);
        Assert.Equal(2, reads);
    }

    sealed class MalformedJsonRegistered {
        public string? Name { get; set; }
    }

    sealed class ForeignJsonRegistered {
        public string? Name { get; set; }
    }

    [Fact]
    public void RegisterJson_MalformedJson_OrAnotherTypesSpec_FailsAtResolution() {
        LayoutRegistry.RegisterJson<MalformedJsonRegistered>(() => "{ not json");
        Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(MalformedJsonRegistered)));

        var other = typeof(JsonRegistered).FullName!;
        LayoutRegistry.RegisterJson<ForeignJsonRegistered>(() =>
            LayoutSpecJson.Serialize(new LayoutSpecs(new DetailLayoutSpec(other, [new LayoutItemSpec("Name")], []), null)));
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(ForeignJsonRegistered)));
        Assert.Contains(other, ex.Message);
        Assert.Null(LayoutSpecResolver.Columns(typeof(ForeignJsonRegistered)));
    }

    sealed class HalfBrokenJsonRegistered {
        public string? Name { get; set; }
    }

    // Codex review of JSON-001: each factory must read only its own half, or a detail node without its $type costs the
    // columns their spec too, which the resolver's per-half isolation promises not to do.
    [Fact]
    public void RegisterJson_ABrokenDetailHalf_DoesNotTakeTheColumnsDown() {
        var type = typeof(HalfBrokenJsonRegistered).FullName!;
        var json = $$"""
            {
              "detail": { "typeName": "{{type}}", "nodes": [ { "member": "Name" } ], "hiddenMembers": [] },
              "columns": { "typeName": "{{type}}", "columns": [ { "member": "Name" } ], "hiddenMembers": [] }
            }
            """;
        LayoutRegistry.RegisterJson<HalfBrokenJsonRegistered>(() => json);

        Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(HalfBrokenJsonRegistered)));
        Assert.Equal("Name", Assert.Single(LayoutSpecResolver.Columns(typeof(HalfBrokenJsonRegistered))!.Columns).Member);
    }

    [Fact]
    public void FactoryThrowingLayoutSpecException_ReachesTheCallerUnwrapped() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(ThrowingLayout)));
        Assert.Equal("placed twice", ex.Message);
        Assert.Contains(nameof(ThrowingLayout.BuildDetailViewLayout), ex.StackTrace);
    }
}
