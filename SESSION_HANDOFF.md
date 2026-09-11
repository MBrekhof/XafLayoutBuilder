# Session handoff

Updated 2026-09-11 (after session 3). Session plan: `XafLayoutBuilder-START.md` section 9.

## Where the plan stands

| Session | Status |
|---|---|
| 1. Solution skeleton, sample module, Blazor host, E2E harness logs in | **done 2026-09-11** |
| 2. Core: builder, spec records, validation, unit tests | **done 2026-09-11** |
| 3. `DetailViewLayoutUpdater` + E2E 1 | **done 2026-09-11** |
| 4. `ListViewColumnsUpdater` incl. lookup + E2E 2–3 | next |
| 5. Registry, interface discovery, startup diagnostics | |
| 6. Exporter, printer, popup + E2E 4–6 | |
| 7. SKILL.md, README, docs, screenshots | |

## Session 3 result

- Section 7 verified against dxdocs + installed 26.1 source; everything recorded in
  `docs/api-notes.md` (generator names, updater signature, layout interfaces, columns model,
  model-cache caveat, column Index handling for session 4).
- `XafLayoutBuilder.Module`: `DetailViewLayoutUpdater` (registered in `AddGeneratorUpdaters`),
  `LayoutRegistry.Register<T>(detail, columns)`, internal `LayoutSpecResolver` (registry first,
  then `ISupportViewLayoutCustomization` via the interface map, cached per type). Session 5 now
  only owes the startup diagnostics polish and the "break a member name" test; discovery exists.
- Sample `Order` implements `ISupportViewLayoutCustomization` in `Order.Layout.cs` with the
  section 4 layout verbatim (`BuildListViewColumns` returns null until session 4).
- E2E 1 passes: SyncToken absent from the form, groups in builder order, Notes inside a
  collapsible group (header has the toggle button), Header group captioned but not collapsible.
  Screenshot `e2e-03-order-detailview.png`, DOM dump `e2e-03-order-detailview.html`.

### Decisions taken in session 3 (confirm or reverse)

- **XLB002, strict by default:** a visible member that is neither placed nor hidden makes the
  updater throw at startup, naming the members. Rationale: a new property must not silently
  vanish from the DetailView. Alternative: append unplaced members to a trailing group.
- **Derived classes keep XAF's default layout.** `ServiceOrder` inherits `Order`'s static
  interface implementation, but the resolver only applies a spec whose `TypeName` matches the
  exact type. Hierarchy composition is phase 2 per the start document.
- **`Collapsible()` forces the caption on** (Blazor renders the toggle in the header). A group
  without an explicit caption and with one item shows that item's caption as header (XAF default).
- Unit-level coverage of the applier is nil: `ModelNode` cannot be built outside an XAF
  application, so the E2E is the only test of `DetailViewLayoutUpdater`.

## Session 2 result

- `XafLayoutBuilder.Core` (still zero DevExpress references): `LayoutSpec.cs` (records, enums,
  `LayoutSpecException`, `ISupportViewLayoutCustomization`), `LayoutBuilder<T>` with
  `GroupBuilder<T>` / `TabsBuilder<T>`, `ListViewColumnsBuilder<T>`, `LayoutSpecJson`.
- Build-time rules from section 4 all throw `LayoutSpecException`: item placed twice, item placed
  and hidden, group id reused (checked across nesting and tabs), non-simple member lambda
  (rejected at call time, not at Build). Column equivalents: listed twice, listed and hidden,
  `Lookup()` inside `Lookup()`.
- 16 xUnit tests: the section 4 example for both builders, every rule, JSON round trip with
  `$type` discriminators (`group` / `tabs` / `item`) and enums as strings.
- Beyond section 4, and therefore owed a SKILL.md sentence in session 7: `GroupBuilder.Group`
  and `GroupBuilder.Tabs` (nesting, needed for real exported layouts), `GroupBuilder.RelativeSize`,
  `GroupBuilder.Image`, `TabsBuilder.Tab(id, g => ...)` for a tab with arbitrary content,
  `Column(..., caption:)`. `TabFor` = a tab group whose id is the member name holding one item.
- Codex review of session 1 found one gate weakness: the E2E accepted any process serving
  :5100. Fixed: the harness now refuses to start if :5100 already answers, and gives up as soon
  as its own host process exits.

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
