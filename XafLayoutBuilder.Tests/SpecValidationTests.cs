using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Tests;

// A spec does not have to come from a builder: records can be constructed, reshaped with `with`, or loaded from
// JSON. The structural rules Build() enforces must hold for those too, before an updater touches the model.
public class SpecValidationTests {
    static readonly string Order = typeof(TestOrder).FullName!;

    static DetailLayoutSpec Detail(IReadOnlyList<LayoutNodeSpec> nodes, IReadOnlyList<string>? hidden = null, string? unplaced = null) =>
        new(Order, nodes, hidden ?? [], unplaced);

    static LayoutGroupSpec Group(string id, params LayoutNodeSpec[] children) => new(id, children);
    static LayoutItemSpec Item(string member) => new(member);

    [Fact]
    public void RawSpec_MemberPlacedAndHidden_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(Detail([Group("A", Item("SyncToken"))], hidden: ["SyncToken"])));
        Assert.Contains("'SyncToken' is both placed and hidden", ex.Message);
    }

    [Fact]
    public void RawSpec_MemberPlacedTwice_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(Detail([Group("A", Item("Number")), Group("B", Item("Number"))])));
        Assert.Contains("'Number' is placed twice", ex.Message);
    }

    [Fact]
    public void RawSpec_GroupIdUsedTwice_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(Detail([Group("A", Item("Number")), new TabbedGroupSpec("T", [Group("A", Item("Notes"))])])));
        Assert.Contains("group id 'A' is used twice", ex.Message);
    }

    [Fact]
    public void RawSpec_GroupAndItemSharingAnIdAsSiblings_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(Detail([Group("Notes", Item("Number")), Item("Notes")])));
        Assert.Contains("'Notes' names both a group and an item under the same parent", ex.Message);
    }

    [Theory]
    [InlineData("Other")]   // a root group
    [InlineData("Number")]  // a root item
    public void RawSpec_CatchAllCollidingWithARootNode_Throws(string catchAll) {
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(Detail([Group("Other", Item("Notes")), Item("Number")], unplaced: catchAll)));
        Assert.Contains($"'{catchAll}' is already used in this layout", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RawSpec_BlankId_Throws(string id) {
        Assert.Throws<LayoutSpecException>(() => LayoutSpecChecks.Validate(Detail([Group(id, Item("Number"))])));
        Assert.Throws<LayoutSpecException>(() => LayoutSpecChecks.Validate(Detail([Group("G", Item(id))])));
        Assert.Throws<LayoutSpecException>(() => LayoutSpecChecks.Validate(Detail([], unplaced: id)));
    }

    [Fact]
    public void RawColumns_ListedTwice_ListedAndHidden_NestedLookup_Throw() {
        Assert.Contains("'Number' is listed twice", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [new ColumnSpec("Number"), new ColumnSpec("Number")], []))).Message);
        Assert.Contains("'Number' is both listed and hidden", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [new ColumnSpec("Number")], ["Number"]))).Message);
        Assert.Contains("cannot be nested", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [], [],
                new ListColumnsSpec(Order, [], [], new ListColumnsSpec(Order, [], []))))).Message);
        Assert.Contains("'Customer' is listed twice", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [], [],
                new ListColumnsSpec(Order, [new ColumnSpec("Customer"), new ColumnSpec("Customer")], [])))).Message);
    }

    // BAND-001: the band rules, for a spec from any source.
    [Fact]
    public void RawColumns_BandRules_Throw() {
        static ListColumnsSpec Spec(ColumnSpec[] columns, BandSpec[] bands) => new(Order, columns, [], Bands: bands);
        string Error(ListColumnsSpec spec) => Assert.Throws<LayoutSpecException>(() => LayoutSpecChecks.Validate(spec)).Message;

        Assert.Contains("a band has no id", Error(Spec([new ColumnSpec("Number", Band: " ")], [new BandSpec(" ")])));
        Assert.Contains("band id 'A' is used twice", Error(Spec([new ColumnSpec("Number", Band: "A")], [new BandSpec("A"), new BandSpec("A")])));
        Assert.Contains("names band 'X', which is not declared", Error(Spec([new ColumnSpec("Number", Band: "X")], [])));
        Assert.Contains("band 'A' has no columns", Error(Spec([new ColumnSpec("Number")], [new BandSpec("A")])));
        Assert.Contains("the columns of band 'A' are not next to each other", Error(Spec(
            [new ColumnSpec("Number", Band: "A"), new ColumnSpec("Customer"), new ColumnSpec("OrderDate", Band: "A")], [new BandSpec("A")])));
        Assert.Contains("bands are for the ListView", Error(new ListColumnsSpec(Order, [], [],
            new ListColumnsSpec(Order, [new ColumnSpec("Number", Band: "A")], [], Bands: [new BandSpec("A")]))));
    }

    // NEST-001: nested paths are for columns. A detail item's id is its member name, and the builder keeps it simple.
    [Fact]
    public void RawDetailSpec_WithANestedMember_Throws() {
        Assert.Contains("'Customer.Name' is a nested path", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(Detail([Group("A", Item("Customer.Name"))]))).Message);
        Assert.Contains("'Customer.Name' is a nested path", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(Detail([Group("A", Item("Number"))], hidden: ["Customer.Name"]))).Message);
    }

    [Theory]
    [InlineData("Customer..Name")]
    [InlineData(".Name")]
    [InlineData("Customer.")]
    public void RawColumns_EmptyPathSegment_Throws(string member) {
        Assert.Contains("empty segment", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [new ColumnSpec(member)], []))).Message);
        Assert.Contains("empty segment", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [], [member]))).Message);
    }

    sealed class NestedRegistered {
        public Reference? Ref { get; set; }

        public sealed class Reference {
            public string? City { get; set; }
        }
    }

    [Fact]
    public void EnsureMembersExist_FollowsNestedPaths() {
        LayoutSpecChecks.EnsureMembersExist(typeof(NestedRegistered), ["Ref", "Ref.City"]);
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.EnsureMembersExist(typeof(NestedRegistered), ["Ref.Nope", "Nope.City"]));
        Assert.Contains("Ref.Nope, Nope.City", ex.Message);
    }

    // SORT-001: an explicit sort priority belongs to a sorted column, is unique and not negative in its list, and is set on
    // every sorted column of that list or on none.
    [Fact]
    public void RawColumns_SortIndexRules_Throw() {
        Assert.Contains("'Number' has a sort index but no sort order", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [new ColumnSpec("Number", SortIndex: 0)], []))).Message);
        Assert.Contains("sort index 0 is used twice", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [
                new ColumnSpec("Number", SortOrder: ColumnSortOrder.Ascending, SortIndex: 0),
                new ColumnSpec("Customer", SortOrder: ColumnSortOrder.Ascending, SortIndex: 0)], []))).Message);
        Assert.Contains("on every sorted column or on none", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [
                new ColumnSpec("Number", SortOrder: ColumnSortOrder.Ascending, SortIndex: 0),
                new ColumnSpec("Customer", SortOrder: ColumnSortOrder.Ascending)], []))).Message);
        Assert.Contains("negative sort index", Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.Validate(new ListColumnsSpec(Order, [new ColumnSpec("Number", SortOrder: ColumnSortOrder.Ascending, SortIndex: -1)], []))).Message);
    }

    [Fact]
    public void BuilderOutput_AndTheSection4Example_AreValid() {
        LayoutSpecChecks.Validate(LayoutBuilderTests.Section4Detail());
        LayoutSpecChecks.Validate(ListViewColumnsBuilderTests.Section4Columns());
    }

    // Registration defers every check to resolution, which runs inside the updaters and the startup check, where
    // FailFastOnLayoutErrors decides whether the host stops (REG-001).
    sealed class RegisteredOnly {
        public string? Name { get; set; }
    }

    [Fact]
    public void Register_AcceptsAStructurallyBrokenRawSpec_AndResolutionRejectsIt() {
        LayoutRegistry.Register<RegisteredOnly>(
            new DetailLayoutSpec(typeof(RegisteredOnly).FullName!, [Group("A", Item("Name"))], ["Name"]), columns: null);
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(RegisteredOnly)));
        Assert.Contains("'Name' is both placed and hidden", ex.Message);
    }

    sealed class LazyRegistered {
        public string? Name { get; set; }
    }

    [Fact]
    public void RegisteredFactories_RunOnlyWhenResolved_AndAMissingMemberFailsThere() {
        var calls = 0;
        LayoutRegistry.Register<LazyRegistered>(
            detail: () => { calls++; return new DetailLayoutSpec(typeof(LazyRegistered).FullName!, [Item("Nope")], []); },
            columns: null);
        Assert.Equal(0, calls);
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(LazyRegistered)));
        Assert.Equal(1, calls);
        Assert.Contains("has no member(s) Nope", ex.Message);
    }

    sealed class RawBrokenLayout : ISupportViewLayoutCustomization {
        public static DetailLayoutSpec? BuildDetailViewLayout() =>
            new(typeof(RawBrokenLayout).FullName!, [Group("Main", Item("X")), Group("Main", Item("Y"))], []);
        public static ListColumnsSpec? BuildListViewColumns() => null;
    }

    [Fact]
    public void Resolver_RejectsAStructurallyBrokenRawSpecFromTheInterface() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecResolver.Detail(typeof(RawBrokenLayout)));
        Assert.Contains("group id 'Main' is used twice", ex.Message);
    }
}
