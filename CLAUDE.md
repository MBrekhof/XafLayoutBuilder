# CLAUDE.md

Instructions for working in this repo. `README.md` says what it is, `XafLayoutBuilder-START.md` is
the original design and session plan (all seven sessions done), `SESSION_HANDOFF.md` says where
things stand and what is open.

## Project overview

DevExpress XAF 26.1 Blazor Server POC (.NET 10, EF Core 10, SQL Server LocalDB): a fluent C#
builder declares a business class's DetailView layout and ListView columns, generator updaters
pour that into the Application Model as generated-layer defaults, and an admin action exports any
view's current layout back to the same fluent C#.

- `XafLayoutBuilder.Core`: builders, `LayoutSpec` records, JSON, `CSharpLayoutPrinter`. **No
  DevExpress dependency.** Keep it that way; XafMergerTool and code generators reuse it.
- `XafLayoutBuilder.Module`: `XafLayoutBuilderModule`, `DetailViewLayoutUpdater`,
  `ListViewColumnsUpdater`, `LayoutRegistry` + resolver, `LayoutStartupCheck`, `LayoutExporter`,
  `ExportLayoutController`. References `DevExpress.ExpressApp` and `DevExpress.Persistent.Base`
  only (platform neutral).
- `XafLayoutBuilder.Blazor`: optional add-on, `XafLayoutBuilderBlazorModule` +
  `CopyLayoutCodeController` (clipboard through `IXafJSRuntime`). Everything browser-specific goes
  here so the Module stays platform neutral.
- `XafLayoutBuilder.Sample.Module`: `Customer` (+ `Customer.Layout.cs`, a detail layout that opts
  into the catch-all group, and columns), `Order`
  (+ `Order.Layout.cs`, the start document's section 4 example verbatim), `OrderLine` (+
  `OrderLine.Layout.cs`, columns that also shape Order's Lines tab, VIEW-001), `SampleViews` (a compact
  Order DetailView and ListView declared in code, registered by the host, VIEW-001),
  `OrderAttachment`, `ServiceOrder : Order` (with `OriginalOrder` for the lookup test),
  `BrokenLayouts` (startup-failure fixture), seeding in `DatabaseUpdate/Updater.cs`.
- `XafLayoutBuilder.Sample.Blazor.Server`: template host. `--break-layout` registers the broken
  fixture; `XafLayoutBuilder:EnableExport` in appsettings.Development.json enables the export.
- `XafLayoutBuilder.Tests`: xUnit against Core, the Module's resolver and registry (the Module grants
  `InternalsVisibleTo`), and the updaters and exporter against an Application Model built in-process
  (`ApplicationModelFixture`, the Model Editor's `DesignerModelFactory` path, no host or database). Test
  business classes there must be top-level public `[DomainComponent]` classes, or XAF gives them no views.
  Rendering, the user layer and the Blazor actions are covered by the E2E gate.
- `XafLayoutBuilder.E2ETests`: console app, C# Playwright, the phase gate. Its file header lists
  every assertion.

## Build / test / E2E

```bash
dotnet build XafLayoutBuilder.slnx
dotnet test XafLayoutBuilder.Tests
dotnet run --project XafLayoutBuilder.E2ETests     # builds + starts the sample on :5100, asserts, exits 0/1/2
dotnet run --project XafLayoutBuilder.Sample.Blazor.Server   # manual: http://localhost:5000, Admin / empty password
dotnet pack XafLayoutBuilder.slnx -c Release -o artifacts/packages                       # Core, Module, Blazor
dotnet nuget push "artifacts/packages/*.nupkg" --source C:\Projects\local-nuget         # local feed (backslashes)
```

LocalDB catalog `XafLayoutBuilder.Sample` on `(localdb)\mssqllocaldb`. Debug builds auto-update the
schema on startup, no debugger needed. The gate writes and deletes Admin's `ModelDifferences`
rows and restarts the host around those writes (XAF Blazor's deferred user-model save would
overwrite them otherwise); it clears any left-over user model before it starts, because a run that
aborts midway would otherwise make the next run's round trip compare against a user layout; and it
refuses to start if :5100 is already serving.

Playwright 1.49 uses `chromium-1148`; if the gate exits 2, run
`pwsh XafLayoutBuilder.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium`.

## Non-negotiables (start document section 10, plus what the sessions added)

- **DevExpress 26.1 only**, at the version in `Directory.Build.props` (`DevExpressVersion`, 26.1.4;
  the packages accept `[26.1.4,26.2)`). Never mix in 25.2 packages, never float the version again.
- **Package versions:** `PackageVersion` in `Directory.Build.props`, never `Version` (XAF records the
  module's assembly version in the database and refuses a lower one). Bump it before each push to
  the local feed `C:\Projects\local-nuget`.
- **Version and changelog:** SemVer, 0.x while a proof of concept; the version is `PackageVersion`.
  Every commit that changes behaviour adds a one-line entry, card id first, under `## Unreleased` in
  `CHANGELOG.md`. A feed push bumps `PackageVersion` (minor for features, patch for fixes only) and
  renames `Unreleased` to that version with the date. At session end, README's "Latest changes"
  lists that session's lines; the detail stays in `SESSION_HANDOFF.md`.
- **EF Core only, never XPO.**
- **Verify every DevExpress API claim** in dxdocs or the installed source at
  `C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp`. Findings go into
  `docs/api-notes.md` with file and line.
- **No `ModelNodesGenerator` subclasses, updaters only.** If something seems to need a generator,
  stop and write why in `BACKBURNER.md` (does not exist yet; nothing has needed one).
- **No XAFML for `Order`** in the sample module. The builder is its only layout source.
- **Builder API changes need a sentence in `skills/xaf-layout-builder/SKILL.md` and a test.**
- Every change ends with `dotnet build`, unit tests green, and the E2E gate at exit 0.
- No environment variables for configuration: switches go in appsettings or on the command line.
- E2E selectors: wait on seeded text or bound input values, never on DevExpress grid CSS class
  names. Scope assertions to the active tab panel; inactive tabs stay in the DOM.

## Task state

Board-only: open work lives on ContextBoard, project **XafLayoutBuilder** (id 32). Never create a
`TODO.md` or `DOCS/DONE.md`. `SESSION_HANDOFF.md` keeps the prose (where things stand, decisions).
Remote: https://github.com/MBrekhof/XafLayoutBuilder, **public** — no assistant attribution in
commits, pull requests or issues.
