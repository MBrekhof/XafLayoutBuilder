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

    [Fact]
    public void FactoryThrowingLayoutSpecException_ReachesTheCallerUnwrapped() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(ThrowingLayout)));
        Assert.Equal("placed twice", ex.Message);
        Assert.Contains(nameof(ThrowingLayout.BuildDetailViewLayout), ex.StackTrace);
    }
}
