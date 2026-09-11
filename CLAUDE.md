# CLAUDE.md

Instructions for working in this repo. `XafLayoutBuilder-START.md` is the design document
and session plan; read it first. `SESSION_HANDOFF.md` says where the session plan stands.

## Project overview

DevExpress XAF 26.1 Blazor Server POC (.NET 10, EF Core 10, SQL Server LocalDB): a fluent C#
builder declares a business class's DetailView layout and ListView columns, generator
updaters pour that into the Application Model as generated-layer defaults, and an admin
action exports any view's current layout back to the same fluent C#.

- `XafLayoutBuilder.Core` — builder, `LayoutSpec` records, C# printer. **No DevExpress
  dependency.** Keep it that way; BPG and XafMergerTool reuse it.
- `XafLayoutBuilder.Module` — `XafLayoutBuilderModule`, generator updaters, registry,
  export controller. References `DevExpress.ExpressApp` only (platform-neutral).
- `XafLayoutBuilder.Sample.Module` — `Customer`, `Order` (+ `OrderLine`, `OrderAttachment`),
  `ServiceOrder : Order`, seeding in `DatabaseUpdate/Updater.cs`.
- `XafLayoutBuilder.Sample.Blazor.Server` — template host, port 5000/5001 from launchSettings.
- `XafLayoutBuilder.Tests` — xUnit against Core.
- `XafLayoutBuilder.E2ETests` — console app, C# Playwright, the phase gate.

## Build / test / E2E

```bash
dotnet build XafLayoutBuilder.slnx
dotnet test XafLayoutBuilder.Tests
dotnet run --project XafLayoutBuilder.E2ETests     # builds + starts the sample on :5100, asserts, exits 0/1/2
dotnet run --project XafLayoutBuilder.Sample.Blazor.Server   # manual: http://localhost:5000
```

LocalDB catalog `XafLayoutBuilder.Sample` on `(localdb)\mssqllocaldb`. DEBUG builds
auto-update the schema on startup (no debugger needed — the E2E harness relies on it).
Login: `Admin`, empty password (DEBUG-only seeding).

Playwright 1.49 uses `chromium-1148`; if the E2E exits 2, run
`pwsh XafLayoutBuilder.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium`.

## Non-negotiables (from the start document, section 10)

- **DevExpress 26.1.\* only.** Never mix in 25.2 packages.
- **EF Core only, never XPO.**
- **Verify every DevExpress API claim** in dxdocs (`devexpress_docs_search` /
  `devexpress_docs_get_content`) or the installed source at
  `C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp`. Findings go
  into `docs/api-notes.md`.
- **No `ModelNodesGenerator` subclasses — updaters only.** If something seems to need a
  generator, stop and write why in `BACKBURNER.md`.
- **No XAFML for `Order`** in the sample module beyond what the template ships. The builder
  is its only layout source.
- **Builder API stays at section 4 of the start document.** A new method needs a sentence
  in `skills/xaf-layout-builder/SKILL.md` and a test, or it doesn't go in.
- Every session ends with `dotnet build`, unit tests green, and the E2E gate at its
  current expected level. Don't advance the session plan on a red gate.
- E2E selectors: wait on seeded text or bound input values, never on DevExpress grid CSS
  class names (`.dxbl-grid-data-row` does not exist in 26.1).

## Task state

No `TODO.md` — open items live in `SESSION_HANDOFF.md` until a ContextBoard project exists.
