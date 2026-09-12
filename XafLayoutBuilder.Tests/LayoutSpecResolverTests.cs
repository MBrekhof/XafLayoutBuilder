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

    [Fact]
    public void FactoryThrowingLayoutSpecException_ReachesTheCallerUnwrapped() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(ThrowingLayout)));
        Assert.Equal("placed twice", ex.Message);
        Assert.Contains(nameof(ThrowingLayout.BuildDetailViewLayout), ex.StackTrace);
    }
}
