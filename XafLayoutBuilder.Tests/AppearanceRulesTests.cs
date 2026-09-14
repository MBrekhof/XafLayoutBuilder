using System.Drawing;
using DevExpress.Drawing;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.Appearance;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

// APPEAR-001: the add-on's resolver, updater and startup check against the in-process Application Model.
[Collection(ApplicationModelCollection.Name)]
public class AppearanceRulesTests(ApplicationModelFixture fixture) {
    // A model node's id lives on ModelNode, not on the node interfaces.
    static string Id(object node) => ((ModelNode)node).Id;

    [Fact]
    public void BuilderRules_AreAddedNextToTheAttributeRule_InTheGeneratedLayer() {
        var rules = fixture.Rules<ModelTestStyled>();
        Assert.Equal(["FromAttribute", "HeaderHidden", "Late"], rules.Select(Id).Order());
        Assert.NotNull(rules["FromAttribute"]!.Attribute);
        Assert.All(rules.Where(r => Id(r) != "FromAttribute"), r => {
            Assert.Null(r.Attribute);
            Assert.False(((ModelNode)r).HasModification, $"{Id(r)} is in the writable layer");
        });
    }

    [Fact]
    public void ItemRule_CarriesItsTargetsCriteriaContextAndAppearance() {
        var late = fixture.Rules<ModelTestStyled>()["Late"]!;
        Assert.Equal("ViewItem", late.AppearanceItemType);
        Assert.Equal("Due, Code", late.TargetItems);
        Assert.Equal("[Due] < LocalDateTimeToday()", late.Criteria);
        Assert.Equal("ListView", late.Context);
        Assert.Equal(Color.Red.ToArgb(), late.FontColor?.ToArgb());
        Assert.Equal(Color.FromArgb(0xFF, 0xF3, 0xE0).ToArgb(), late.BackColor?.ToArgb());
        Assert.Equal(DXFontStyle.Bold | DXFontStyle.Italic, late.FontStyle);
        Assert.Equal(2, late.Priority);
        Assert.Null(late.Enabled);
        Assert.Null(late.Visibility);
    }

    [Fact]
    public void LayoutRule_TargetsLayoutItems_WithVisibilityEnabledAndTheJoinedContext() {
        var hidden = fixture.Rules<ModelTestStyled>()["HeaderHidden"]!;
        Assert.Equal("LayoutItem", hidden.AppearanceItemType);
        Assert.Equal("Header", hidden.TargetItems);
        Assert.Equal("DetailView;ModelTestStyled_ListView", hidden.Context);
        Assert.Equal(ViewItemVisibility.Hide, hidden.Visibility);
        Assert.False(hidden.Enabled);
    }

    // Both clash types are read by the fixture before any test runs (see ApplicationModelFixture).
    [Fact]
    public void IdTakenByAnAttributeRule_WithFailFastOn_IsXLB006() {
        var ex = Assert.IsType<LayoutSpecException>(fixture.StrictAppearanceClashError);
        Assert.Contains("XLB006", ex.Message);
        Assert.Contains("'Taken'", ex.Message);
    }

    // Check before mutate: with fail-fast off the clash is logged, and not even the class's free rule is added.
    [Fact]
    public void IdTakenByAnAttributeRule_WithFailFastOff_AddsNoBuilderRule() =>
        Assert.Equal(["Taken"], fixture.DegradedAppearanceClashRuleIds);

    [Fact]
    public void StartupCheck_ReportsTheClashes_AnUnknownLayoutTarget_AndCriteriaThatDoNotParse() {
        var ex = Assert.IsType<LayoutSpecException>(Record.Exception(() => AppearanceStartupCheck.Check(fixture.Model)));
        Assert.Contains("XLB006 ModelTestAppearanceClashStrict", ex.Message);
        Assert.Contains("XLB006 ModelTestAppearanceClashDegraded", ex.Message);
        Assert.Contains("XLB007 ModelTestAppearanceBroken: rule 'NoSuchGroup'", ex.Message);
        Assert.Contains("XLB008 ModelTestAppearanceBroken: rule 'BadCriteria'", ex.Message);
        Assert.DoesNotContain("ModelTestStyled", ex.Message);
    }

