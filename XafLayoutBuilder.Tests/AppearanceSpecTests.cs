using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

// APPEAR-001: appearance rules in the builder, the spec's own checks, the printer and JSON; no DevExpress involved.
public class AppearanceSpecTests {
    const string Order = "XafLayoutBuilder.Tests.TestOrder";

    internal static AppearanceSpec CardExample() =>
        AppearanceBuilder<TestOrder>.Create()
            .Rule("Old", r => r
                .When("[OrderDate] < AddDays(LocalDateTimeToday(), -30)")
                .On(x => x.OrderDate, x => x.Number)
                .FontColor("Red")
                .FontStyle(AppearanceFontStyle.Bold)
                .InListView())
            .Rule("HeaderTint", r => r
                .OnLayout("Header")
                .BackColor("#FFF3E0")
                .InDetailView())
            .Build();

    [Fact]
    public void Builder_RecordsEachRulesTargetsConditionAndAppearance() {
        var spec = CardExample();
        Assert.Equal(Order, spec.TypeName);
        var old = spec.Rules[0];
        Assert.Equal("Old", old.Id);
        Assert.Equal(AppearanceTargetKind.Items, old.TargetKind);
        Assert.Equal(["OrderDate", "Number"], old.Targets);
        Assert.Equal("[OrderDate] < AddDays(LocalDateTimeToday(), -30)", old.Criteria);
        Assert.Equal("ListView", old.Context);
        Assert.Equal("Red", old.FontColor);
        Assert.Equal(AppearanceFontStyle.Bold, old.FontStyle);
        var tint = spec.Rules[1];
        Assert.Equal(AppearanceTargetKind.Layout, tint.TargetKind);
        Assert.Equal(["Header"], tint.Targets);
        Assert.Equal("#FFF3E0", tint.BackColor);
        Assert.Equal("DetailView", tint.Context);
        Assert.Null(tint.Criteria);
    }

    [Fact]
    public void Builder_JoinsContexts_AndFollowsReferencesInMemberTargets() {
        var rule = AppearanceBuilder<TestOrder>.Create()
            .Rule("City", r => r.On(x => x.Customer!.City).Enabled(false).InListView().InView("Order_Compact_DetailView"))
            .Build().Rules[0];
        Assert.Equal("ListView;Order_Compact_DetailView", rule.Context);
        Assert.Equal(["Customer.City"], rule.Targets);
        Assert.False(rule.Enabled);
    }

    [Fact]
    public void Rule_TargetingMembersAndLayoutNodes_Throws() =>
        Assert.Contains("either members or layout nodes", Assert.Throws<LayoutSpecException>(() =>
            AppearanceBuilder<TestOrder>.Create().Rule("Both", r => r.On(x => x.Number).OnLayout("Header").FontColor("Red"))).Message);

    // The spec's own checks, for a spec from any source: the builder, a hand-built record, JSON.
    [Fact]
    public void RawSpec_Checks_Throw() {
        static AppearanceRuleSpec Rule(string id = "R", string[]? targets = null, string? fontColor = "Red", string? backColor = null) =>
            new(id, AppearanceTargetKind.Items, targets ?? ["Number"], FontColor: fontColor, BackColor: backColor);
        static string Fails(params AppearanceRuleSpec[] rules) =>
            Assert.Throws<LayoutSpecException>(() => LayoutSpecChecks.Validate(new AppearanceSpec(Order, rules))).Message;

        Assert.Contains("a rule has no id", Fails(Rule(id: " ")));
        Assert.Contains("rule id 'R' is used twice", Fails(Rule(), Rule()));
        Assert.Contains("rule 'R' has no target", Fails(Rule(targets: [])));
        Assert.Contains("rule 'R' has a blank target", Fails(Rule(targets: ["Number", " "])));
        Assert.Contains("rule 'R' sets no appearance", Fails(Rule(fontColor: null)));
        Assert.Contains("rule 'R': 'Reddish' is not a colour", Fails(Rule(fontColor: "Reddish")));
        LayoutSpecChecks.Validate(new AppearanceSpec(Order, [Rule(backColor: "#fff3e0")]));
    }

