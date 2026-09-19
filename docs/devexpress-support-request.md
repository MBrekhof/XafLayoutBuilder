# Draft support request: runtime Model Editor for XAF Blazor

Draft for DXSUPPORT-001, collected while building a runtime Model Editor for XAF Blazor 26.1.4 as an add-on
(`XafLayoutBuilder.ModelEditor`, MODELEDITOR-001 onwards). Not sent; the owner decides what goes to DevExpress
and how. Every item names the behaviour at runtime, the 26.1.4 source, the smallest change that would help, and what
the add-on does meanwhile. Line numbers refer to the installed sources under
`Components\Sources\DevExpress.ExpressApp`.

## Summary for the ticket

We built a runtime Model Editor for XAF Blazor on public API (tree, value grid, per-user save). It works, but a
few framework details make it harder and more fragile than it needs to be. The most important is item 1: on a
warmed-up Blazor application `IModelNode.ClearValue` does not update the model's value cache, so a cleared value
keeps returning its old value. Could DevExpress consider the changes below, or advise a supported way?

## 1. ClearValue does not update the warmed-up model's value cache

- **Behaviour.** XAF Blazor 26.1 warms the application up by default (`Optimization.WarmUpApplication`,
  `DevExpress.ExpressApp.Blazor/Services/StartupExtensions.cs` 222) and collapses each application's model
  (`XafApplication.cs` 491-500, `AspNetCoreApplication.CollapseModel` 73-81). `GetValue` then answers from the
  root master's value cache (`Model/Core/ModelNode.cs` 2503-2524). `SetValue` updates that cache
  (`UpdateCachedValue`, 2668), `AddNode` and `Undo` refresh it (`UpdateCache`, 505, 635), but `ClearValue`
  (2368-2390) does not. After `node.ClearValue(name)`, `IsValueModified(name)` is false while `GetValue(name)`
  still returns the cleared value, until the model is built again (next circuit).
- **Repro.** `XafLayoutBuilder.Tests/WarmedUpModelTests.cs`,
  `ClearValue_LeavesTheCachedValue_UntilTheModelIsBuiltAgain`; in a running app, clear a view's `Caption` from code
  after it was set in the user differences and read it back.
- **Smallest change.** In `_ClearValue` (or `ClearValueInThisLayer`), when `IsCached`, recompute the node's cached
  value for that name (as `SetValue` does through `UpdateCachedValue`, or `UpdateCache(this, true, false)`).
- **Meanwhile.** Edits stay pending in the editor, and Save reloads the page so the next circuit rebuilds the model.

## 2. No public way to refresh cached values or undo a single value

- **Behaviour.** `UpdateCache`, `UpdateLocalCacheRecursive`, `CacheAllNodeValues` (`ModelNode.cs` 518-538,
  3607-3635) and `HasValueInThisLayer` / `ClearValueInThisLayer` (2402-2405, 2384-2390) are internal. The public
  dependency updaters in `ModelNodeValuesCache.cs` 371-387 need the root's internal `localCache`. `Undo()` (609)
  refreshes the cache but takes back every modification of the node, children included.
- **Ask.** A public (even `EditorBrowsable(Never)`) `ModelNode.RefreshCachedValues()` or a value-level
  `UndoValue(name)`.
- **Meanwhile.** Page reload after Save (item 1).

## 3. Collapse() throws a NullReferenceException outside BlazorApplication

- **Behaviour.** `ModelApplicationBase.Collapse()` starts with `ModelMultipleMasterStore.Instance.UseThreadLocalMasterStore`
  (`Model/Core/ModelApplication.cs` 699-703). `Instance` is only set by `BlazorApplication`'s constructor
  (`DevExpress.ExpressApp.Blazor/BlazorApplication.cs` 82), so a unit test that builds a model with
  `DesignerModelFactory` and collapses it gets a bare NullReferenceException.
