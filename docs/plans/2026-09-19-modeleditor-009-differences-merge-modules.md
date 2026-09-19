# MODELEDITOR-009: differences XML, Generate Content, Loaded Modules, Merge Differences

Card #1701. Scope: `docs/model-editor-scope.md`, "Differences, merge, modules". Public API only; DevExpress 26.1.4 source
paths below are under `C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp`.

## Facts (verified in the 26.1 source)

- **Differences XML.** WinForms shows `ModelEditorHelper.GetNodeInLayer(node, LastLayer).Xml`
  (`DevExpress.ExpressApp.Win\...\ModelEditorViewController.cs` 795-805). `GetNodeInLayer` is
  `FindNodeByPath(path, layer, inThisLayer: true, findLastNodeByPath: false)` (`Model\ModelEditorHelper.cs` 71-94, 350-353):
  it finds, never creates; null when the layer has no such node. `ModelNode.Xml` writes the current aspect only
  (`Model\Core\ModelNode.cs` 3501-3503); `ModelXmlWriter.WriteToString(node, aspectIndex)` is public (`ModelXmlWriter.cs` 108)
  and already used by `SharedModel.StoredHolds`.
- **Generate Content.** `ModelEditorHelper.IsGenerateContentNode` (231-241) is true when a child list node's generator carries
  a visible `[ModelGenerateContentAction]`: the ListView, DetailView and DetailView-layout generators
  (`Model\NodeGenerators\ModelListViewNodesGenerator.cs` 95, `ModelDetailViewNodesGenerator.cs` 64,
  `ModelDetailViewLayoutNodesGenerator.cs` 49). `GenerateContent` (242-257) adds a temporary sibling of the same type, applies
  the node's diff values, runs the generators, merges the generated children into the node and deletes the sibling. It writes
  to the model at once. WinForms enables it for a single, valid, not read-only node (VC 2521-2531).
- **Loaded Modules.** A read-only grid of `ModuleBase` with Name, Version, AssemblyName (`ModelEditorControl.cs` 470-493).
- **Merge Differences.** WinForms calls `ModelEditorHelper.MoveNodeToOtherLayer` (393-409, `(Never)`), which needs a module
  layer of the same model (`ModelNode.GetModuleLayerById`, internal) and the internal `ModelNode.MoveNodeToOtherLayer`
  (3552-3605), which refuses a layer of another model ("belong to different models"). XAF Blazor's shared differences are not
  a writable layer of the circuit's model (MODELEDITOR-010), so that route is closed. What it does (3751-3840): values and
  child nodes of the node in the last layer move to the same path in the target layer (localizable values merge per aspect,
  `IsNewNode`/`IsRemovedNode` carry over), and what moved leaves the source layer.
- **The XML route is public.** `ModelXmlReader.ReadFromString(layerRoot, aspect, xml)` (`ModelXmlWriter.cs` 347) is how every
  store loads a layer (`AddChildNodeFromXml`, `ModelNode.cs` 3364-3416: an existing node in the layer is reused, so values
  merge; `IsNewNode`, `IsRemovedNode` are serialized values). `ModelNode.GetXmlName()` (3458) and `KeyValueName` (316) are public.

## Design

All logic in `ModelEditing` / `ModelEditSession` / `SharedModelSession` (unit tested); the component only calls it.

1. **Differences XML** – `ModelEditing.DifferencesXml(node)`: `GetNodeInLayer(node, root.LastLayer)`; for each aspect of the
   layer with non-empty `WriteToString(layerNode, i)`, the aspect name ("(default)" for "") and the XML. Empty list when the
   layer has no such node. UI: a "Differences" toggle in the node actions showing `<pre class="xlb-differences">` per aspect. In
   the shared editor `LastLayer` is the shared layer, so it shows the shared differences. Pending (unapplied) edits are not in
   the layer and not shown; the panel says so when the session has pending edits.
