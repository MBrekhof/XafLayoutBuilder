# MODELEDITOR-014: value-level reads and resets through the writable layer's own node

Card #1755. Follows MODELEDITOR-009 (6dbad45), which fixed the node level. DevExpress 26.1 source paths are under
`C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp\DevExpress.ExpressApp`.

## Measured (warmed-up model over [shared layer, user layer], user sets `Phone.Width = 123` and a caption)

| shared layer holds | `phone.Application` | `IsValueModified("Width")` | `ClearValue("Width")` |
|---|---|---|---|
| nothing | collapsed root, `LastLayer` null | true | clears the user layer |
| the view only | same | true | clears |
| a sibling column | collapsed root, `LastLayer` another layer | true | clears |
| **the column itself** | the circuit's model | **false** | **no effect, the user layer keeps Width** |

`ModelEditorHelper.HasValueInCurrentAspect` was true in all four. One root: `ModelNode.GetWritableLayer()`
(`Model/Core/ModelNode.cs` 1252-1265) behind `HasModification` (885), `IsValueModified` (899), `Undo` (609) and `ClearValue`
(2378-2383). On the user layer's own node (`GetNodeInThisLayer` down the path) all of them work (MODELEDITOR-009 tests).
A node gives no reliable route to its model (`Application.LastLayer` was null, another layer, or the user layer), so the model
is passed in.

Symptom in the app: after Edit Shared Model or a Merge touched a node, a value the user then changes on that node is not
bold, has no Reset button, and a pending Reset (or an empty text that clears) saves nothing.

## Design

- `ModelEditSession.Model` (`ModelApplicationBase?`), set by `ModelEditorPropertyEditor.CreateComponentModel` to the model the
  component gets (the circuit's, or the shared session's). Null keeps today's calls (the many tests on a single-layer model).
  It replaces MODELEDITOR-009's `ResetNode(node, model)` parameter and its `resetModels` dictionary.
- `ModelEditing`: one private helper `Writable(model, node)`: the layer's own node when a model is given and the layer holds
  the node, null when it does not, the merged node when no model is given. Used by
  - `Values(node, model = null)` / `Row`: `IsModified` of a value;
  - `Reset(node, name, model = null)`: the "other languages hold a value" check, `ClearValue` of the value and of its
    `{Name}_ID` helper value; the write-back of the other languages stays on the merged node (`SetValue` reaches the user
    layer: measured, the width was written there in all four cases);
  - `SetText(node, name, text, model = null)`, whose empty text resets;
  - `StoredValueWrites(root, model = null)`: `IsValueModified` and `ClearValue` in the replay.
- The session passes `Model` in `Reset`, `SetText` (added nodes), `WritePending`'s closures, the node-reset closure and
  `Saved`'s `StoredValueWrites`; the component passes `Model` to `Values`.
- Not touched: `MissingRequired`, lookups, `LocalizableValues` (`HasValueInCurrentAspect` measured fine), the old
  `IsModified(node)` overload (tests use it to document the DevExpress behaviour).

## Codex plan review (2026-09-19)

The four checks came back clean (layer-node reads and resets, write routing, the single nullable `Model`, the replay over a
rebuilt circuit). One P2, in code the plan touches: `StoredValueWrites`' replay cleared a value with `ClearValue` when the
snapshot held nothing for that language, which drops the value in every language (`ModelNode.cs` 2384-2390). Measured
first: `IsValueModified` is per aspect, so the clear only runs when that language holds a value written since the snapshot;
reproduced red with a translation written after the snapshot (`StoredValueWrites_ReplayKeepsTheDefaultLanguage_...`), fixed
by resetting through `ModelEditing.Reset`, which writes the other languages back. Two earlier Codex runs were blocked by
its Windows sandbox setup (a 283-character path in the Codex desktop app's runtime cache), not by the plan.

## Codex diff review (2026-09-19)

Five areas clean. One P3, fixed test first: the application root has no ids, so `Writable` took it for missing from the
layer; it is the layer itself. Not reachable in the editor, whose tree starts at the root's children.

## Tests (red first), model = [shared layer holding the Phone column, user layer], warmed up

1. `Values(phone, model)` marks Width modified, where `Values(phone)` does not (documents the DevExpress behaviour).
2. A session with `Model`: `Reset(phone, "Width")` + `Apply` takes Width out of the user layer's XML; without the fix it stays.
3. `SetText(phone, "Width", "")` + `Apply` does the same.
4. A reset of the caption in nl-NL keeps the default-language caption in the user layer (the MODELEDITOR-010 write-back, in
   this configuration).
5. The MODELEDITOR-009 merge test uses `Model` instead of the removed `ResetNode` parameter.

## Gate

In the MODELEDITOR-009 step, after the first merge (the shared record now holds Order_ListView): Admin sets the caption again
and saves; reopened, the Caption row offers Reset (`.xlb-reset`), a saved Reset takes the caption out of Admin's record
(SQL) and the list shows the shared caption again. Then the step goes on to its second merge as before.