- **Ask.** A clear exception message, or a default store; ideally a documented helper to build a warmed-up,
  collapsed model for tests (DevExpress's own `ModelApplicationTestHelper` needs a full `XafApplication`).
- **Meanwhile.** The tests set `BlazorModelMultipleMasterStore`, `FastModelNodeLockHelper` and the warm-up flag
  themselves and restore them (`WarmedUpModelTests.WithWarmedUpModels`).

## 4. Model Editor logic is WinForms-only

- **Behaviour.** The helpers the editor needs are public but `EditorBrowsable(Never)` (`Model/ModelEditorHelper.cs`,
  the whole class; parts of `Model/FastModelEditorHelper.cs`). Model-only logic lives in the WinForms assembly:
  `ModelValidator`, `LinksNodeHelper`, `ExtendModelInterfaceAdapter`, the lookup list computation in
  `ModelAttributesPropertyGridHelper` (`DevExpress.ExpressApp.Win/Core/ModelEditor/...`, see
  `docs/model-editor-scope.md`).
- **Ask.** Move the platform-neutral editor logic (validation, creatable child types, lookup lists, display values,
  descriptions) into `DevExpress.ExpressApp`, or document it as supported API.
- **Meanwhile.** The add-on uses `FastModelEditorHelper` and ports the rest.

## 5. ModelOperationPermissionRequest in Blazor

- **Behaviour.** Only the WinForms Edit Model and View in Model actions check `ModelOperationPermissionRequest`
  (`DevExpress.ExpressApp.Win/SystemModule/EditModelController.cs` 67-83). It is granted only by a role's
  `CanEditModel` (`DevExpress.ExpressApp.Security/SecurityStrategy/PermissionPolicy/PermissionsExtractor.cs` 51-56,
  `ModelPermissionRequestProcessor.cs` 56-57); `IsAdministrative` does not grant it, which surprised us.
- **Ask.** Say so in the `CanEditModel` / `IsAdministrative` documentation.

## 6. Refreshing a running Blazor application after model changes

- **Behaviour.** WinForms has `WinApplication.EditModel` restarting every window and `WinApplication.Restart()`.
  XAF Blazor has no counterpart; the Blazor deferred user-model save (`IUserModelSaveDispatcher`,
  `Services/AppState/UserModelSaveDispatcher.cs` 50-164) is internal.
- **Ask.** A supported way to rebuild the current application's model and views after a model edit, or to flush the
  user model save.
- **Meanwhile.** Save calls `SaveModelChanges` and reloads the page (`NavigationManager.NavigateTo(uri, forceLoad: true)`).

## 7. The database store keeps an aspect whose differences became empty

- **Behaviour.** `ModelDifferenceDbStore.SaveDifference` writes one `ModelDifferenceAspect` row per aspect but skips
  an aspect whose XML is empty (`ModelDifferenceDbStore.cs` 194-213, `if(!String.IsNullOrEmpty(xml))`). When the
  last localizable value of an aspect is cleared (a caption reset to its default, say), the aspect's XML becomes
  empty, the stored row is neither cleared nor deleted, and the next load brings the value back. Measured in the
  gate (MODELEDITOR-002): after a saved reset of a view caption the `en-US` row still held the caption.
- **Smallest change.** In `SaveDifference`, when an aspect's XML is empty and a stored aspect exists, clear its
  `Xml` or delete it.
- **Meanwhile.** After saving, the editor blanks the stored aspect rows of the aspects that became empty (writes
  `EmptyXafml`), keeping the store's version guard (line 181), and only for the aspects its own save emptied, since
  another tab of the same user may have saved into an aspect that is empty in this circuit's model.

## 8. Deferred user-model saves store whatever the running model holds

- **Behaviour.** Every Blazor application registers a deferred user-model save when it loads the user differences
  (`DevExpress.ExpressApp.Blazor/BlazorApplication.cs` 103-111). It is flushed when the same user's next application
  loads (107, 286-289) and when the circuit closes (`Services/ApplicationSaveModelChangesOnCircuitClosed.cs` 64-82), and
  it calls `app.SaveModelChanges()` (112-132). An editor that has to touch the live model before the user confirms (a
  node added so its values can be filled in) cannot keep that state out of such a save: the dispatcher is internal
  (`Services/AppState/UserModelSaveDispatcher.cs` 50) and there is no event around the save itself.
- **Ask.** A supported "user model saving" event (or a documented use of `CreateCustomUserModelDifferenceStore` for
  it), or public API to flush or drop the current application's deferred save.
- **Meanwhile.** The editor removes its unsaved nodes in `CreateCustomUserModelDifferenceStore`, which `SaveModelChanges`
  raises right before `SaveDifference` (`XafApplication.cs` 1743-1747, 2497-2506).

## 9. Open views overwrite model edits in the next deferred save

- **Behaviour.** The deferred save first runs `SaveModel()` on the main window and its MDI children
  (`BlazorApplication.cs` 121-127). A columns list editor then sets `Index = -1` on every model column its control does
  not show (`Editors/ColumnsListEditor.cs` 230-232). After a model edit adds a column to a ListView that is open, and the
  page reloads, the new application flushes the old application's deferred save, and the old grid hides the new column
  in the stored differences. Measured in the gate (MODELEDITOR-004): the saved column came back with `Index="-1"`.
  WinForms avoids this in `WinApplication.EditModel` by closing every window first and rebuilding them afterwards
  (`DevExpress.ExpressApp.Win/WinApplication.cs` 782-817).
- **Ask.** A Blazor counterpart: close or rebuild the current application's windows after a model edit without their
  saving view state, or a way to discard the current application's deferred save.
- **Meanwhile.** The editor writes its saved edits again right before any later save of that application.

## 10. ClearValue leaves a reference value that was loaded from the differences

- **Behaviour.** A value with a `[DataSourceProperty]`, a node reference such as `IModelListView.DetailView`, is kept in
  the layer under a helper name, `{Name}_ID` (`ModelValuePersistentPathCalculator.GetHelperValueName`,
  `Model/Core/ModelValueCalculator.cs` 58, 96-98), and a value read from stored differences is set under that name only
  (`ModelNode.SetSerializableValues`, `Model/Core/ModelNode.cs` 3149-3166). `ClearValue(name)` removes the value stored
  under `name` itself (2368-2390). So a reset of `DetailView` in a model loaded from the user's differences leaves
  `DetailViewID` in the layer, and the next save stores it again; set and cleared in the same model, the reset works.
- **Repro.** `XafLayoutBuilder.Tests/WarmedUpModelTests.cs`, `Session_ResetOfAReference_TakesItsPersistentValueOutOfTheLayer`
  without the editor's workaround; measured first in the gate (MODELEDITOR-005).
- **Smallest change.** In `ClearValue`, also clear the helper value of a value whose `ModelValueInfo` has a
  `PersistentPath`.
- **Meanwhile.** The editor clears `GetHelperValueName(name)` as well.

## 11. CurrentAspectProviderFromCulture's setter changes the process-wide default culture

- **Behaviour.** `XafApplication.CurrentAspectProvider` is a `CurrentAspectProviderFromCulture` (`XafApplication.cs`
  145). Its `CurrentAspect` setter sets `CultureInfo.DefaultThreadCurrentUICulture` as well as the current thread's UI
  culture (`CurrentAspectProvider.cs` 80-90). In a Blazor Server host every circuit shares the process, so switching the
  aspect through the provider, the way the WinForms Localization window's `AspectScope` does around each read and write
  (`DevExpress.ExpressApp.Win/Core/ModelEditor/Localization/LocalizationItem.cs`), changes the default culture of every
  thread started afterwards, in every circuit. The warmed-up model reads its aspect from the thread's UI culture anyway
  (`UseCurrentUICultureToGetCurrentAspectIndex`, `ApplicationWarmUpService.cs` 169), so the provider is the wrong switch
  there twice over.
- **Repro.** `XafLayoutBuilder.Tests/ModelEditorLocalizationTests.cs`,
  `TheAspectScope_RestoresTheThreadCulture_AndTheModelsAspect` asserts the editor's scope leaves
  `DefaultThreadCurrentUICulture` alone; set the provider instead and it changes.
- **Smallest change.** A public, per-call way to read or write a value in a named aspect (`GetValue(name, aspect)`,
  `SetValue(name, aspect, value)`, `ClearValue(name, aspect)`), or a documented scope that touches the thread only.
- **Meanwhile.** The editor scopes `CultureInfo.CurrentUICulture` and `ModelApplicationBase.SetCurrentAspect` around each
  synchronous read or write (`ModelEditing.Aspect`), which serves both aspect modes.

## 12. No public way to reach or refresh the shared (administrator) differences from a circuit

- **Behaviour.** With warm-up the administrator layer is read once into the shared model every circuit builds on
  (`ApplicationWarmUpService.cs` 150-179, `ApplicationModelManager.cs` 379-385); `LoadUserDifferences` adds only the
  user layer and the user-store event's extra stores (`XafApplication.cs` 1485-1519). A circuit never raises
  `CreateCustomModelDifferenceStore`, so a module cannot learn where the host keeps the shared differences, and a change
  saved to the shared record shows only after a restart (dxdocs 112580 says as much for the Administrative UI).
- **Repro.** Save a caption to the shared `ModelDifference` record; open another circuit: the caption is not there.
- **Smallest change.** A public `XafApplication.CreateModelDifferenceStore()` (it is `protected internal`, 1728) and a
  documented way to re-read the administrator layer per circuit, or an option to load it below the user layer at logon.
- **Meanwhile.** The host names the store (`XafModelEditorModule.SharedDifferences`); the module adds it through
  `AddExtraDiffStore` on the user-store event, so every circuit reads it at logon, and edits it in a second model built
  with `IApplicationModelManagerProvider.GetModelManager()` inside a value-manager storage of its own (`SharedModelSession`).

## 13. ClearValue drops a localizable value in every language

- **Behaviour.** `ModelNode.ClearValue(name)` removes the layer's whole `IModelValue` for the name
  (`ClearValueInThisLayer`, `Model/Core/ModelNode.cs` 2385-2391), whichever aspect is current, while `SetValue` and
  `GetValue` work per aspect (`GetValueCurrentAspectIndex`, 848-863). Clearing a caption's Dutch translation also clears
  its default-language value and every other translation in that layer. The WinForms Localization window's Undo lives
  with it by clearing in the default aspect (`LocalizationItem.cs` `Undo`).
- **Repro.** `XafLayoutBuilder.Tests/ModelEditorLocalizationTests.cs`, `AResetInOneLanguage_KeepsTheOtherLanguagesValues`
  without the editor's workaround.
- **Smallest change.** `ClearValue(name, aspect)` (or clear only the current aspect's value when the value is localizable
  and other aspects hold one).
- **Meanwhile.** The editor reads the other aspects' stored values first and writes them back after the clear
  (`ModelEditing.Reset`).

## 14. Merge Differences cannot reach a layer of another model, or a store

- **Behaviour.** `ModelEditorHelper.MoveNodeToOtherLayer` (`Model/ModelEditorHelper.cs` 393-409) finds its target with the
  internal `ModelNode.GetModuleLayerById` and moves with the internal `ModelNode.MoveNodeToOtherLayer`
  (`Model/Core/ModelNode.cs` 3552-3605), which refuses a layer of another model (3565). In XAF Blazor the shared
  differences are not a layer a circuit's model can write (item 12), so a user's differences cannot be promoted with it.
- **Repro.** None needed: the target layer cannot be obtained with public API.
- **Smallest change.** A public `ModelNode.MoveNodeToOtherLayer(node, targetLayer, moveInfo)` that accepts a layer built by
  the same `ApplicationModelManager`, or a store-level "merge these differences" on `ModelDifferenceStore`.
- **Meanwhile.** The editor writes the user layer's XML per aspect, prunes it to the node and reads it into the shared
  layer before that layer joins its model (`ModelEditing.DifferencesForMerge`, `MergeInto`). XML read over a layer is no
  structural move, so a replaced node and an added or deleted node the shared layer already holds are refused.

## 15. HasModification, IsValueModified and Undo miss the writable layer when a layer in between holds the node

- **Behaviour.** In a collapsed model built over [extra differences layer, user layer] (what `AddExtraDiffStore` gives a
  circuit), a node that the extra layer holds too reports `HasModification` and `IsValueModified(name)` false for values the
  user layer holds, and `Undo()` does nothing; `GetValue` returns the user's value and the user layer's XML holds it. The
  same calls on the user layer's own node (`GetNodeInThisLayer`) answer correctly, and its `Undo()` clears the differences.
- **Repro.** `XafLayoutBuilder.Tests/ModelEditorMergeTests.cs`,
  `Merge_MovesValuesIntoTheSharedStore_PerAspect_AndKeepsWhatTheSharedStoreHeld` (the two `IsModified` asserts).
- **Smallest change.** `GetWritableLayer` (1252-1265) resolving the last layer's node for such a node as it does for one the
  extra layer does not hold.
- **Meanwhile.** The editor asks the writable layer's own node (`ModelEditing.IsModified(model, node)`, `UndoInLayer`).

## To verify before sending

- Shared (administrator) differences: done, item 12 (MODELEDITOR-010).
- Aspects on a warmed-up model (MODELEDITOR-008): done, item 11; the value cache is per aspect (`TryGetValueFromCache`
  takes the aspect index, `ModelNode.cs` 2514-2518), so item 1 gains nothing there.