2. **Loaded Modules** – a "Modules" toggle next to Save showing a table (`xlb-modules`) of `Application.Modules`: Name, Version,
   AssemblyName, ordered by name. No model logic, no unit test; the gate asserts a row for `XafLayoutBuilderModule`.
3. **Generate Content** – `ModelEditing.CanGenerateContent(node)` = `IsGenerateContentNode` and not read-only
   (`Helper.IsReadOnly(node, null)`); `ModelEditSession.GenerateContent(node)`:
   - refused under a pending node reset, on a node pending delete, and while `MissingRequired(node)` is non-empty (the
     generators read `ModelClass`; WinForms requires a valid node);
   - refused while other edits are pending on or under the node ("save first"), as Clone refuses: the generator reads the model,
     not the pending texts;
   - calls `ModelEditorHelper.GenerateContent`. On a node added in this session the result is rolled back with the node. On any
     other node the write is live and cannot be taken back in a warmed-up model, so it is recorded as an applied write
     (`writes.Add((node, () => { }))`): `HasPendingEdits` is true, Reload/close take the existing "discarded applied edits"
     path (page reload, deferred user-model save suppressed).
   UI: a "Generate content" button when `CanGenerateContent(selected)`.
   To verify first in `WarmedUpModelTests`: that `GenerateContent` works on a collapsed model (the temporary sibling, `Merge`).
   If it cannot, the button is limited to what works and the limitation goes into `docs/devexpress-support-request.md`.
4. **Merge Differences (user → shared)** – only in the user's own editor (`Shared is null`), only when
   `SharedModel.CanEdit(application)`, only on a node with differences in the user layer.
   - `ModelEditing.DifferencesForMerge(node)`: per aspect, the user layer's XML pruned to the node's path. Built from the
     layer-node chain root → node: each ancestor becomes an element `GetXmlName()` with only its key attribute
     (`KeyValueName` = Id, omitted when the writer omits it, i.e. when Id equals the XML name), the node itself is
     `WriteToString(layerNode, aspectIndex)` inserted as the innermost element. Refused when an ancestor is `IsNewNode` in the
     user layer ("merge {that ancestor} instead"): its own values would be left behind and the shared layer could not hold the
     child.
   - `SharedModelSession` gets an optional `preload` (`Action<ModelApplicationBase>` on the layer, run after
     `CreateLayerByStore` and before `CreateModelApplication`): the merge reads each aspect's XML into the layer with
     `ModelXmlReader.ReadFromString(layer, aspect, xml)`, which is the store's own load path, before the layer joins the
     collapsed master. (MODELEDITOR-010 found that nodes *added* to a layer before it joins break the warmed-up unchangeable
     layer; nodes *loaded from XML* are what every store does. The warmed-up test proves it; if it fails, read after joining.)
   - Order in the component (`MergeToShared`): refuse when `Session.HasPendingEdits` ("save first"); run the existing
     unusable-differences warning first (extracted from `Save`); collect the XML; open the shared session with the preload and
     `Persist` (snapshot check, save, `Verify`, version bump: all existing, serialized by `SaveGate`); then on the user side
     `Session.ResetNode(node)`, or `Session.Delete(node)` when the node is `IsNewNode` in the user layer, and run the existing
     `Save` body (Apply, `SaveModelChanges`, emptied-aspect cleanup, `Saved`, page reload). Shared first: if the user-side save
     then fails, the differences exist in both layers with equal values, which is harmless and retryable; the other order
     could lose them.
   - After the reload the circuit layers the shared store below the user layer (MODELEDITOR-010), so the merged values show
     from the shared layer and the node is no longer bold.
   - Not done (ponytail): merging several nodes at once; choosing which values move (`IModelNodeMoveInfo`); a merge into
     Model.xafml (not writable at runtime in a deployed Blazor host).

## Codex plan review, round 1 (2026-09-19): settled, these override the design above

