# Changelog

One line per change, newest first. The version is `PackageVersion` in `Directory.Build.props`, the
version the packages on the feed carry. Ids are ContextBoard cards; the detail is in
`SESSION_HANDOFF.md`.

## Unreleased

- MODELEDITOR-009: Model Editor Differences (the node's differences as XML, per language), Modules (the loaded modules), Generate content for a view added in the editor, and Merge to shared, which moves a node's saved differences from the user's own record into the shared one; a node's bold mark and Reset node now work when the shared differences hold the node too.
- MODELEDITOR-010: Edit Shared Model edits the administrator differences in a model of their own and saves them through the shared store; the module layers that store over every circuit's model at logon, so a shared edit shows to everyone at their next page load without a restart; Reload discards pending edits; closing with edits warns once. The host sets `XafModelEditorModule.SharedDifferences`.
- MODELEDITOR-008: Model Editor languages: a language combo reads and writes localizable values per aspect without touching the circuit's culture, Add makes a new language, and Translate lists every localizable value under the node with its translation and an untranslated filter.

## 0.3.0 (2026-09-15)

- PKG-003: `System.Security.Cryptography.Xml` pinned to 10.0.12 in Blazor, ModelEditor and the sample module; DevExpress.ExpressApp.Blazor pulled in 9.0.0 with eight high-severity advisories (NU1903).
- MODELEDITOR-003 (review): the Model Editor's buttons and inputs are DevExpress Blazor components, matching the rest of XAF Blazor; no stray "Model" caption beside the editor; long values wrap instead of pushing the editors out of view, the node's path and actions stay at the top while its values scroll, and View in Model opens at the top.
- APPEAR-001: `XafLayoutBuilder.Appearance` add-on: conditional appearance rules in the builder (`AppearanceBuilder<T>`, `ISupportAppearanceRules`, `AppearanceRegistry`) with checked targets, startup diagnostics XLB006-008, JSON and export.
- GROUP-001: `Column(..., groupIndex:)` and `.GroupPanel()` set a ListView's default grouping and show its group panel; the export keeps both.
- MODELEDITOR-013: Model Editor tree nodes show the Visual Studio Model Editor's icons; an unknown image name no longer shows a broken image preview.
- PKG-002: Core, Module and Blazor 0.2.0 published as private packages on GitHub Packages (`https://nuget.pkg.github.com/MBrekhof/index.json`), the feed from now on.

- MODELEDITOR-007: Model Editor validation: required and key values are checked against pending edits, including required resets on saved user-created nodes; Save lists missing values and warns about unusable stored differences.
- MODELEDITOR-006: Model Editor special editors: a filter builder for criteria values, text areas for expressions and multiline strings, image names with a preview.
- MODELEDITOR-005: Model Editor drop-downs for references and types, field and language suggestions, Go to, Source, Back/Forward and View in Model.
- MODELEDITOR-004: Model Editor adds, clones, deletes, moves and resets nodes; added nodes need their required values and are removed again unless saved.
- MODELEDITOR-003: Model Editor tree in the WinForms order with captions, modified marks and search; values grouped by category with descriptions.
- MODELEDITOR-002: Model Editor Save reloads the page and blanks emptied stored aspects, so a saved Reset shows at once and stays; warmed-up model testable in-process.
- MODELEDITOR-001 (spike): `XafLayoutBuilder.ModelEditor` add-on, "Edit Model" in XAF Blazor: model tree, value grid, saved to the user's differences.
- RECHECK-001: a repeated startup check still reports a layout that failed XLB001-003 the first time.
- HIER-001: a derived class starts from its base's layout and columns with `Extend<Base>()` and adds with `InGroup(...)`.
- LOGIN-001: the gate's login commits the user name before clicking Log In (XAF's editor posts on lost focus).
- BAND-001: `.Band(id, b => ..., caption: ...)` puts a band header over adjacent columns (one level, as XAF Blazor renders).
- E2E4-001: the gate drags Order Date in XAF's Blazor layout editor instead of writing the user-layer XAFML.
- VIEW-001: views declared in code (`LayoutRegistry.AddDetailView<T>` / `AddListView<T>`) get their own layout or columns.
- VIEW-001: a type's columns spec also shapes its nested ListViews, without the back-reference to the owner.
- CACHE-001: XAF's model cache is WinForms-only; a Blazor host always runs the updaters.

## 0.2.0 (2026-09-13)

- EXPORT-001: exported ListViews print `.Hide(...)` only for columns the spec or a later layer hid.
- SORT-001: `Column(..., sortIndex:)` sets sort priority independently of column order, and the export keeps it.
- MODEL-001: the updaters and the exporter are unit tested against an Application Model built in-process.
- NEST-001: columns may follow references, `Column(x => x.Customer.City)`, and export that way.
- JSON-001: "Export Layout To JSON", "Download Layout JSON" and `LayoutRegistry.RegisterJson<T>`.
- DIFF-001: a stored difference aimed at a replaced stock layout path is ignored, then deleted at the user's next save.
- FREEZE-001: the gate proves an administrator's frozen column set keeps a later-added column hidden.
- TEST-001: the gate covers a spec factory that throws a non-layout exception.
- CASE-001: `docs/cases.md`, the intake route for layout cases from generated applications.

## 0.1.0 (2026-09-13)

- PKG-001: Core, Module and Blazor published as NuGet packages to the local feed, DevExpress `[26.1.4,26.2)`.
- GATE-001: the degraded-mode gate step fails when a rejected spec leaves a partial layout.
- REG-001: `Register` only stores, specs are checked at resolution; `Register<T>(Func, Func)` takes factories.
- RESOLVE-002: a type's detail and columns specs resolve independently.
- CHECK-002: `FailFastOnLayoutErrors`, default off: a broken layout is logged and the view keeps XAF's own layout.
- CHECK-001: the startup check reports every broken view in one run.
- DIAG-001: XLB001 names the attributes that actually remove the view item.
- APPLY-001: the updaters check the whole spec before they change the model.
- SPEC-001: hand-built, `with`-reshaped and JSON specs get the builder's structural checks.
- RESOLVE-001: spec factory exceptions reach callers unwrapped.
- DOCS-001, DOCS-002: converting a customised view drops its differences; keep the module in its own assembly.
- Codex review: add-on actions no longer `async void`, catch-all id collisions caught, export keeps `.Unplaced(...)`.
- "Download Layout File" in the Blazor add-on.
- `XafLayoutBuilder.Blazor` add-on with "Copy Layout To Clipboard"; `.Unplaced(...)` opts out of XLB002.
- MIT license.
- Session 7b: the export emits the namespace and valid C#, and exports the view it runs on.
- Session 7: README, how-it-works, SKILL.md, screenshots, round-trip gate step.
- Session 6: `LayoutExporter`, `CSharpLayoutPrinter` and "Export Layout To Code".
- Session 5: the startup check forces every view with a spec, so XLB001 to XLB004 surface at startup.
- Session 4, 4b: `ListViewColumnsUpdater` including the lookup ListView, column order through `GeneratedIndex`.
- Session 3, 3b: `DetailViewLayoutUpdater`, `LayoutRegistry` and `ISupportViewLayoutCustomization` discovery.
- Session 2: `LayoutBuilder<T>`, `ListViewColumnsBuilder<T>`, spec records, validation and JSON.
- Session 1: solution skeleton, sample module, Blazor host and E2E harness.
