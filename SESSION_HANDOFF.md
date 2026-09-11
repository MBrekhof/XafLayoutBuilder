# Session handoff

Updated 2026-09-11. Session plan: `XafLayoutBuilder-START.md` section 9.

## Where the plan stands

| Session | Status |
|---|---|
| 1. Solution skeleton, sample module, Blazor host, E2E harness logs in | **done 2026-09-11** |
| 2. Core: builder, spec records, validation, unit tests | next |
| 3. `DetailViewLayoutUpdater` + E2E 1 | |
| 4. `ListViewColumnsUpdater` incl. lookup + E2E 2–3 | |
| 5. Registry, interface discovery, startup diagnostics | |
| 6. Exporter, printer, popup + E2E 4–6 | |
| 7. SKILL.md, README, docs, screenshots | |

## Session 1 result

- Solution from the `dx.xaf` 26.1 template (Blazor, EF Core, SQL Server, password auth, no
  extra modules), retargeted to net10.0 / EF Core 10.0.11 like XafReportScheduler.
- Sample entities: `Customer`, `Order` with `Number, Customer, OrderDate, Notes, SyncToken,
  Lines, Attachments` (the exact members section 4 of the start document uses),
  `OrderLine`, `OrderAttachment`, `ServiceOrder : Order`. Seeded: 2 customers, ORD-001..003,
  SRV-001, one line each.
- `XafLayoutBuilderModule` is an empty `ModuleBase`, already required by `SampleModule`, so
  session 3 only has to add `AddGeneratorUpdaters`.
- E2E gate (exit 0): build, start on :5100, log in as Admin, Order ListView shows ORD-001 and
  SRV-001, ORD-001 DetailView binds with the default XAF layout (SyncToken still visible).
  Screenshots land in `XafLayoutBuilder.E2ETests/bin/Debug/net10.0/screenshots/`.
- Unit tests: one placeholder test so `dotnet test` is green; session 2 replaces it.

## Gotchas found

- .NET 10 SDK creates `.slnx`, not `.sln`; the E2E `FindRepoRoot` looks for `*.slnx`.
- `.dxbl-grid-data-row` (the grid row class XafReportScheduler's harness used) does not match
  in 26.1. Wait on seeded text instead.
- After navigating to `Order_DetailView`, `NetworkIdle` fires while the ListView is still
  rendered. Wait for an `input` whose value is the seeded key before reading the DOM.
- The template's `DatabaseVersionMismatch` handler only auto-updates with a debugger attached;
  changed to `#if EASYTEST || DEBUG` so the headless E2E run updates the schema.

## Open points

- No git remote yet. Create a private repo under `MBrekhof` when the owner says so.
- Section 7 of the start document (verify 26.1 generator/model API names via dxdocs) is
  still to do; it is the first step of session 3. Record findings in `docs/api-notes.md`.
- `XafLayoutBuilder.Core` currently has no DevExpress reference at all. The start document
  allows `DevExpress.ExpressApp` for `IModel*` interfaces; add it only if session 6's exporter
  needs the spec side to see model types (it should not — the exporter lives in Module).
