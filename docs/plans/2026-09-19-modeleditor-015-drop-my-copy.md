# MODELEDITOR-015: recovering from a merge whose user-side save was refused

Card #1756. Follows MODELEDITOR-009 (6dbad45). DevExpress 26.1 source paths are under
`C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp`.

## The situation

Merge to shared saves the shared record first, then resets (or deletes) the node on the user side and saves the user's own
record. `ModelDifferenceDbStore.SaveDifference` refuses in silence when a newer `Version` is stored or an aspect write is
denied (`ModelDifferenceDbStore.cs` 181, 209); the editor detects that afterwards (`StoredAspectCleanup.UserRecordHolds`) and
says the values are now in both records.

- **Values:** both records hold the same values, the user's win, the node is bold again after the reload, and a second Merge
  settles it, unless whatever refused the save still refuses it (a permission denial rather than a stale version). Not
  asserted in the gate: this card's step covers the stuck case below.
- **A node the user added:** the shared record holds it *and* the user layer keeps its own `IsNewNode` copy. Merge is then
  refused ("shadows a node the model has of its own", MODELEDITOR-009), Reset node leaves an empty node of the user's own
  behind (measured, see below), and Delete writes a tombstone that hides the shared node from this user
  (`ModelNode.CanRemoveNode` 737-761). **Stuck**: today only an administrator resetting that user's differences helps.

## Measured (warmed-up model over [shared layer with the added column, user layer with its own copy])

| on the user layer's own node | user layer XML | what a fresh circuit sees |
|---|---|---|
| `Remove()` | `<ColumnInfo Id="Extra" ... Removed="True" />` | **no column at all**: the tombstone hides the shared one |
| `Undo()` + `SetIsNewNode(false)` | the node is gone from the XML | the shared column, with its values |

`Undo` clears the layer node's values and children (`ModelNode.cs` 609-637) but keeps its own flags (Codex, MODELEDITOR-009
round 2); `SetIsNewNode` is public (684). A node left with neither values nor flags is not written at all
(`ModelXmlWriter.IsNotEmptyNode`, `ModelXmlWriter.cs` 84-95).

## Design

- `ModelEditing.DropFromLayer(model, node)`: `Undo()` and `SetIsNewNode(false)` on the writable layer's own node
  (`Writable`, MODELEDITOR-014). Nothing when the layer does not hold the node.
- `ModelEditSession.CanDropCopy(node)`: the writable layer holds the node, its layer node is `IsNewNode`, and another layer
  holds a node of that id (`EnumerateAllLayers().Count() > 1`, as MODELEDITOR-009's refusal counts them). Merge refuses that
  case; Reset node is offered but answers it badly. Not offered for a node added in this session (Delete removes those).
- `ModelEditSession.DropCopy(node)` pends like a node reset, with the same guards (nothing else pending in that subtree, not
  under a pending reset), and `WritePending` applies it. Save writes it, so the user's record loses the node and the page
  reloads onto the shared one.
- UI: a "Drop my copy" button next to Reset node, and MODELEDITOR-009's refusal message names it.
- Not done (ponytail): narrowing the window by checking the user record's `Version` before the shared save (it narrows,
  never closes, and the recovery above is what makes it survivable); a bulk "reset my whole model" action, which the
  Administrative UI already is.

## Codex plan review (2026-09-19): no P1s, all three taken

1. **P2, the recovery Save was not verified.** `verifyUserRecord` only became true after a Merge, so the Save after Drop my
   copy could be refused in silence again. Drop my copy sets it too.
2. **P2, the gate bumped the version too early**, which would have refused the setup save rather than the merge. The step
   now saves the column first, bumps `Version` after that, and lets the reload give the next circuit the current version.
3. **P3, two claims overstated.** "A second Merge settles it" holds for values after a reload, not against a continuing
   permission denial; and the existing gate merges both succeed, so this card's step is the first with a refusal in it.

Codex verified clean: `Undo()` + `SetIsNewNode(false)` clears children and every language together and serializes to nothing.

## Codex diff review (2026-09-19): three P2s, all fixed

1. **A lookup edit was allowed beside a pending drop**, so a reference could be saved to a node the drop then removed.
2. **Clone ignored a pending drop in its source**, so the clone would keep values the drop takes away.
3. **The gate step left the database dirty when it failed** (a bumped version and the shared column), since its cleanup came
   after the assertions. It runs in a `finally` now.

A pending drop is a change to the saved model like a node reset, so it joins the checks a pending reset already fails
(lookup edits, Clone, Reset node). The test was red before the guards: neither the lookup edit nor the clone was refused.

## Measured while writing the tests, and kept as tests

- **Reset node is offered on such a node** (`CanResetNode` asks the merged node, which carries no mark of its own), but
  `Undo` keeps the layer node's `IsNewNode`, so the user's record keeps an empty node of its own. That is why Drop my copy
  exists, and `ResetNode_OnTheUsersOwnCopy_LeavesAnEmptyNodeOfItsOwnBehind` says so.
- **Over a layer that only deletes the node**, `EnumerateAllLayers` counts one layer, so Drop my copy is not offered:
  nothing would come back and Delete is the answer. Measured, then written down as a test.

## Tests, red first

Unit (`ModelEditorMergeTests`, warmed-up, [shared layer holding the added column, user layer with its own copy]):

1. `CanDropCopy` is true there; false when the shared layer does not hold it (Delete is the answer then), false for a node
   added in this session, false for a generated node the user only edited (Reset node is the answer).
2. `DropCopy` + `Apply`: the user layer's XML no longer holds the node, and holds no `Removed` mark; a circuit built over
   [shared, that XML] has the column with the shared values.
3. Delete on the same node still writes the tombstone (documents why Drop my copy exists).

Gate, after the MODELEDITOR-016 step (Codex's ordering): add a column and Save it, *then* raise Admin's record `Version` in
SQL so the next save of that record is refused in silence, Merge to shared; assert the editor says so and that both records
hold the column. Close (the refused merge's writes are discarded and the page reloads, MODELEDITOR-016), so the next circuit
holds the current version; reopen, assert Drop my copy is offered, press it and Save; assert Admin's record no longer holds
the column, the shared record still does, and the column still shows in the grid. The step then clears both records, so the
steps after it start from the builder's own layout.
