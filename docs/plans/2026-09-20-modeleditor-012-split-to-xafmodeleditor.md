# MODELEDITOR-012: split the Model Editor into its own repository (XafModelEditor)

Card: MODELEDITOR-012 (#1704). Owner's decisions, 2026-09-20: **full move (route 1)**, new repository
**`MBrekhof/XafModelEditor`**, **private to start**. Written against the live tree at `db22fca`.

**Codex plan review, round 1: four P1s, all taken** — marked **[R1]**. The type inventory missed a type reached only
through a property, the builder's column order and a second DetailView are hard dependencies of the editor's tests
rather than risks, the gate's editor block is the executable range and not the comment inventory I first cited, and the
removal step missed several references.

**Codex plan review, round 2: four further P1s, all taken** — marked **[R2]**. A ninth gate helper, a call to a moving
helper from the gate's startup cleanup, a dangling embedded resource in the copied sample, and a module selector that
names the builder's module. Every finding of both rounds was re-verified against the tree before it was taken.

**One round-2 finding was checked and rejected:** Codex put the executable block's start at line 640. Line 640 is
`Assert(cityInOther, ...)`, the *builder's* last E2E 8 assertion; 641 is blank and 642 is
`Step("MODELEDITOR-001: ...")`. The range stays **642**-1236. Neither round contradicted the route or the scope.

## 1. What the survey found (two corrections to the handoff's assessment)

`SESSION_HANDOFF.md` lines 16-26 called the harness "most of the work" and said the test fixtures and the support
request "mix both". Measured against the tree, the entanglement is smaller than that:

- **`WarmedUpModelTests.cs` is not shared.** It is MODELEDITOR-002's own file (its header says so), all 17 of its tests
  are editor tests, and the only callers of its `WithWarmedUpModels` / `WithWarmedUpManager` helpers are four editor test
  files. No builder test touches it. It moves whole — **but not unedited [R1]:** it imports `XafLayoutBuilder.Module`
  (line 5) and reads and writes `FailFastOnLayoutErrors` (line 320), which both have to go with the builder.
- **`docs/devexpress-support-request.md` is not mixed.** All 16 items are runtime Model Editor findings; the only builder
  strings in the file are `XafLayoutBuilder.Tests/...` **repro paths**, which the move rewrites. The whole file goes.
- **`DesignerModelFactory` is DevExpress's own type** (`DevExpress.ExpressApp.Utils`), not ours, so
  `ApplicationModelFixture` has no dependency on the editor. The dependency runs the other way: every editor test uses
  the builder's fixture — except `StoredAspectCleanupTests`, which uses no fixture at all.

What is genuinely entangled is one thing: **the editor's tests sit on a fixture whose business types, column order and
second DetailView are all produced by the builder.** Section 4 replaces all three.

## 2. Scope

| | |
|---|---|
| Moves to XafModelEditor | the add-on, 12 test files (133 of 290 tests), the editor's gate block, its docs, its plans, screenshots 13-17 |
| Copied, then diverges | a sample host and sample module, trimmed of every builder project; the gate's shared helpers |
| Stays here | Core, Module, Blazor, Appearance, their tests and gate steps, screenshots 01-12 |
| Deleted here | `XafLayoutBuilder.ModelEditor/`, the 12 test files, the editor's gate block, its docs and its wiring |

## 3. The new repository

- `MBrekhof/XafModelEditor`, **private to start**, MIT.
- **Namespace and package id become `XafModelEditor`**, replacing `XafLayoutBuilder.ModelEditor`. The module class is
  already called `XafModelEditorModule`, so only the namespace moves. This is free: the add-on has `IsPackable=false`
  and has never been published, so there is no consumer to break. (Codex could confirm `IsPackable=false` but not the
  publication history; the feed's 0.3.0 push listed Core, Module, Blazor and Appearance only, which is the evidence.)
- Projects: `XafModelEditor` (the add-on), `XafModelEditor.Tests`, `XafModelEditor.E2ETests`,
  `XafModelEditor.Sample.Module`, `XafModelEditor.Sample.Blazor.Server`. The add-on's `InternalsVisibleTo` is renamed to
  `XafModelEditor.Tests` **[R1]**.
- `Directory.Build.props` copied with `DevExpressVersion` 26.1.4 and `DevExpressPackageRange` `[26.1.4,26.2)` unchanged —
  both repositories move that version together, as the comment there already says. `PackageVersion` starts at **0.1.0**.
  `PackageProjectUrl`, `RepositoryUrl`, `PackageTags` and the description are rewritten for the new repository **[R1]**;
  copying them unchanged would point the new package at `MBrekhof/XafLayoutBuilder`.
- **The sample's LocalDB catalog becomes `XafModelEditor.Sample` [R1].** `XafLayoutBuilder.Sample` is hardcoded in
  `appsettings.json` lines 3-4 and in the gate at `Program.cs:79`, and both gates clear and write Admin's
  `ModelDifferences` rows. A cloned sample left on the old catalog would have the two repositories' gates corrupting each
  other's model differences; port isolation does not cover this.
- `CLAUDE.md` copied and cut down to what applies (the DevExpress verify rule, EF Core only, the plan/Codex loop, the
  board project id once the new board exists), `README.md` and `CHANGELOG.md` written fresh.

## 4. The fixture the new repository needs

The 12 moving test files reference **six** business types: `ModelTestOrder`, `ModelTestLine`, `ModelTestCustomer` and
`ModelTestContact`, plus the two plain classes `ModelTestPlainOwner` and `ModelTestPlainPart` **[R1]**. `ModelTestPlainPart`
is reached only through `ModelTestPlainOwner.Part`, so a search by type name misses it, and it must stay **without**
`[DomainComponent]`: `ModelEditorSpecialEditorsTests.FilterFields_FollowAReferenceThatIsNoDomainComponent` asserts
exactly that (`ModelTestTypes.cs:221-231`, `ModelEditorSpecialEditorsTests.cs:57-64`). The correct reduction is therefore
**four registered domain-component types plus two unregistered plain classes**, not "19 of 24 dropped".

The new `ApplicationModelFixture` keeps `DesignerModelFactory`, `CreateApplicationModel` over a `ModuleBase` requiring
`SystemModule` only, the `Class<T>()` helper and the xUnit collection definition. It drops `XafLayoutBuilderModule` and
the appearance add-on from `RequiredModuleTypes`, `FailFastOnLayoutErrors`, the broken-layout and appearance-clash
fields, `Rules<T>()` and `LayoutIds`.

**Two builder outputs are hard dependencies and get explicit replacements, not a hopeful re-run [R1].** The first draft
called these a risk to verify; they are not risks, they are assertions in the tests:

1. **Column order and visibility.** `ModelEditorTreeTests.Children_ListShownColumnsInIndexOrder_BeforeHiddenOnes` (line
   16) expects `Number, Customer, OrderDate`; `ModelEditorNodeTests` expects Customer/Number at indexes 0/1 then 1/0
   (lines 100, 252) and `SyncToken` hidden. All of that is `ModelTestOrder.BuildListViewColumns` (`ModelTestTypes.cs:58`).
   The replacement declares the same order and the hidden `SyncToken` as an explicit XAFML difference in the fixture's
   model store.
2. **A second DetailView for `ModelTestOrder`.** Five lookup tests pick a choice *other* than the current one
   (`row.Choices!.First(c => c != row.Text)`, `ModelEditorLookupTests.cs:27, 70, 88, 136, 151`). Today the alternative is
   `ModelTestOrder_Compact_DetailView`, registered through the builder's `LayoutRegistry.AddDetailView`
   (`ModelTestTypes.cs:134-146`). With one DetailView those `.First(...)` calls throw before they test anything, so the
   replacement declares a second DetailView in the same XAFML difference.

**The mechanism [R2].** `CreateApplicationModel(..., ModelStoreBase.Empty)` passes that argument as the *user* store
(`DesignerModelFactory.cs:377, 390`), so it is the wrong hook for a baseline. The fixture already has the right one:
`CreateModelApplication([… "SharedDiff", shared … "UserDiff", ModelStoreBase.Empty])`, the layered form
`ModelEditorLayeredValueTests.cs:92` already uses. The two replacements go into that shared layer — XAF's own mechanism,
no builder reference, and no `ModelNodesGenerator` subclass.

This reproduces **what the cited assertions read**, not the builder's whole column baseline **[R2]**: the real spec also
sets `Customer.City` and leaves unlisted columns at index `-1` (`ModelTestTypes.cs:63`, `ListViewColumnsUpdater.cs:124`).
The replacement declares only the order, the hidden `SyncToken` and the second DetailView. The XML itself is written in
step 2 and is not part of this plan, so its correctness is proven by the tests going green, not asserted here.

## 5. The new gate

**Corrected boundaries [R1].** The block at `Program.cs` lines 34-**63** (not 59) is the *comment inventory*; the
executable editor assertions are **lines 642-1236**, between the E2E 8 assertion that ends at 642 and `FREEZE-001` at
1237. That block moves.

- **Nine editor-only helpers move [R2]**, all outside the range the first draft named: `LogOff` (1448), `OpenComboList`
  (1509), `PickComboItem` (1521), `SetCulture` (1529), `OpenOrderFromOrderList` (1539), `DropFailSaveConstraint` (1601),
  `CloseModelEditor` (1659), the Edit Model helper (1671-1690), and **`SaveModelEditorAndWaitForReload` (1691-1696)**,
  which the first revision missed although the merge step calls it at line 837 — without it the new gate does not
  compile. **`BuilderExpression` (1699) stays** — it is the builder's round-trip helper, and "1671-1700" wrongly swept
  it up.
- **Shared infrastructure is copied, not moved:** `Step`, `Assert`, `Login`, `OpenListView`, `GridHeaders`, `Sql`,
  `SqlScalar`/`Retry`, `WaitForNoLoading`, `ClosePopup`, `NewPage`, the process and startup plumbing, and the `adminId`
  initialisation. Both gates need them.
- Sample module: `Customer`, `Order`, `OrderLine` copied **without** their `*.Layout.cs` partials, plus the `Updater.cs`
  seeding and roles (`adminRole.CanEditModel = true`, line 97, is MODELEDITOR-001's own line, and the User read
  permission block at line 118 goes with it). Not copied: `BrokenLayouts.cs`, `GateFixtures.cs`, `SampleViews.cs`,
  `Model.DesignedDiffs.xafml`, `ServiceOrder`, `OrderAttachment`. **Dropping them leaves dangling references** at
  `Order.cs:24`, `SampleDbContext.cs:24`, `Updater.cs:73, 120` **[R1]** and, missed there, the
  `<None Remove="Model.DesignedDiffs.xafml" />` and `<EmbeddedResource Include="Model.DesignedDiffs.xafml" />` lines at
  `XafLayoutBuilder.Sample.Module.csproj:14, 17` **[R2]** — a copied project referencing a file that was not copied does
  not build. The copy fixes all six up.
- Sample host: the five wiring lines stay as they are, with the new namespace and the new catalog.
- The gate keeps its current discipline: port :5100, refuses to start if the port serves, clears Admin's
  `ModelDifferences` before it starts, restarts the host around those writes, and the MODELEDITOR-016 step's temporary
  check constraint (`CK_XLB_FailSave`, renamed `CK_XME_FailSave`), which lives in the gate's own SQL.
- **Five scenarios need more than a renamed caption**, and each gets its fixture in the new sample: the lookup scenario
  needs a second Order DetailView; the layout-designer scenario expects the `Live group caption` an earlier *builder*
  step installs; the criteria-reset scenario expects four orders including a `ServiceOrder`; the column-deletion
  scenario asserts `!deletedHeaders.Contains("Notes")` (`Program.cs:1084, 1091`), which holds only if the new sample's
  baseline has no `Notes` column at all, where today the builder's spec is what leaves it out **[R2]**; and the Modules
  tab waits on `tr[data-module='XafLayoutBuilderModule']` (`Program.cs:833`), a module the new sample will not load —
  retarget it at the editor's own module or the new sample module, or the step times out **[R2]**.

## 6. Order of work

Each step ends green before the next starts; nothing is deleted here until the new repository is green.

0. **Freeze the baseline [R1].** Record the source SHA and the moving-file inventory in this plan before any copy, so the
   two trees cannot drift mid-move. Reconcile the five cards in Review first (they are all confirmed Done as of
   2026-09-20), so the history closes on this board.
1. **New repository, add-on only.** Create `MBrekhof/XafModelEditor`, copy the 10 add-on files, rename the namespace and
   `InternalsVisibleTo`, rewrite the `Directory.Build.props` metadata, `dotnet build` clean.
2. **Tests.** Copy the 12 files, write the trimmed fixture, the six types and the XAFML store of section 4, strip
   `WarmedUpModelTests`' builder import and `FailFastOnLayoutErrors`. Red first where the fixture changed behaviour, then
   `dotnet test` with all 133 green.
3. **Sample host and module.** Copy and strip per section 5, fix the four dangling references, point the connection
   string at `XafModelEditor.Sample`; run it by hand once and open Edit Model.
4. **Gate.** Move lines 642-1236, the nine editor helpers and the editor half of the startup cleanup (lines 111-115:
   the MODELEDITOR-010 shared-record delete and the `DropFailSaveConstraint` call; the Admin cleanup at 108-110 stays
   and is copied) **[R2]**, copy the shared infrastructure, build the five scenario fixtures, run to exit 0, retake
   screenshots 13-17 from that run.
5. **Docs.** `docs/api-notes.md` gets the "Runtime Model Editor" section (lines 352-732, boundary confirmed by Codex) and
   nothing else; `model-editor-scope.md` and `devexpress-support-request.md` move whole with their repro paths rewritten
   to `XafModelEditor.Tests/...`; the six model-editor plans move (`2026-09-14-appear-001.md` stays here). README and
   CHANGELOG written fresh. Push.
6. **Remove from this repo, last**, after a delta check against the step-0 SHA — recording a SHA does not itself stop
   drift when steps 1-5 ran in between **[R2]**. Delete the add-on project, the 12 test files, the gate block, the nine
   editor helpers and the editor half of the startup cleanup, the editor's docs, screenshots 13-17, and the host wiring.
   Also, each missed by the first draft **[R1]**: the `ProjectReference` at `XafLayoutBuilder.Tests.csproj:19`; the
   `CanEditModel` grant (`Updater.cs:97`) and the User read-permission block (`Updater.cs:118`); both plain types at
   `ModelTestTypes.cs:221-231`, which would otherwise sit dead; the project line in `XafLayoutBuilder.slnx`; and the
   documentation lines at README 77, 86, 107, 299, CHANGELOG 9 and 19, `CLAUDE.md:27` and `SESSION_HANDOFF.md:16`, plus
   `SESSION_HANDOFF.md:47, 76, 107, 122` and `CHANGELOG.md:20, 26` **[R2]**. `docs/how-it-works.md`, `docs/cases.md` and
   `skills/xaf-layout-builder/SKILL.md` need no change — Codex checked both rounds. `StoredAspectCleanup` has no
   retained caller, so nothing replaces it here. Then `dotnet build`, 157 tests green, gate exit 0, README and CHANGELOG
   saying where the editor went.
7. **Board.** New ContextBoard project for XafModelEditor; MODELEDITOR-012 closes here citing both repositories' commits.
   The support request item list becomes a card on the new board.

## 7. Risks

- **The gate is the long pole.** ~600 lines of assertions, several multi-logon (shared differences seen by User at the
  next logon, the failed-save step, the merge steps), plus five scenario fixtures to rebuild. Budget the day here.
- **Two repositories now pin DevExpress 26.1.4.** A version move has to happen in both; the note is in each
  `Directory.Build.props`.
- **Two gates, two LocalDB catalogs.** Section 3's catalog rename is what keeps them apart; if a step is ever run with
  the old connection string, it clears the other repository's model differences. **The rename does not buy runtime
  isolation [R2]:** both gates hardcode port 5100 (`Program.cs:103`) and each refuses to start if the port serves, so
  the two repositories' gates must still be run one at a time.
- **Screenshots 13-17** are referenced from this repo's README; step 6 removes those lines rather than leaving dead
  image links.
- **"157 green" is arithmetic, not a run** (290 − 133; 129 methods + 4 theory cases). Step 6 proves it.

## 8. Frozen baseline (step 0, 2026-09-20)

Source tree **`db22fcab7ad3fbaacbc5bfbef9dd7c76ec007558`**. Every file and line range below is read at that commit; this
plan's own commit touches `docs/plans/` only, so it does not move the baseline. Step 6 re-checks this list against the
tree before deleting anything.

**Add-on, 10 files** — `XafLayoutBuilder.ModelEditor/`: `IsolatedRender.cs`, `ModelEditing.cs`,
`ModelEditorComponent.razor`, `ModelEditorController.cs`, `ModelEditorPropertyEditor.cs`, `SharedModel.cs`,
`StoredAspectCleanup.cs`, `XafModelEditorModule.cs`, `_Imports.razor`, `XafLayoutBuilder.ModelEditor.csproj`.

**Tests, 12 files, 133 cases** — `XafLayoutBuilder.Tests/`: `ModelEditingTests.cs`, `ModelEditorLayeredValueTests.cs`,
`ModelEditorLocalizationTests.cs`, `ModelEditorLookupTests.cs`, `ModelEditorMergeTests.cs`, `ModelEditorNodeTests.cs`,
`ModelEditorSharedModelTests.cs`, `ModelEditorSpecialEditorsTests.cs`, `ModelEditorTreeTests.cs`,
`ModelEditorValidationTests.cs`, `StoredAspectCleanupTests.cs`, `WarmedUpModelTests.cs`.

**Gate** — `XafLayoutBuilder.E2ETests/Program.cs`: comment inventory 34-63; startup cleanup 111-115; assertions
642-1236; helpers 1448, 1509, 1521, 1529, 1539, 1601, 1659, 1671-1690, 1691-1696.

**Docs** — `docs/api-notes.md` 352-732; `docs/model-editor-scope.md`; `docs/devexpress-support-request.md`;
`docs/plans/` 2026-09-13-modeleditor-007, 2026-09-19-modeleditor-009, -011, -014, -015, -016;
`docs/screenshots/` 13-model-editor.png, 14-model-editor-translate.png, 15-model-editor-shared.png,
16-model-editor-shared-as-user.png, 17-model-editor-validation.png.

**Owner's decision, 2026-09-20:** DevExpress is contacted later and the private-repository access question is settled
then; step 5 writes the `XafModelEditor.Tests/` paths as planned.

## 9. For the owner, before step 7

**DXSUPPORT-001's send order.** Fifteen of the sixteen items cite repro paths in `XafLayoutBuilder.Tests/`, which stop
existing at step 6 (item 14 needs none: "the target layer cannot be obtained with public API", line 186) **[R2]**.
Sending the request before the move puts paths in a DevExpress ticket that go stale within the day. Recommendation: do
the move first, then send the request with `XafModelEditor.Tests/` paths, so an engineer following a repro lands in the
repository that still has it.

**Settled by the owner 2026-09-20:** DevExpress is contacted later and the access question dealt with then. (Was: the support request sends DevExpress repro paths
into a repository they will not be able to open. Either the request quotes the test code inline instead of citing paths,
or the repository is public by the time it is sent. Worth deciding before step 5 writes those paths.