    // Gate, --break-layout: the appearance check read Order's broken layout and reported its layout error as its own, which ended
    // the startup before LayoutStartupCheck reported XLB001. A layout that cannot be read, or that failed and shows XAF's own
    // layout, is the layout check's to report, and its layout targets cannot be judged.
    [Fact]
    public void FirstAppearanceCheck_LeavesABrokenLayoutToTheLayoutCheck() {
        var ex = Assert.IsType<LayoutSpecException>(fixture.FirstAppearanceCheckError);
        Assert.Contains("XLB006", ex.Message);
        Assert.DoesNotContain("ModelTestAppearanceOnBrokenLayout", ex.Message);
    }

    [Fact]
    public void RepeatedAppearanceCheck_DoesNotJudgeLayoutTargetsOnAFailedLayout() {
        var ex = Assert.IsType<LayoutSpecException>(Record.Exception(() => AppearanceStartupCheck.Check(fixture.Model)));
        Assert.DoesNotContain("ModelTestAppearanceOnBrokenLayout", ex.Message);
        var layoutError = Assert.IsType<LayoutSpecException>(Record.Exception(() => XafLayoutBuilder.Module.LayoutStartupCheck.Check(fixture.Model)));
        Assert.Contains("XLB001 ModelTestAppearanceOnBrokenLayout_DetailView", layoutError.Message);
    }

    // Codex review: with fail-fast off, criteria that do not parse must not reach the model, where XAF parses them again when the
    // view renders and throws. Checked before the updater adds anything, like XLB006.
    [Fact]
    public void UnparsableCriteria_WithFailFastOff_AddsNoBuilderRule() =>
        Assert.Empty(fixture.DegradedBadCriteriaRuleIds);

    // Codex review: an administrator's later layer may remove the Header group the builder layout made. That is a customisation,
    // not a builder error, so a layout target is judged against the class's builder layout, not the merged model.
    [Fact]
    public void LayoutTarget_IsJudgedAgainstTheBuilderLayout_NotALaterLayersRemoval() {
        var view = fixture.Class<ModelTestAppearanceOverridden>().DefaultDetailView;
        var main = Assert.IsAssignableFrom<DevExpress.ExpressApp.Model.IModelLayoutGroup>(Assert.Single(view.Layout));
        main["Header"]!.Remove();

        var ex = Record.Exception(() => AppearanceStartupCheck.Check(fixture.Model));
        Assert.DoesNotContain("ModelTestAppearanceOverridden", ex?.Message ?? "");
    }

    // Codex re-review: the startup check reads LayoutRegistry for the builder's layout ids, so a layout registered after an
    // application passed the check must make the next application check again.
    [Fact]
    public void StartupCheckMemory_IsKeyedOnTheLayoutRegistryToo() {
        var before = AppearanceStartupCheck.Key(typeof(object));
        try {
            XafLayoutBuilder.Module.LayoutRegistry.Register<TestOrder>((DetailLayoutSpec?)null, (ListColumnsSpec?)null);
            Assert.NotEqual(before, AppearanceStartupCheck.Key(typeof(object)));
        }
        finally {
            XafLayoutBuilder.Module.LayoutRegistry.Entries.TryRemove(typeof(TestOrder), out _);
        }
    }

    // Codex review 3: the export hook is process-wide, so the model of an application without Conditional Appearance can reach it.
    // A proxy stands in for such a class node: it is an IModelClass and nothing else, and any member read would throw.
    [Fact]
    public void Exporter_ReturnsNothing_ForAModelWithoutConditionalAppearance() {
        var withoutAppearance = System.Reflection.DispatchProxy.Create<IModelClass, ModelClassWithoutAppearance>();
        var (spec, notes) = AppearanceExporter.Export(withoutAppearance);
        Assert.Null(spec);
        Assert.Empty(notes);
    }