1. **P1, the XML overlay is not a structural move** (a user-layer `IsRemovedNode` read over a shared `IsNewNode` node leaves
   both flags, which reads as a replacement, `ModelNode.cs` 2090; a replacement keeps the target's old values and children,
   where the native move clears them, 3751-3810). **P1, deleting a user replacement leaves a tombstone** (a node that is new
   over an inherited node of the same id is not physically removed, 737-771, and `UndoCore` 615-637 keeps the node's own
   flags). Settled by refusing what the overlay cannot express, checked on the user-layer subtree (`NodeCountInThisLayer`,
   `GetNodeInThisLayer`, `IsNewNode`, `IsRemovedNode`, all public) before anything is saved:
   - a node that is both `IsNewNode` and `IsRemovedNode` (a replacement), anywhere in the subtree or the node itself;
   - an `IsRemovedNode` or `IsNewNode` node whose path already exists in the shared layer itself
     (`FindNodeByPath(path, sharedLayer, inThisLayer: true, false)`).
   The message names the node and points to Edit Shared Model. A plain added node (new, not removed, nothing of that id
   below) is physically removed by `Delete`, which the test asserts on the user layer's XML.
2. **P2, key omission.** The wrapper is not built by hand. The whole user layer is written per aspect
   (`WriteToString(userLayerRoot, i)`, what the store writes) and the `XmlDocument` is pruned to the path: at each level the
   child element whose name is the layer node's `GetXmlName()` and whose `KeyValueName` attribute, when present, equals its
   Id; siblings are dropped, ancestors keep only that key attribute. The keys are the writer's own.
3. **P2, a refused user-side save is silent.** Existing behaviour of every user save (`ModelDifferenceDbStore.cs` 181, 209;
   support request item). For a merge the outcome is equal values in both layers and a node that stays bold after the reload;
   pressing Merge again is idempotent. Documented in the README paragraph, not fixed here.
4. **P1, close does not take the applied-write path; P1, a no-op write is not replayable.** Both go away by narrowing
   (ponytail): **Generate Content only on a node added in this session, or under one.** Such a node is removed on close and
   before any foreign save (`RollbackAdded`), and `Saved` already records `StoredValueWrites` for its whole subtree, generated
   children included, so the replay holds real values. WinForms' use on a stock view (re-adding deleted generated nodes) is
   left out; Reset node covers it. No `writes` marker, no `DiscardedApplied` involvement.
   Read in code on the way, **unverified, not filed**: after an Apply whose save threw, closing twice unsubscribes
   `BeforeSave` without `Discard`, so the applied edits stay in the live user layer for XAF's deferred save
   (`ModelEditorController.cs` 114-121). Pre-existing (MODELEDITOR-010), needs a failed save first.
5. **P2, a generator exception leaves the temporary sibling.** `GenerateContent` is wrapped: the parent's child ids are
   taken before, and in a `finally` any sibling that was not there before and is not the node is removed.
6. **P2, pending-lookup rule.** Generation is refused while any value edit, delete or node reset is pending anywhere in the
   session ("save first"), not only under the node. Lookups written to added nodes (`addedLookups`, the new view's own
   `ModelClass`) do not block it: generation only adds nodes under a node that did not exist when they were chosen.

## Codex plan review, round 2 (2026-09-19): accepted

1. **P1, a plain `IsNewNode` node may exist below too** (a module later brought a node of that id): `Delete` then writes a
   tombstone (`CanRemoveNode`, `ModelNode.cs` 737-761) that hides the merged node. A user-added node is merged only when the
   circuit's model holds it in the user layer alone: the layer node's public `EnumerateAllLayers()` (3469-3496) yields one
   node. Otherwise refused; the message says the user's copy shadows a node the model has of its own.
