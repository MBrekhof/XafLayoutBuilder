using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-004: node operations as the WinForms Model Editor offers them (docs/model-editor-scope.md, "Node
// operations"), against the in-process model. Every node added here is removed again in a finally block.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorNodeTests(ApplicationModelFixture fixture) {
    IModelListView OrderListView => fixture.Class<ModelTestOrder>().DefaultListView;

    // FastModelEditorHelper.GetChildNodeTypes, filtered as LinksNodeHelper.FilterCreatableItems does.
    [Fact]
    public void CreatableTypes_OfColumns_OfferAColumn() =>
        Assert.Contains(ModelEditing.CreatableTypes(OrderListView.Columns).Values, t => typeof(IModelColumn).IsAssignableFrom(t));

    // A new node is saved only with its required values: a column needs PropertyName ([Required], CommonInterfaces.cs 673-675).
    [Fact]
    public void AddChild_CreatesTheNode_AndNamesItsMissingRequiredValues() {
        var column = ModelEditing.AddChild(OrderListView.Columns, typeof(IModelColumn), "EditorAdded");
        try {
            Assert.Contains(ModelEditing.Children(OrderListView.Columns), n => ModelEditing.Id(n) == "EditorAdded");
            Assert.Contains("PropertyName", ModelEditing.MissingRequired(column));

            ModelEditing.SetText(column, "PropertyName", nameof(ModelTestOrder.Number));
            Assert.DoesNotContain("PropertyName", ModelEditing.MissingRequired(column));
        }
        finally {
            column.Remove();
        }
    }

    // Every property has a generated column, so an id like "Number" is taken even when the column is hidden.
    [Fact]
    public void AddChild_WithATakenId_ThrowsAndAddsNothing() {
        var count = OrderListView.Columns.NodeCount;
        var ex = Assert.Throws<InvalidOperationException>(() => ModelEditing.AddChild(OrderListView.Columns, typeof(IModelColumn), "Number"));
        Assert.Contains("already has a node", ex.Message);
        Assert.Equal(count, OrderListView.Columns.NodeCount);
    }

    // A band offers Band as a child, but bands hold no children: the new band goes into the bands layout, owned by the selected
    // band, as the WinForms editor adds it (Codex review).
    [Fact]
    public void AddChild_OfABandUnderABand_GoesIntoTheBandsLayoutOwnedByThatBand() {
        var bands = OrderListView.BandsLayout;
        var outer = (IModelBand)ModelEditing.AddChild(bands, typeof(IModelBand), "EditorOuter");
        try {
            Assert.Contains(ModelEditing.CreatableTypes(outer).Values, t => typeof(IModelBand).IsAssignableFrom(t));
            var inner = (IModelBand)ModelEditing.AddChild(outer, typeof(IModelBand), "EditorInner");

            Assert.Contains(ModelEditing.Children(bands), n => ReferenceEquals(n, inner));
            Assert.Equal("EditorOuter", ModelEditing.Id(inner.OwnerBand!));
        }
        finally {
            bands.GetNode("EditorInner")?.Remove();
            outer.Remove();
        }
    }

    // As the WinForms editor's UpdateNewNode does (ModelEditorViewController.cs 881-886).
    [Fact]
    public void AddChild_OfAMember_MarksItCustomAndCalculated() {
        var members = fixture.Class<ModelTestOrder>().OwnMembers;
        var member = (IModelMember)ModelEditing.AddChild(members, typeof(IModelMember), "EditorAddedMember");
        try {
            Assert.True(member.IsCustom);
            Assert.True(member.IsCalculated);
        }
        finally {
            member.Remove();
        }
    }

    [Fact]
    public void Clone_AddsASiblingWithTheSameValues() {
        var number = OrderListView.Columns["Number"]!;
        var clone = ModelEditing.Clone(number, "NumberCopy");
        try {
            Assert.Equal(number.PropertyName, ((IModelColumn)clone).PropertyName);
            Assert.Equal(ModelEditing.Path(OrderListView.Columns), ModelEditing.Path(clone.Parent));
            // The session recognises its added nodes by instance, so the tree must hand back the same one.
            Assert.Contains(ModelEditing.Children(OrderListView.Columns), n => ReferenceEquals(n, clone));
        }
        finally {
            clone.Remove();
        }
    }

    [Fact]
    public void CanDelete_AllowsAListChild_ButNotTheListItself() {
        Assert.True(ModelEditing.CanDelete(OrderListView.Columns["Number"]!));
        Assert.False(ModelEditing.CanDelete(OrderListView.Columns));
    }

    // Up/Down renumbers the shown siblings the way ModelEditorControllerBase.ChangeNodeIndex does: hidden (negative
    // index) siblings keep their index.
    [Fact]
    public void IndexesForMove_SwapsANodeWithItsShownNeighbour() {
        var customer = OrderListView.Columns["Customer"]!;
        var indexes = ModelEditing.IndexesForMove(customer, up: true).ToDictionary(p => ModelEditing.Id(p.Node), p => p.Index);
        Assert.Equal(0, indexes["Customer"]);
        Assert.Equal(1, indexes["Number"]);
        Assert.DoesNotContain(nameof(ModelTestOrder.SyncToken), indexes.Keys);
    }

    // A node added in an editor closed without Save must not stay in the running model, where a later save would store it.
    [Fact]
    public void Session_RollbackAdded_RemovesTheNodesItAddedOrCloned() {
        var session = new ModelEditSession();
        var added = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "RolledBack");
        session.Clone(added, "RolledBackCopy");
        Assert.True(session.IsAdded(added));

        session.RollbackAdded();
        Assert.DoesNotContain(ModelEditing.Children(OrderListView.Columns), n => ModelEditing.Id(n) is "RolledBack" or "RolledBackCopy");
    }

    // A save from elsewhere rolls the added nodes back while the editor is open; a pending delete of one must not stay behind.
    [Fact]
    public void Session_RollbackAdded_ForgetsPendingDeletesOfTheNodesItRemoves() {
        var session = new ModelEditSession();
        var added = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "RolledBackDeleted");
        try {
            session.Delete(added);
            Assert.True(ModelEditing.IsInModel(added));

            session.RollbackAdded();
            Assert.False(ModelEditing.IsInModel(added));
            Assert.False(session.IsPendingDelete(added));
            Assert.True(ModelEditing.IsInModel(OrderListView.Columns["Number"]!));
        }
        finally {
            if (OrderListView.Columns["RolledBackDeleted"] is { } left) left.Remove();
        }
    }

    // A required value XAF calculates from another one (a column's PropertyEditorType from its member) is there only once
    // that one is written, so the values of an added node are not kept pending.
    [Fact]
    public void Session_SetText_WritesTheValuesOfAnAddedNodeAtOnce() {
        var session = new ModelEditSession();
        var added = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "WrittenAtOnce");
        try {
            session.SetText(added, "PropertyName", nameof(ModelTestOrder.Number));
            Assert.False(session.TryGetPending(added, "PropertyName", out _, out _));
            Assert.Equal(nameof(ModelTestOrder.Number), ((IModelColumn)added).PropertyName);
            session.Apply();
        }
        finally {
            session.RollbackAdded();
        }
    }

    // After the editor's Save, XAF's deferred save of the old circuit lets its views write their state first; a grid hides
    // every column it does not show (ColumnsListEditor.cs 230-232). The saved edits are written again before that save.
    [Fact]
    public void Session_ReplaySaved_WritesTheSavedEditsAgain() {
        var session = new ModelEditSession();
        var added = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "Replayed");
        var number = OrderListView.Columns["Number"]!;
        try {
            session.SetText(added, "PropertyName", nameof(ModelTestOrder.Customer));
            session.SetText(added, "Index", "3");
            session.SetText(number, "Width", "123");
            session.Apply();
            session.Saved();
            ((IModelColumn)added).Index = -1;
            number.Width = 77;

            session.ReplaySaved();
            Assert.Equal(3, ((IModelColumn)added).Index);
            Assert.Equal(123, number.Width);
        }
        finally {
            added.Remove();
            ModelEditing.Reset(number, "Width");
        }
    }

    // A clone carries values nobody edited; after Save they are written again too, or the old grid hides it (Codex review).
    [Fact]
    public void Session_ReplaySaved_WritesTheValuesOfAClonedNodeAgain() {
        var session = new ModelEditSession();
        var clone = (IModelColumn)session.Clone(OrderListView.Columns["Number"]!, "NumberReplayed");
        try {
            var index = clone.Index;
            session.Apply();
            session.Saved();
            clone.Index = -1;

            session.ReplaySaved();
            Assert.Equal(index, clone.Index);
        }
        finally {
            clone.Remove();
        }
    }

    // A column saved without an Index gets Index -1 from the old grid; the replay clears that again (Codex review).
    [Fact]
    public void Session_ReplaySaved_ClearsAValueTheAddedNodeDidNotHold() {
        var session = new ModelEditSession();
        var added = (IModelColumn)session.AddChild(OrderListView.Columns, typeof(IModelColumn), "SavedWithoutIndex");
        try {
            session.SetText(added, "PropertyName", nameof(ModelTestOrder.Customer));
            session.Apply();
            session.Saved();
            added.Index = -1;

            session.ReplaySaved();
            Assert.False(((ModelNode)added).IsValueModified("Index"));
            Assert.Equal(nameof(ModelTestOrder.Customer), added.PropertyName);
        }
        finally {
            added.Remove();
        }
    }

    // A clone copies its subtree, an incomplete column included; Save checks every node under an added one (Codex review).
    [Fact]
    public void Session_Apply_ChecksTheNodesACloneCopied() {
        var session = new ModelEditSession();
        var incomplete = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "IncompleteBeforeClone");
        session.Clone(OrderListView, "OrderListViewIncompleteCopy");
        try {
            session.Delete(incomplete);
            var ex = Assert.Throws<InvalidOperationException>(() => session.Apply());
            Assert.Contains("OrderListViewIncompleteCopy/Columns/IncompleteBeforeClone", ex.Message);
        }
        finally {
            session.RollbackAdded();
        }
    }

    // The clone would copy the model without the source's pending edits, and a pending reset's outcome is not known before
    // the model is built again; a source with pending edits is saved first (Codex review).
    [Fact]
    public void Session_Clone_RefusesASourceWithPendingEdits() {
        var session = new ModelEditSession();
        var number = OrderListView.Columns["Number"]!;
        session.SetText(number, "Width", "123");

        var ex = Assert.Throws<InvalidOperationException>(() => session.Clone(number, "NumberWithPendingWidth"));
        Assert.Contains("Save the pending edits", ex.Message);
        Assert.Null(OrderListView.Columns["NumberWithPendingWidth"]);
    }

    // A second move counts the first one's pending indexes (Codex review).
    [Fact]
    public void Session_Move_CountsEarlierPendingMoves() {
        var session = new ModelEditSession();
        var customer = OrderListView.Columns["Customer"]!;
        session.Move(customer, up: true);
        session.Move(customer, up: false);
        Assert.True(session.TryGetPending(customer, "Index", out var customerIndex, out _));
        Assert.True(session.TryGetPending(OrderListView.Columns["Number"]!, "Index", out var numberIndex, out _));
        Assert.Equal(("1", "0"), (customerIndex, numberIndex));
    }

    // Invalid text on an added node blocks Save as on any other node, until a valid value replaces it (Codex review).
    [Fact]
    public void Session_InvalidTextOnAnAddedNode_BlocksApply() {
        var session = new ModelEditSession();
        var added = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "InvalidOnAdded");
        try {
            session.SetText(added, "PropertyName", nameof(ModelTestOrder.Number));
            session.SetText(added, "Index", "3");
            Assert.ThrowsAny<Exception>(() => session.SetText(added, "Index", "three"));
            Assert.True(session.HasErrors);
            Assert.Throws<InvalidOperationException>(() => session.Apply());

            session.SetText(added, "Index", "4");
            Assert.False(session.HasErrors);
        }
        finally {
            session.RollbackAdded();
        }
    }

    // Invalid text on a node marked for deletion does not block Save: the edit goes with the node (Codex review).
    [Fact]
    public void Session_AnErrorOnADeletedNode_DoesNotBlockApply() {
        var session = new ModelEditSession();
        var added = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "InvalidThenDeleted");
        try {
            Assert.ThrowsAny<Exception>(() => session.SetText(added, "Index", "three"));
            Assert.True(session.HasErrors);
            session.Delete(added);
            Assert.False(session.HasErrors);
            session.Apply();
        }
        finally {
            session.RollbackAdded();
        }
    }

    // A node under a deleted added node goes with it, so its required values are not checked (Codex review).
    [Fact]
    public void Session_Apply_SkipsANodeUnderADeletedOne() {
        var session = new ModelEditSession();
        var copy = (IModelListView)session.Clone(OrderListView, "OrderListViewDeletedCopy");
        try {
            session.AddChild(copy.Columns, typeof(IModelColumn), "Incomplete");
            session.Delete(copy);
            session.Apply();
            Assert.Null(OrderListView.Application.Views["OrderListViewDeletedCopy"]);
        }
        finally {
            session.RollbackAdded();
        }
    }

    // A required value cleared under a cloned node blocks Save: nodes under an added one that the editor wrote are checked too.
    [Fact]
    public void Session_Apply_RefusesARequiredValueClearedUnderAClonedNode() {
        var session = new ModelEditSession();
        var copy = (IModelListView)session.Clone(OrderListView, "OrderListViewClearedCopy");
        try {
            session.Reset(copy.Columns["Number"]!, "PropertyName");
            var ex = Assert.Throws<InvalidOperationException>(() => session.Apply());
            Assert.Contains("PropertyName", ex.Message);
        }
        finally {
            session.RollbackAdded();
        }
    }

    [Fact]
    public void Session_Apply_RefusesAnAddedNodeWithoutItsRequiredValues() {
        var session = new ModelEditSession();
        session.AddChild(OrderListView.Columns, typeof(IModelColumn), "Incomplete");
        try {
            var ex = Assert.Throws<InvalidOperationException>(() => session.Apply());
            Assert.Contains("PropertyName", ex.Message);
        }
        finally {
            session.RollbackAdded();
        }
    }

    // With bands enabled each band numbers its own columns, so a column moves only among the columns of its band (Codex review).
    [Fact]
    public void IndexesForMove_InABandedListView_StaysWithinTheOwnerBand() {
        var bands = OrderListView.BandsLayout;
        var number = (IModelBandedColumn)OrderListView.Columns["Number"]!;
        var band = (IModelBand)ModelEditing.AddChild(bands, typeof(IModelBand), "EditorMoveBand");
        try {
            bands.Enable = true;
            number.OwnerBand = band;
            Assert.Empty(ModelEditing.IndexesForMove(OrderListView.Columns["Customer"]!, up: true));
        }
        finally {
            ModelEditing.Reset(number, "OwnerBand");
            ModelEditing.Reset(bands, "Enable");
            band.Remove();
        }
    }

    // A node deleted together with a node under it: the outer one goes first, and the inner one must not fail the Apply.
    [Fact]
    public void Session_Apply_DeletesANodeAndANodeUnderIt() {
        var views = OrderListView.Application.Views;
        var view = (IModelListView)ModelEditing.AddChild(views, typeof(IModelListView), "EditorNestedDelete");
        try {
            var column = ModelEditing.AddChild(view.Columns, typeof(IModelColumn), "Inner");
            var session = new ModelEditSession();
            session.Delete(view);
            session.Delete(column);
            Assert.True(ModelEditing.IsInModel(column));

            session.Apply();
            Assert.Null(views["EditorNestedDelete"]);
            Assert.False(ModelEditing.IsInModel(column));
        }
        finally {
            if (views["EditorNestedDelete"] is { } left) left.Remove();
        }
    }

    // Undo clears a node's values but keeps the node (ModelNode.cs 615-637): a node that exists only in the user's differences
    // is deleted, not reset, or it would be saved without its required values (Codex review).
    [Fact]
    public void Session_ResetNode_RefusesANodeThatExistsOnlyInTheDifferences() {
        var session = new ModelEditSession();
        var added = session.AddChild(OrderListView.Columns, typeof(IModelColumn), "ResetNodeOnAdded");
        var custom = ModelEditing.AddChild(OrderListView.Columns, typeof(IModelColumn), "SavedEarlier");
        try {
            session.SetText(added, "PropertyName", nameof(ModelTestOrder.Number));
            Assert.False(session.CanResetNode(added));
            Assert.Throws<InvalidOperationException>(() => session.ResetNode(added));
            Assert.False(session.CanResetNode(custom));
            Assert.True(session.CanResetNode(OrderListView.Columns["Number"]!));
        }
        finally {
            session.RollbackAdded();
            custom.Remove();
        }
    }

    // A node reset takes back its whole subtree, on Apply and on the replay after Save, so it does not combine with other
    // edits there: neither order is accepted (Codex review).
    [Fact]
    public void Session_ResetNode_DoesNotCombineWithEditsInItsSubtree() {
        var columns = OrderListView.Columns;
        var resetting = new ModelEditSession();
        resetting.ResetNode(columns);
        Assert.Throws<InvalidOperationException>(() => resetting.Delete(columns["Number"]!));
        Assert.Throws<InvalidOperationException>(() => resetting.SetText(columns["Number"]!, "Width", "5"));
        Assert.Throws<InvalidOperationException>(() => resetting.AddChild(columns, typeof(IModelColumn), "UnderReset"));
        Assert.Null(columns["UnderReset"]);

        var deleting = new ModelEditSession();
        deleting.Delete(columns["Number"]!);
        Assert.Throws<InvalidOperationException>(() => deleting.ResetNode(columns));

        var adding = new ModelEditSession();
        adding.AddChild(columns, typeof(IModelColumn), "AddedBeforeReset");
        try {
            Assert.Throws<InvalidOperationException>(() => adding.ResetNode(columns));
        }
        finally {
            adding.RollbackAdded();
        }
    }

    // An id may contain '/', so "EditorNumber/Extra" is EditorNumber's sibling, not a node under it: deleting EditorNumber
    // neither skips nor removes it (Codex review).
    [Fact]
    public void Session_Apply_ChecksASiblingWhoseIdStartsWithADeletedNodesId() {
        var session = new ModelEditSession();
        session.AddChild(OrderListView.Columns, typeof(IModelColumn), "EditorNumber");
        session.AddChild(OrderListView.Columns, typeof(IModelColumn), "EditorNumber/Extra");
        try {
            session.Delete(OrderListView.Columns["EditorNumber"]!);
            var ex = Assert.Throws<InvalidOperationException>(() => session.Apply());
            Assert.Contains("EditorNumber/Extra", ex.Message);
        }
        finally {
            session.RollbackAdded();
        }
    }

    // A move that touches a sibling pending Reset node is refused as a whole, with no partial renumbering (Codex review).
    [Fact]
    public void Session_Move_IsRefusedWholeWhenASiblingIsPendingReset() {
        var session = new ModelEditSession();
        var customer = OrderListView.Columns["Customer"]!;
        session.ResetNode(OrderListView.Columns["Number"]!);

        Assert.Throws<InvalidOperationException>(() => session.Move(customer, up: true));
        Assert.False(session.TryGetPending(customer, "Index", out _, out _));
    }

    // A generated member cannot be deleted, but a custom copy of it can be made (Codex review).
    [Fact]
    public void CanClone_AGeneratedMember_ThoughItCannotBeDeleted() {
        var member = fixture.Class<ModelTestOrder>().OwnMembers[nameof(ModelTestOrder.Number)]!;
        Assert.False(ModelEditing.CanDelete(member));
        Assert.True(ModelEditing.CanClone(member));
        Assert.False(ModelEditing.CanClone(OrderListView.Columns));
    }

    [Fact]
    public void Session_DeletesAndResetsNodes_OnApply() {
        var added = ModelEditing.AddChild(OrderListView.Columns, typeof(IModelColumn), "ToDelete");
        var view = fixture.Class<ModelTestContact>().DefaultListView;
        try {
            ModelEditing.SetText(view, "Caption", "Changed");
            var session = new ModelEditSession();
            session.Delete(added);
            session.ResetNode(view);
            Assert.True(session.IsPendingDelete(added));

            session.Apply();
            Assert.DoesNotContain(ModelEditing.Children(OrderListView.Columns), n => ModelEditing.Id(n) == "ToDelete");
            Assert.False(ModelEditing.IsModified(view));
        }
        finally {
            if (OrderListView.Columns["ToDelete"] is { } left) left.Remove();
            ModelEditing.Reset(view, "Caption");
        }
    }
}