    public class ModelClassWithoutAppearance : System.Reflection.DispatchProxy {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} was read");
    }

    // A derived class inherits the static implementation; a spec only applies to the type it names.
    [Fact]
    public void Resolver_ReadsTheInterface_ForTheTypeItWasBuiltFor() {
        Assert.Equal(["Late", "HeaderHidden"], AppearanceSpecResolver.For(typeof(ModelTestStyled))!.Rules.Select(r => r.Id));
        Assert.Null(AppearanceSpecResolver.For(typeof(ModelTestCustomer)));
    }

    // TestOrder is in no model, so registering for it cannot change a rule the other tests read.
    [Fact]
    public void Registry_ChecksTheNamedMembers_AndReadsTheJsonAppearanceHalf() {
        try {
            AppearanceRegistry.Register<TestOrder>(() =>
                new AppearanceSpec(typeof(TestOrder).FullName!, [new AppearanceRuleSpec("Ghost", AppearanceTargetKind.Items, ["Nope"], FontColor: "Red")]));
            Assert.Contains("Nope", Assert.Throws<LayoutSpecException>(() => AppearanceSpecResolver.For(typeof(TestOrder))).Message);

            AppearanceRegistry.RegisterJson<TestOrder>(() => LayoutSpecJson.Serialize(new LayoutSpecs(null, null, AppearanceSpecTests.CardExample())));
            Assert.Equal(["Old", "HeaderTint"], AppearanceSpecResolver.For(typeof(TestOrder))!.Rules.Select(r => r.Id));
        }
        finally {
            AppearanceRegistry.Clear();
        }
    }

    // Export: the builder rules come back as the spec that made them; the [Appearance] rule stays an attribute, named in a note.
    [Fact]
    public void Exporter_ReturnsTheBuilderRules_AndNamesTheAttributeRule() {
        var (spec, notes) = AppearanceExporter.Export(fixture.Class<ModelTestStyled>());
        Assert.Equal(CSharpLayoutPrinter.PrintAppearance(ModelTestStyled.BuildAppearanceRules()!, nameof(ModelTestStyled)),
            CSharpLayoutPrinter.PrintAppearance(spec!, nameof(ModelTestStyled)));
        Assert.Contains(notes, n => n.Contains("'FromAttribute'") && n.Contains("[Appearance]"));
    }

    // Rules added in the model: a plain one is exported, with XAF's default context "Any" left out; an Action rule, a method rule
    // and an all-except target have no builder form and are named instead.
    [Fact]
    public void Exporter_ExportsRulesAddedInTheModel_AndNamesWhatTheBuilderCannotExpress() {
        var rules = fixture.Rules<ModelTestAppearanceExport>();
        Add("Plain", r => { r.TargetItems = "Name"; r.FontColor = Color.Green; r.Context = "Any"; });
        Add("ForAction", r => { r.TargetItems = "Save"; r.AppearanceItemType = "Action"; r.Enabled = false; });
        Add("ByMethod", r => { r.TargetItems = "Name"; r.Method = "IsLate"; r.FontColor = Color.Red; });
        Add("AllBut", r => { r.TargetItems = "*, Name"; r.BackColor = Color.Yellow; });
        // Codex review: a view item that is no member (a static text), which a member lambda cannot name.
        Add("OnStaticText", r => { r.TargetItems = "HelpText"; r.FontColor = Color.Blue; });
        // Codex re-review: a rule an administrator cleared of every value, which XAF ignores and Build() would reject.
        Add("Inert", r => r.TargetItems = "Name");
        // Codex review 3: targets cleared in the model (the Model Editor allows an empty string) while a colour is kept.
        Add("NoTargets", r => { r.TargetItems = ""; r.FontColor = Color.Red; });

        var (spec, notes) = AppearanceExporter.Export(fixture.Class<ModelTestAppearanceExport>());

        LayoutSpecChecks.Validate(spec!);
        Assert.Contains(notes, n => n.Contains("'Inert'"));
        Assert.Contains(notes, n => n.Contains("'NoTargets'"));
        var plain = Assert.Single(spec!.Rules);
        Assert.Equal("Plain", plain.Id);
        Assert.Equal(["Name"], plain.Targets);
        Assert.Equal("Green", plain.FontColor);
        Assert.Null(plain.Context);
        Assert.Contains(notes, n => n.Contains("'ForAction'"));
        Assert.Contains(notes, n => n.Contains("'ByMethod'"));
        Assert.Contains(notes, n => n.Contains("'AllBut'"));
        Assert.Contains(notes, n => n.Contains("'OnStaticText'") && n.Contains("HelpText"));

        void Add(string id, Action<DevExpress.ExpressApp.ConditionalAppearance.IModelAppearanceRule> set) =>
            set(rules.AddNode<DevExpress.ExpressApp.ConditionalAppearance.IModelAppearanceRule>(id));
    }
}
