# MODELEDITOR-016: after a failed Save, a discarded edit is still stored at the next logon

Card #1757. DevExpress 26.1 source paths are under
`C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp`.

## Reproduced (2026-09-19, throwaway gate step, sample host, as Admin)

A temporary `CHECK (Xml NOT LIKE '%FAILSAVE%')` on `ModelDifferenceAspects` makes one save fail. Edit Model, Order_ListView
caption "FAILSAVE caption", Save: "Cannot save user settings to the database", nothing stored. Constraint dropped. Close
(warned "close again to discard"), close again, Log Off, log on: Admin's aspect row holds the caption and the list shows it.

## Facts

- Save's `Session.Apply` writes the pending edits into the live user layer before `Application.SaveModelChanges()`
  (`ModelEditorComponent.razor`, `SaveUserModel`). When that throws, the edits stay in the live model: a warmed-up model cannot
  take them back (MODELEDITOR-002). `ModelEditSession.writes` keeps them, so `HasPendingEdits` stays true.
- XAF Blazor's deferred user-model save runs when the same user's next application loads its differences and when the
  circuit closes, and calls `app.SaveModelChanges()` (`DevExpress.ExpressApp.Blazor/BlazorApplication.cs` 103-132, 286-289;
  `Services/ApplicationSaveModelChangesOnCircuitClosed.cs` 64-82). `SaveModelChanges` raises
  `CreateCustomUserModelDifferenceStore` right before `SaveDifference` and saves nothing when a handler answers `Handled`
  with a null `Store` (`XafApplication.cs` 408-414, 2497-2506).
- Reload covers this today: `Session.Discard()` sets `DiscardedApplied`, and `ModelEditorController.BeforeSave` answers with
  no store (MODELEDITOR-010, Codex review 2). But `BeforeSave` finds the sessions through the popup's view and returns early
  when `view.IsDisposed`, and `view.Closed` unsubscribes it and only calls `RollbackAdded` (`ModelEditorController.cs`
  114-143). So the guard does not outlive the popup: closing loses it, and after Reload it holds only while the old
  circuit's view is still alive when the flush comes (not measured; the red gate run below tells).

## Design

One guard that does not depend on the popup's view, used by both ways of discarding:

- `ModelEditorController.SuppressUserModelSave(XafApplication)`: subscribes a handler to
  `CreateCustomUserModelDifferenceStore` that answers `Store = null, Handled = true`, for the rest of that application's
  (circuit's) life. ponytail, as MODELEDITOR-010 already accepted: the circuit's other runtime customisations since its last
  save go with it; the page reload that follows keeps that window to the moment of the discard.
- **Close:** `view.Closed` calls `Session.Discard()` (which includes `RollbackAdded`) inside the editor's model scope. When it
  returns true (edits an Apply wrote were discarded) and the editor is the user's own (not the shared session, whose model is
  disposed with it), it calls `SuppressUserModelSave` and reloads the page
  (`application.ServiceProvider.GetRequiredService<NavigationManager>().NavigateTo(uri, forceLoad: true)`), as Reload does:
  the new circuit builds its model from what is stored, so the discarded edit is gone from the screen too and saving works
  again.
- **Reload:** the component calls `SuppressUserModelSave` before its `NavigateTo` when `Discard()` returns true. The
  `DiscardedApplied` branch in `BeforeSave` goes; the property stays (unit tested, and harmless).
- Not changed: a failed Save followed by another Save (the retry stores the edits, as intended); the shared editor.

## Codex plan review (2026-09-19): no P1s, both revisions taken

1. **P2, the Reload bullet did not exclude the shared editor.** A failed *shared* save also leaves session writes, so its
   discard returns true and would have suppressed the user's own model, which that save never touched. Both paths now
   suppress only for `Shared is null`.
2. **P3, the gate did not prove the close reloads.** `OpenListView` navigates by itself, so an implementation that
   suppressed but forgot the reload would have passed. The step waits for the navigation on the second close and reads the
   reloaded page before logging off.

Checked clean: no save path bypasses `CreateCustomUserModelDifferenceStore` (circuit close and the next application's flush
both go through `SaveModelChanges`, `BlazorApplication.cs` 128); a null `Store` with `Handled` skips persistence without an
error and does not skip other handlers (`XafApplication.cs` 408, 2497); resolving `NavigationManager` in the view's `Closed`
is fine on the interactive close path; a normal close neither reloads nor suppresses, since `Discard()` returns true only
when an Apply had already written.

## Found by the gate: Reload was refused after a failed Save

The first run with the fix was red on the Reload half, and not for the reason the card describes: the editor answered the
click with "Your model was saved elsewhere, which removed the nodes added here. Select a node." and did nothing. Every
action goes through `Run`, which first checks that the selected node is still the instance the model hands out
(MODELEDITOR-004, for nodes a foreign save removed); after a failed Save the instances differ, so the guard fired. A second
click worked, because the refusal clears the selection. Reload is the one action that must not be refused, and it does not
use the selection: it skips that check (`Run(..., checkSelection: false)`). The gate now reports the editor's message
instead of a bare navigation timeout.

## Tests, red first

The logic that can be unit tested exists and is tested (`Session_AfterAnAppliedButFailedSave_...`: `Discard()` returns
true). The rest needs a circuit, so the reproduction becomes a gate step, run red before the fix:

1. **Close:** constraint on, caption "FAILSAVE closed", Save fails (message checked, nothing stored), constraint dropped,
   close twice, Log Off, log on: Admin's rows do not hold the marker and the list does not show it.
2. **Reload:** the same with "FAILSAVE reloaded" and the Reload button instead of closing.
3. The gate's start-up cleanup drops a left-over `CK_XLB_FailSave` (a run that aborted inside the step), and the step drops
   it in a `finally`.