2. **P1, the backing node may be gone from the shared model by now** (a difference on a shared-only view someone deleted
   since this circuit's logon): after the shared session is built with the preload and before `Persist`, the selected node's
   path must resolve in the session's model; otherwise refused, nothing saved, the user's difference stays.
3. **P2, "merge again is idempotent" was wrong for an added node**, which rule 1 then refuses. The user-side save is
   verified for a merge: the stored user record must hold the user layer (`SharedModel.StoredHolds` over the user's rows),
   else the editor says the values are now in both layers. For values a second Merge settles it; an added node's left-over
   copy needs an administrator to reset that user's differences (README).

## Codex diff review (2026-09-19): fixed

1. **P1, a difference further down may have lost its node too.** `HasNode` looked at the merged node only; the shared model
   drops a difference without a backing node when it is built (`ModelNode.cs` 2087), and the user side was reset right
   after. `MergeDifferences.Nodes` lists every node of the subtree that is not a deletion and
   `SharedModelSession.FirstMissing` names the first one the merged model lacks; reproduced in
   `SharedSession_FirstMissing_NamesADescendantTheSharedModelLostSince`.
2. **P2, Save after a refused merge skipped the stored-record check.** Once a merge ran, every save of that editor checks
   it (`verifyUserRecord`). Read in code, not reproduced at runtime: it needs the store to refuse the user's save.

3. **Re-review, P2: that check refused a subtree holding a deleted node with differences under it** (a deleted node keeps
   them, `ModelNode._Delete` 771-791, and the merged model has none of those nodes). Nodes under a deletion are not
   expected; reproduced red in `DifferencesForMerge_DoesNotExpectNodesUnderADeletedNode`.

## Found on the way

- In the app the caption is saved in the circuit's language (en-US), not the default aspect; Differences shows each
  language the layer holds, and Merge moves each of them (the gate's first run failed on a selector that assumed "").
- `HasModification`, `IsValueModified` and `Undo` on the merged node miss the user layer when the shared layer holds the
  node too. Node level fixed here (`IsModified(model, node)`, `UndoInLayer`); value level is MODELEDITOR-014 (#1755).

## Tests (written first, proven red)

`ModelEditorMergeTests` (new, warmed-up fixtures as in `ModelEditorSharedModelTests`):

- `DifferencesXml` returns the default-aspect XML of an edited ListView caption, an nl-NL entry after a translated caption,
  nothing for an unmodified node.
- `DifferencesForMerge` for a column under `Views/X_ListView/Columns`: XML holds the path elements with only key attributes,
  not a sibling view's differences, not the ListView's own caption.
- Merge end to end: user layer holds a caption (default + nl-NL) and a width on a column; after preload + `Save`, the memory
  shared store holds them; after `ResetNode` + `Apply` the user layer's XML no longer does; a next circuit
  (shared layer + user layer) reads the values; an existing shared value not in the user's XML survives.
- Merge of a user-added column (`IsNewNode`): shared store holds it with `IsNewNode`, next circuit has the column; a child of
  a user-added node is refused naming the ancestor.
- `CanGenerateContent`: true for a ListView, false for a column. `GenerateContent` on a cloned-then-emptied / newly added
  ListView with `ModelClass` set fills `Columns`; refused without `ModelClass`; on a saved node it sets `HasPendingEdits`.

## Gate (E2E)

After the MODELEDITOR-010 step, as Admin: Edit Model on Order_ListView, set the caption, Save; reopen, "Differences" shows
the caption XML (screenshot); "Modules" lists `XafLayoutBuilderModule`; "Merge to shared", then the DB shows the caption in
the shared record (UserId '') and not in Admin's; User sees it at logon; cleanup resets it through Edit Shared Model as the
010 step does. Generate Content: add a ListView node with `ModelClass` Order, Generate content, columns appear in the tree;
closed without Save (rolled back). DOM of any DevExpress component read from the running sample first.

## Docs

`docs/api-notes.md` (facts above with file and line), `docs/model-editor-scope.md` (009 status), CHANGELOG `Unreleased`,
README Model Editor paragraph, `docs/devexpress-support-request.md` item 14: a public `MoveNodeToOtherLayer` across models /
to a store would replace the XML route.