    [Fact]
    public void Members_AreTheMemberTargets_NotTheLayoutNodes() =>
        Assert.Equal(["OrderDate", "Number"], CardExample().Members());

    [Fact]
    public void Printer_PrintsTheBuilderBack() {
        const string expected = """
            AppearanceBuilder<TestOrder>.Create()
                .Rule("Old", r => r
                    .When("[OrderDate] < AddDays(LocalDateTimeToday(), -30)")
                    .On(x => x.OrderDate, x => x.Number)
                    .FontColor("Red")
                    .FontStyle(AppearanceFontStyle.Bold)
                    .InListView())
                .Rule("HeaderTint", r => r
                    .OnLayout("Header")
                    .BackColor("#FFF3E0")
                    .InDetailView())
                .Build()
            """;
        Assert.Equal(expected.ReplaceLineEndings(), CSharpLayoutPrinter.PrintAppearance(CardExample(), "TestOrder").ReplaceLineEndings());
    }

    // Rules read from the model can hold every value, and a context the builder's shortcuts do not name.
    [Fact]
    public void Printer_PrintsEveryAppearanceValue_AndCombinedFontStyles() {
        var spec = new AppearanceSpec(Order, [new AppearanceRuleSpec("All", AppearanceTargetKind.Items, ["Customer.City"],
            Context: "Any;Order_ListView", FontStyle: AppearanceFontStyle.Bold | AppearanceFontStyle.Italic, Enabled: false,
            Visibility: AppearanceVisibility.ShowEmptySpace, Priority: 2)]);
        var code = CSharpLayoutPrinter.PrintAppearance(spec, "TestOrder");
        Assert.Contains(".On(x => x.Customer.City)", code);
        Assert.Contains(".FontStyle(AppearanceFontStyle.Bold | AppearanceFontStyle.Italic)", code);
        Assert.Contains(".Enabled(false)", code);
        Assert.Contains(".Visibility(AppearanceVisibility.ShowEmptySpace)", code);
        Assert.Contains(".Priority(2)", code);
        Assert.Contains(".InView(\"Any\")", code);
        Assert.Contains(".InView(\"Order_ListView\")", code);
    }

    [Fact]
    public void PrintClass_AddsTheAppearanceInterfaceAndMethod_OnlyWithRules() {
        var with = CSharpLayoutPrinter.PrintClass("Sample", "TestOrder", null, null, appearance: CardExample());
        Assert.Contains("public partial class TestOrder : ISupportViewLayoutCustomization, ISupportAppearanceRules {", with);
        Assert.Contains("    public static AppearanceSpec? BuildAppearanceRules() =>", with);
        Assert.Contains("        AppearanceBuilder<TestOrder>.Create()", with);
        var without = CSharpLayoutPrinter.PrintClass("Sample", "TestOrder", null, null);
        Assert.Contains("public partial class TestOrder : ISupportViewLayoutCustomization {", without);
        Assert.DoesNotContain("Appearance", without);
    }

    [Fact]
    public void Json_RoundTripsTheAppearanceHalf_AndOmitsItWhenMissing() {
        var json = LayoutSpecJson.Serialize(new LayoutSpecs(null, null, CardExample()));
        Assert.Equal(json, LayoutSpecJson.Serialize(LayoutSpecJson.Deserialize<LayoutSpecs>(json)));
        Assert.Contains("\"fontStyle\": \"Bold\"", json);
        Assert.Contains("\"targetKind\": \"Layout\"", json);
        Assert.DoesNotContain("appearance", LayoutSpecJson.Serialize(new LayoutSpecs(null, ListViewColumnsBuilderTests.Section4Columns())));
    }
}
