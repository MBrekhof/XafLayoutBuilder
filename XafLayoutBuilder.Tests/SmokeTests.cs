namespace XafLayoutBuilder.Tests;

// Session 1 placeholder so `dotnet test` has one green test. Replaced by builder tests in session 2.
public class SmokeTests {
    [Fact]
    public void CoreAssemblyLoads() => Assert.NotNull(typeof(SmokeTests).Assembly);
}
