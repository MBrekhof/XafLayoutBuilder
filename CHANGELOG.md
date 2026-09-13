# Changelog

One line per change, newest first. The version is `PackageVersion` in `Directory.Build.props`, the
version the packages on the local feed carry. Ids are ContextBoard cards; the detail is in
`SESSION_HANDOFF.md`.

## Unreleased

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
