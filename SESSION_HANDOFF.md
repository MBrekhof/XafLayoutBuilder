# Session handoff

Updated 2026-09-13 (board loop over the limitation cards). Session plan: `XafLayoutBuilder-START.md`
section 9.

**State: the POC is complete.** All seven sessions are done, `dotnet build` is clean, 98 unit tests
pass, and the E2E gate exits 0 with every assertion from section 8 plus the round trip, the
startup-failure check and the degraded-mode check. The repository is public on GitHub, MIT licensed.
Open work lives on ContextBoard, project **XafLayoutBuilder** (id 32).

## Board loop 2026-09-13

Owner's instruction: publish to the local feed, work the open cards with a Codex review after each fix
(fix, re-review), then the known limitations (cards first, then implementing them). Every card went test
first, red-checked, build, unit tests, E2E gate, Codex working-tree review, commit with exact files.

| Card | Commit | What changed |
|---|---|---|
| FREEZE-001 | 0eb1349 | Gate proves an administrator's `FreezeColumnIndices` keeps a later column hidden (application-level diff store fixture). A freeze in a user's own differences has no effect in XAF Blazor. |
| DIFF-001 | 9323f72 | A stored difference aimed at a stock layout path the builder replaced is ignored (unusable node) and deleted from the database user store at that user's next save (Log Off, reload). Converting a customised view discards its customisations. |
| JSON-001 | b2bca44 | `LayoutSpecs` JSON document per type, "Export Layout To JSON" and "Download Layout JSON", `LayoutRegistry.RegisterJson<T>` (each half read on its own). |
| NEST-001 | 87824e1 | Columns may follow references: `Column(x => x.Customer.City)`; exporter and printer handle them. |
| MODEL-001 | 1ed98ac | The updaters and the exporter are unit tested against an Application Model built in-process with `DesignerModelFactory` (`ApplicationModelFixture`). Test types must be top-level public `[DomainComponent]` classes. |
| SORT-001 | fbc0f33 | `Column(..., sortIndex:)` sets sort priority independently of column order; exported only when it differs. Codex found a column grouped in the Blazor grid exported `sortIndex: -1` (the grid keeps its SortOrder, SortIndex -1); the exporter now ranks priority the way DxGrid sorts, grouped columns first. Grouping itself is not exported. |
| EXPORT-001 | 6df25b9 | The updater stamps columns the spec hides; the exporter leaves out only generated leftovers (unstamped, `GeneratedIndex` -1), so never-mentioned columns are no longer printed as `.Hide`. Codex: a later-layer-added hidden column must stay (fixed); both bool markers are lost with XAF's model cache on, **deferred to CACHE-001 by the owner** (card body updated). |

**Versioning (09caaaa):** `PackageVersion` is the version (0.1.0 on the feed), `CHANGELOG.md` holds one
line per change under `Unreleased` until the next feed push, README's "Latest changes" lists the latest
session. The rules are in CLAUDE.md.

**Nothing is pushed.** Local commits since the last push: 5291bbb, fabc233, 49382a1, 0eb1349, 9323f72,
b2bca44, 87824e1, 1ed98ac, 5122e01, 09caaaa, fbc0f33, 85be610, 6df25b9.

Next cards, in order: HIER-001 (1670), VIEW-001 (1671), BAND-001 (1672), CACHE-001 (1673),
E2E4-001 (1674). Not autonomous: SEC-001, REL-001, the BPG LAYOUT cards.

Open question for the owner: with `FailFastOnLayoutErrors` on, a non-layout exception from a spec factory
ends the startup check at once instead of joining its aggregated report (TEST-001 pins that behaviour).

The two "no test"/"not established" items further down are resolved: TEST-001 (49382a1) and DIFF-001
(9323f72).

**Environment, 2026-09-13:** each Codex plugin review leaves an MCP server set running under the repo's
broker (auto-memory `gate-and-codex-sequential`). The mcpRoslyn session's broker reached 181 processes
(8.5 GB), one Codex review here was killed for low memory, and the owner reboots after mcpRoslyn's bug-fix
session. Run the gate and a Codex review one after the other, never together.

## BPG references XafLayoutBuilder (decided 2026-09-13)

XafLayoutBuilder stays the proof of concept for the fluent technique. BPG (the XAF app generator,
C:\Projects\BPG, skeleton C:\Projects\BPGDemo, generated app e.g. C:\Projects\driver) will
**reference** it rather than re-implement it: one copy of the technique, because WLNCentral's vendored
copy drifted and its fixes had to come back by hand. BPG also becomes a source of realistic layout
cases for this repo.

Cards, with board dependencies in this order:

- This board: **PKG-001** (#1655) publish Core, Module and Blazor as NuGet packages with DevExpress
  pinned to 26.1.4 (the repo uses `26.1.*`; BPGDemo pins 26.1.4). The feed is an open owner decision:
  private GitHub Packages recommended, nuget.org only after the "Xaf" naming ticket (SEC-001).
  **CASE-001** (#1656) the intake route for cases found in BPG-generated apps.
- bpg board: **LAYOUT-001** (#1657) skeleton references the packages → **LAYOUT-002** (#1658)
  generator emits `{Entity}.Layout.cs` through Core, replacing `{Entity}Views.cs` (which sets
  `IModelColumn.Index` directly) → **LAYOUT-003** (#1659) structured layout in the spec replacing
  `detailViewLayoutHint` and **LAYOUT-004** (#1660) pipeline gate with fail-fast on →
  **LAYOUT-005** (#1661, Backlog) running-app layout back into the spec.
- BPG still file-syncs TODO.md: a session in the BPG repo must cite the LAYOUT cards there. Nothing
  in BPG, BPGDemo or Driver was changed from this repo.

## Final Codex review follow-up (2026-09-12)

A final Codex adversarial review of the whole findings round (cc42981..ed6637a) returned
"needs-attention" with three findings. Each became a card and went through the same loop: test
first, build, unit tests, E2E gate, Codex working-tree review, commit. All three are in Review.

| Card | Commit | What changed |
|---|---|---|
| RESOLVE-002 | 4f1353f | The resolver caches a type's detail and columns specs separately, and the startup check resolves each view's spec inside that view's own attempt, so a broken DetailView factory no longer hides or costs the same type's ListView and lookup. |
| REG-001 | fe7d9db | **Owner's option (a).** `LayoutRegistry.Register` only stores; a new `Register<T>(Func, Func)` overload takes factories. Validation and the member check run at resolution, inside the updaters and the startup check, so `FailFastOnLayoutErrors` governs a broken registration. The `--break-layout` fixture registers Order with a detail factory whose `Build()` throws plus XLB003 columns, reported together with fail-fast on and logged with it off. |
| GATE-001 | fc85459 | The degraded-mode gate step now carries canaries only a partially applied spec can produce: a "Broken layout" group caption on Customer and a "Broken number" column caption on Order. Proven by running the gate against a simulated APPLY-001 regression one updater at a time: red on each canary, while the older assertions passed. |

Still open after this round:

- **No test covers a spec factory that throws a non-layout exception** (an `InvalidOperationException`,
  say) with fail-fast off. REG-001's fixture throws a `LayoutSpecException`, which takes the other branch
  of the startup check's catch.
- **Not established:** whether XAF ignores or renders a stored difference whose node path the builder no
  longer generates.

## Findings from the WLNCentral integration, fixed (2026-09-12)

`docs/findings-from-wlncentral-integration.md` (cc42981) recorded eight defects found while vendoring the
POC into WLNCentral. Each became a card on the board, was fixed test first, and passed build, unit tests,
the E2E gate and a Codex working-tree review before its commit. All are in Review, waiting for the owner's
Confirm Done. Nothing has been pushed.

| Card | Commit | What changed |
|---|---|---|
| RESOLVE-001 | 7ec9aa3 | Spec factories invoked with `BindingFlags.DoNotWrapExceptions`, so their `LayoutSpecException` reaches callers unwrapped. Tests now reference the Module (`InternalsVisibleTo`). |
| SPEC-001 | ddd1d5b | Structural rules moved into `LayoutSpecChecks.Validate` for detail and column specs, called by both `Build()`s, `LayoutRegistry.Register` and the resolver, so hand-built, `with`-reshaped and JSON specs are checked too. |
| APPLY-001 | c1f921d | Both updaters check everything before they change the model. XLB001/XLB002 live in the pure `LayoutSpecChecks.CheckAgainstView`; XAF marks a node generated even when an updater throws, so the old order left half-applied layouts. |
| CHECK-001 | 79211e9 | The startup check attempts every view and reports all failures in one exception. `--break-layout` now breaks two views (XLB001 + XLB003). |
| DIAG-001 | f2c5047 | XLB001 names `[Browsable(false)]`, `[HideInUI]` and the owner reference; `[VisibleInDetailView(false)]` never removes the view item. |
| CHECK-002 | b133c4b | `XafLayoutBuilderModule.FailFastOnLayoutErrors`, **default off (owner's decision)**: a broken layout is logged to `eXpressAppFramework.log` and the view keeps XAF's own layout; any exception degrades. The sample turns it on in appsettings.Development.json; a new gate step runs the broken fixture with it off. |
| DOCS-001 | 9c9f785 | README and SKILL.md: converting a customised view does not carry its differences over (they target stock node paths); a `FreezeLayout` copy does. |
| DOCS-002 | 90dcdcf | README and SKILL.md: keep the module in its own assembly; `ModuleBase` scans its own assembly for updaters and model resources. |
| DOCS-003 | fece878 | This handoff no longer says the export has no copy button. |

Left open from this round:

- **No test covers a spec factory that throws a non-layout exception during the startup check**, the
  CHECK-002 degraded path Codex flagged and that was fixed. It would need a sample fixture type whose
  factory throws.
- **Not established:** whether XAF ignores or renders a stored difference whose node path the builder no
  longer generates. DOCS-001 claims only that it is not carried over.
- **Backlog on the board, not autonomous work:** SEC-001 (public-repo second look, owner decision),
  REL-001 (xafskills and XafMergerTool, other repositories), and the phase 2 candidates card (CARD-1640).

## Where the plan stands

| Session | Status |
|---|---|
| 1. Solution skeleton, sample module, Blazor host, E2E harness logs in | **done 2026-09-11** |
| 2. Core: builder, spec records, validation, unit tests | **done 2026-09-11** |
| 3. `DetailViewLayoutUpdater` + E2E 1 | **done 2026-09-11** |
| 4. `ListViewColumnsUpdater` incl. lookup + E2E 2–3 | **done 2026-09-11** |
| 5. Registry, interface discovery, startup diagnostics | **done 2026-09-11** |
| 6. Exporter, printer, popup + E2E 4–6 | **done 2026-09-11** |
| 7. SKILL.md, README, docs, screenshots | **done 2026-09-11** |

## XAFLogicExplainer against the sample module (2026-09-12)

Ran `xaflogic extract` against a scratchpad copy of `XafLayoutBuilder.Sample.Module`, to see what a
syntax-only XAF documentation tool makes of a layout that lives in C# instead of XAFML. The copy was
deliberate: `extract` writes `.xaflogic-output` beside the project and has no output flag. The tool
itself is at `C:/Projects/XAFLogicExplainer`.

It reported EF Core, seven entities, no controllers, one XAFML file, and **one customised view**,
which is `ApplicationUser_ListView` from the XAF template. Neither `Order` nor `Customer` appears in
its Model Editor section at all. For every sample view its Screens section prints "generated by XAF,
exists in no file", `Order_DetailView` included, whose layout is fully specified in `Order.Layout.cs`
down to the tab captions and the relative sizes.

That is the evidence for the post. A tool treating XAFML as the source of truth for layout goes blind
the moment the layout moves into C#, and it goes blind silently rather than with an error. The same
move is what makes the layout legible to a language model, which needs no XAFML parser and no
DevExpress licence to read `Order.Layout.cs`.

Two defects are worth proposing upstream, neither of them specific to this project:

- **The base type of a partial class is misread.** Both sample entities come back with
  `ISupportViewLayoutCustomization` as their base type. The real base is `BaseObject`, declared in
  `Customer.cs:9` and `Order.cs:13`. The cause is at `EntityAnalyzer.cs:1105`, which picks the
  primary part with `group.Find(part => part.BaseTypes.Count > 0)` on the assumption that only one
  part declares a base list. When a second partial declares nothing but an interface, the winner is
  whichever part the file enumeration listed first, and `Customer.Layout.cs` sorts ahead of
  `Customer.cs` because uppercase L precedes lowercase c. The repair at line 1125 cannot fire,
  because it only replaces a base type of `object` or empty. Any XAF codebase pairing a hand-written
  `Foo.cs : BaseObject` with a generated `Foo.Generated.cs : IXafEntityObject` hits this, silently,
  with the outcome depending on file names. The fixture shape already exists there as `Shipment.cs`
  plus `Shipment.Generated.cs` in the legacy solution fixture.
- **"exists in no file" over-claims.** `MarkdownDocumentationGenerator.cs:587` renders that phrase in
  both languages when all it establishes is that no XAFML node defines the view. Honest wording is
  that no Model Editor customisation was found. One string, and it stops the over-claim for every
  code-first layout scheme rather than only this one.

Not worth proposing: teaching the extractor to read fluent layout builders. That is bespoke support
for a scheme with one user, and its CONTRIBUTING asks for the reduced pattern rather than special
cases.

If this is ever picked up, `origin` there is `peopleworks/XAFLogicExplainer`, a third-party upstream,
with the `MBrekhof` fork as `fork`. So it is a branch on the fork and a pull request upstream, with
no assistant attribution in the commits or the pull request body. It belongs in a session rooted in
that repository, which carries its own instructions and build gate. Nothing was changed there; the
run was read-only.

## Codex review of the review points (2026-09-12)

Codex found five issues in that batch; all five are fixed and the gate is green through E2E 9.

- **`async void` action handlers.** XAF runs an Execute handler synchronously and considers the
  action done when it returns, so a denied clipboard, a failed module import or a dead circuit
  escaped XAF's error handling. Both add-on actions now share
  `LayoutCodeActionController`: one task that never throws, a busy flag so a second click cannot
  start a second run, `JSDisconnectedException` and cancellation swallowed deliberately, anything
  else reported through `ShowMessage` (itself guarded, since reporting needs the circuit too).
- **Catch-all id collisions.** `Build()` only compared the `Unplaced` group id with other group
  ids, so a root-level *item* with the same name passed validation and then failed inside XAF with
  a duplicate sibling id. It now checks every root node id. Two tests cover it, including the case
  that stays legal (a member of the same name nested inside a group).
- **The startup memory was too coarse.** Keyed only by application type, it suppressed the check
  for a later application whose registered specs had changed. The key now includes a registry
  version that `LayoutRegistry` bumps on every change, the run happens under a lock so "once" holds
  under concurrent starts, and `LayoutStartupCheck.Reset()` exists for tests.
- **The export dropped `.Unplaced(...)`.** Exporting a class that opted in printed its catch-all
  group as ordinary items, so adopting that file silently restored the strict rule. The applier now
  stamps the group it generates and the exporter turns it back into the policy call, leaving its
  members out of the hidden list. E2E 5a asserts Customer's export equals `Customer.Layout.cs`.
- **The pre-run cleanup broke a fresh machine.** It queried the sample database before the app had
  ever created it. It now treats "no catalog" and "no such table" as first run and carries on,
  while still surfacing real connection and permission faults.

## Review points (2026-09-12)

Four points from the owner's read of the finished repository, all applied:

- **The copy button, and the download.** `XafLayoutBuilder.Blazor` is a new optional add-on: one
  module and two controllers. Copy uses `navigator.clipboard.writeText` through XAF's
  `IXafJSRuntime` with no JavaScript file; Download ships one small JS module in the add-on's
  `wwwroot`, because a server-side action cannot start a browser download and Chrome blocks
  top-level `data:` navigation. The gate downloads the file and compares it with the popup text.
  The Module stays platform neutral. The button could not go inside the export popup after all:
  XAF Blazor's popup for a non-persistent object renders only its own OK and Cancel, so the action
  sits next to the export in the Tools tab. Both paths share `LayoutCodePrinter.ForView`, and the
  gate asserts the clipboard holds exactly what the popup shows.
- **Startup check once per process.** A static set of application types that already passed, so
  the forced generation no longer repeats for every Blazor circuit. Only a completed run counts.
- **XLB002 opt-in.** `.Unplaced(UnplacedMembers.AppendToGroup("Other"))` collects whatever a layout
  does not mention into one captioned group at the end of the form. Strict stays the default and
  the docs say why. The sample's `Customer` uses it, and the gate asserts City lands there.
- **GeneratedIndex comment.** The updater now names the E2E assertion that catches a DevExpress
  rename, so whoever hits it knows where to look.

Two harness fixes fell out of this: the gate clears Admin's left-over user model before starting
(an aborted run used to poison the next round trip), and the clipboard comparison ignores line
endings and the timestamped comment.

## Session 7b result (Codex final review follow-up, 2026-09-12)

Codex confirmed all four earlier findings closed, and raised eleven new ones. Seven were real
defects in the new exporter and startup check; all seven are fixed:

- **The export omitted the namespace.** The printed partial class declared a different type from
  the business class, so pasting it did not compile. `CSharpLayoutPrinter.PrintClass` now takes the
  namespace and emits a file-scoped `namespace`; the controller passes `type.Namespace`.
- **The printer could emit invalid C#.** Captions are now escaped for newlines, tabs and control
  characters, a member whose name is a C# keyword gets the `@` prefix, and an empty group or tab
  set prints `_ => { }` instead of `g => g`, which is an expression and does not convert to
  `Action<GroupBuilder<T>>`.
- **Export could turn a group caption off.** A caption equal to XAF's computed default was dropped,
  but the applier only shows a caption for a captioned, collapsible or tab group, so a plain group
  lost its header on the way back. The exporter now prints such a caption unless the group is
  collapsible or a tab.
- **Export silently dropped a customised root group.** The sole root group is only unwrapped when it
  is the stock plain `Main`; anything else is exported as an ordinary group.
- **The action exported the default views, not the one in front of the user.** It now exports the
  view it runs on and fills the other half from the defaults, and the printed comment names all
  three view ids.
- **Model node ids were printed as member names.** Both halves now read `PropertyName`, and a member
  bound to a nested path such as `Customer.Name` is skipped with a comment instead of printed as
  code that would not compile or would throw.
- **The startup check followed configurable default views.** It now resolves the same fixed
  `{Type}_...` ids the updaters handle, and a missing lookup view with a lookup spec is XLB004
  rather than silently skipped.

Two weak assertions in the gate were tightened, which is how the namespace bug would have been
caught: the round trip now also asserts the namespace, the class header and the view-ids comment,
code is compared line by line instead of with all whitespace removed (squashing also ignored
differences inside string literals), and E2E 5 asserts OrderDate inside the Details block rather
than anywhere after it. Four printer unit tests were added, 36 in total.

Two findings were answered with documentation rather than code, deliberately:

- **A hidden column loses its sort order.** Keeping it would mean either a new builder argument or
  leaving the stock display-member sort in place, which is what the applier clears on purpose. The
  README and the skill now say so.
- **A host without a security system shows the action to every user.** Such an application has no
  roles to ask, and the debugger or `EnableExport` condition still gates it. Documented in the
  controller, the README and the skill.

Codex also confirmed clean: the popup's ObjectSpace ownership, the `EnableExport` static for a
single-host process, `NodeCount` as the forcing mechanism, index ordering, the `TabFor` heuristic
and invariant number formatting.

## Session 7 result

- `README.md` rewritten: what it is, the section 4 example, how to run, how to adopt it, the
  diagnostics table, screenshots, and a limitations section that lists every gap honestly
  (scope decisions, export verbosity, engineering caveats).
- `docs/how-it-works.md`: the design for XAF developers. Layer table, both updaters, discovery,
  startup check, exporter and printer, the user layer in XAF Blazor, testing, and a decisions log
  that says which choices came from the owner and which from the Codex reviews.
- `skills/xaf-layout-builder/SKILL.md` finalised: setup, full surface, build-time rules,
  diagnostics, the "when you change a business class" checklist, and the export. Two agent traps
  are called out: the `{Type}.Layout.cs` partial must import only `XafLayoutBuilder.Core`
  (`FlowDirection` and `ColumnSortOrder` also live in DevExpress namespaces), and a property added
  to a class with a layout must be placed or hidden.
- `docs/screenshots/` holds seven pictures from a real gate run, referenced by the README. The
  `.gitignore` rule `**/screenshots/` was removed; it would have hidden them (the E2E's own
  screenshots are under `bin/`, already ignored).
- `CLAUDE.md` updated to the finished repository.
- **New in the gate, E2E 5a:** exporting the untouched layout reproduces `Order.Layout.cs` modulo
  whitespace, and every column the source hides is hidden in the export. This is the start
  document's section 6 round-trip property, which section 8 wanted as a unit test; the exporter
  needs a live Application Model, so it is asserted here instead.
- **Bug found by that check:** column captions were exported through `HasValue`, which misses
  localizable values (the same trap already fixed for group captions). A column caption was
  silently dropped. Fixed by comparing with the member caption XAF falls back to, and covered by a
  new sample spec: `Customer.Layout.cs` has `.Column(x => x.Name, caption: "Customer name")`, and
  E2E 5a asserts both the rendered header and the round trip.

## Open points

- **Public on GitHub:** https://github.com/MBrekhof/XafLayoutBuilder, owner account `MBrekhof`,
  MIT licensed. The commit messages were rewritten before the first push to drop the assistant
  attribution trailers and session links, so that history differs from any older local clone.
  Worth a second look now that it is public: the sample carries the XAF template's demo
  `UrlSigningKey` in `appsettings.json` and seeds `Admin` with an empty password in Debug builds,
  and the repository name contains "Xaf", which is the subject of an open DevExpress ticket about
  naming and publishing terms for a sibling repository.
- **Codex has now reviewed every session.** The final review of 2026-09-12 confirmed the session 4b
  fixes and raised eleven items on sessions 5 to 7; see "Session 7b result" for what was changed and
  what was answered with documentation.
- **Release step (start document section 11):** copy `skills/xaf-layout-builder/SKILL.md` into
  `xafskills`, and consider the `XafMergerTool` "export as builder C#" option that references
  `XafLayoutBuilder.Core`.
- **Phase 2 candidates**, unchanged from the start document plus what the sessions added:
  hierarchy composition (`Extend<TBase>()`), ListView bands, nested member paths, localised
  captions through message keys, a `spec.json` loader so a generator can ship layouts as data; and from
  here: an in-process XAF Application Model in the unit tests, so the updaters and the exporter can
  be tested without the E2E; driving the Blazor layout editor's drag and drop in E2E 4; exercising
  `FreezeColumnIndices`, which is currently reasoned from source only.
- **Known gaps kept as limitations** (all in the README): copying and downloading are separate
  actions in the optional `XafLayoutBuilder.Blazor` add-on rather than buttons inside the export
  popup (XAF Blazor renders only OK and Cancel there), only the default views are handled, and only XAF 26.1.4 Blazor with EF Core and
  LocalDB was tested.
- **XAFLogicExplainer reported nothing of the builder layouts**, and two upstream defects
  turned up while checking why; see the 2026-09-12 section above for the cause, the file and
  line of each, and what a pull request would have to respect. Nothing in that repository was
  changed.
- **Task state is on ContextBoard** (project XafLayoutBuilder, id 32), board-only: no TODO.md. The
  open points above are mirrored there as SEC-001, REL-001 and the phase 2 candidates card.

## Session 6 result

- `CSharpLayoutPrinter` (Core): `PrintDetail`, `PrintColumns`, `PrintClass`. The section 4 example
  is a fixed point: builder -> spec -> print reproduces its own source byte for byte (tests).
- `LayoutExporter` (Module): merged model -> spec for the DetailView (root "Main" unwrapped, skipped
  non-editor items reported) and for ListView + lookup. Only explicitly stored values are exported
  (`HasValue`), except captions, which are localizable and are compared with XAF's default rule
  instead (see api-notes; the column half of that was fixed in session 7).
- `ExportLayoutController` (Module): "Export Layout To Code" in the Tools category on any object
  view; active for administrators (`ISecurityUserWithRoles` + `IPermissionPolicyRole.IsAdministrative`,
  hence the new `DevExpress.Persistent.Base` reference) and only with a debugger or
  `XafLayoutBuilderModule.EnableExport`. The sample host sets it from
  `XafLayoutBuilder:EnableExport` in appsettings.Development.json. The popup is a DetailView of the
  non-persistent `LayoutCode` (one unlimited string). **No copy button**: that needs Blazor JS
  interop, which the platform-neutral Module cannot host; select-all/copy in the memo does the job.
- `LayoutBuilder<T>.Item(...)` at root level added so exported layouts with root items compile.
- E2E 4-6 pass. **Deviation from section 8:** E2E 4 does not drive the Blazor layout editor (drag
  and drop only); it writes the user-layer XAFML XAF's editor would persist, with the host restarted
  around the write because XAF Blazor's deferred user-model save otherwise overwrites the row. E2E 5
  exports and asserts OrderDate under Details; E2E 6 deletes the rows, restarts, and asserts the
  builder layout is back.
- Exported columns list every unshown column as `.Hide(...)` except the key. Hidden and
  unmentioned are the same thing in the model; documented in SKILL.md.

## Session 5 result

- Registry and interface discovery were already in place since session 3; this session added
  the missing piece: `LayoutStartupCheck.Run` (hooked to `XafApplication.SetupComplete` in
  `XafLayoutBuilderModule.Setup`) enumerates `Model.BOModel`, and for every type with a spec
  touches `DefaultDetailView.Layout`, `DefaultListView.Columns` and, with a lookup spec,
  `DefaultLookupListView.Columns`. Touching `NodeCount` makes XAF generate the nodes, which runs
  the updaters, which throw XLB001-003. XLB004 if a spec'd type has no default view.
- **In XAF Blazor this is a real process-level startup failure.** The template host builds the
  application (and its model) while the host starts, so a broken layout throws from
  `OnSetupComplete` as an unhandled exception and the process exits before listening. Verified by
  hand and by the gate.
- Fixture: `Customer.InternalCode` (`[Browsable(false)]`) and `BrokenLayouts.Register()` in the
  sample module, wired to the host's `--break-layout` argument (a command-line switch, not an
  environment variable). The gate's last step starts the host with it and asserts: process exits
  non-zero, nothing serves on :5100, output contains `XLB001 Customer_DetailView ... 'InternalCode'`.

## Session 4b result (Codex review follow-up, 2026-09-11)

- Column index now goes through the generator's own mechanism (`GeneratedIndex` value + cleared
  `Index`), so `IModelListView.FreezeColumnIndices` set by an admin keeps later-added listed
  columns hidden, exactly as for stock columns. api-notes revised accordingly.
- Hidden members without a stock column (typical in the lookup view) now get a column with index
  -1, so the chooser can offer them.
- Spec immutability closed for real: list properties freeze in their `init` accessors (covers the
  constructor, JSON and `with { }`), and `Frozen()` always copies, so a caller's
  `ReadOnlyCollection` over a mutable list is not aliased. Two tests added (28 total).
- E2E 1 asserts the tabbed group holds exactly Lines, Attachments in that order. E2E 2 now fails
  when the Column Chooser menu item is missing instead of logging and moving on.
- api-notes corrected: the lookup generator tests `IsVisibleInLookupListView` (not
  `VisibleInListView`) and falls back to the full column set when nothing was generated.
- `skills/xaf-layout-builder/SKILL.md` drafted with the current surface.

## Session 4 result

- `ListViewColumnsUpdater` (registered after the detail updater): applies the spec to
  `{Type}_ListView`, and to `{Type}_LookupListView` when the spec has `.Lookup(...)`. Listed
  columns get their order, `Width`, `Caption`, `SortOrder`/`SortIndex`; every other stock column
  (hidden or just unmentioned) is not shown and its default sort cleared. Unlike the DetailView
  there is no XLB002 equivalent: an unmentioned member is still reachable through the column
  chooser, nothing vanishes.
- **Found by the fail-fast on the first run:** XAF's lookup ListView only generates columns for
  the display property and members visible in lookups, so `Customer` had no column in
  `Order_LookupListView`. The updater now adds a missing column the way the stock generator does
  (`AddNode<IModelColumn>` + `PropertyName`); XLB003 remains for collections and unknown members.
- Sample: `ServiceOrder.OriginalOrder` (reference to `Order`) added so E2E 3 has a lookup editor
  that targets `Order_LookupListView`; the aggregated `OrderLine.Order` back-reference is hidden
  by XAF in the nested detail, so it could not serve. Seeded for SRV-001 on a fresh database only.
- E2E 2 passes: headers Number, Customer, Order Date; rows ORD-003, ORD-001, SRV-001, ORD-002
  (OrderDate descending); header context menu → Column Chooser lists Sync Token (and ID, Notes).
- E2E 3 passes: SRV-001 detail → Original Order editor → edit mode → dropdown shows a grid whose
  header row is exactly Number, Customer.
- The session's original Index decision (set `IModelColumn.Index` directly) was **superseded by
  session 4b** after the Codex review; see there and `docs/api-notes.md`.

## Session 3b result (Codex review follow-up, 2026-09-11)

- XLB002 strictness **confirmed by the owner**: unplaced-and-unhidden members fail at startup.
- Fixed from the review: a group and an item with the same id under one parent now fail at
  `Build()` (XAF sibling-uniqueness; parent/child reuse as in `TabFor` stays legal); spec lists
  are frozen copies (`ReadOnlyCollection`), so a registered spec cannot be mutated through a cast,
  and deserialised specs are frozen too; `LayoutRegistry.Register<T>` throws when the spec names
  a member `T` lacks; tests for group `RelativeSize`/`Image`, nested `Tabs`, `Tab(id, ...)`,
  column captions, `Members()`; E2E 1 now also asserts no SyncToken element exists and that the
  three top-level nodes are Header, Details, Tabs by DOM inspection.
- Carried to session 5 and done there: XAF generates view nodes lazily, so XLB001/XLB002 fired on
  first open of the DetailView rather than at application start.

## Session 3 result

- Section 7 verified against dxdocs + installed 26.1 source; everything recorded in
  `docs/api-notes.md` (generator names, updater signature, layout interfaces, columns model,
  model-cache caveat, column Index handling for session 4).
- `XafLayoutBuilder.Module`: `DetailViewLayoutUpdater` (registered in `AddGeneratorUpdaters`),
  `LayoutRegistry.Register<T>(detail, columns)`, internal `LayoutSpecResolver` (registry first,
  then `ISupportViewLayoutCustomization` via the interface map, cached per type).
- Sample `Order` implements `ISupportViewLayoutCustomization` in `Order.Layout.cs` with the
  section 4 layout verbatim.
- E2E 1 passes: SyncToken absent from the form, groups in builder order, Notes inside a
  collapsible group (header has the toggle button), Header group captioned but not collapsible.

### Decisions taken in session 3

- **XLB002, strict by default:** a visible member that is neither placed nor hidden makes the
  updater throw at startup, naming the members. Confirmed by the owner in session 3b.
- **Derived classes keep XAF's default layout.** `ServiceOrder` inherits `Order`'s static
  interface implementation, but the resolver only applies a spec whose `TypeName` matches the
  exact type. Hierarchy composition is phase 2 per the start document.
- **`Collapsible()` forces the caption on** (Blazor renders the toggle in the header). A group
  without an explicit caption and with one item shows that item's caption as header (XAF default).
- Unit-level coverage of the applier is nil: `ModelNode` cannot be built outside an XAF
  application, so the E2E is the only test of the updaters (and, since session 6, the exporter).

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
- Methods beyond section 4 (nested `Group`/`Tabs`, `RelativeSize`, `Image`, `Tab(id, ...)`,
  `Column(..., caption:)`) are documented in the skill and tested since session 3b.
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
- E2E gate (exit 0): build, start on :5100, log in as Admin, Order ListView shows ORD-001 and
  SRV-001, ORD-001 DetailView binds with the default XAF layout.
- Unit tests: one placeholder test so `dotnet test` is green; session 2 replaced it.

## Gotchas found along the way

- .NET 10 SDK creates `.slnx`, not `.sln`; the E2E `FindRepoRoot` looks for `*.slnx`.
- `.dxbl-grid-data-row` (the grid row class XafReportScheduler's harness used) does not match
  in 26.1. Wait on seeded text instead.
- After navigating to `Order_DetailView`, `NetworkIdle` fires while the ListView is still
  rendered. Wait for an `input` whose value is the seeded key before reading the DOM.
- The template's `DatabaseVersionMismatch` handler only auto-updates with a debugger attached;
  changed to `#if EASYTEST || DEBUG` so the headless E2E run updates the schema.
- Grid header cells carry the filter button's accessibility text; strip "No filter applied" before
  comparing captions.
- The login form occasionally rejects the first fill ("The user name must not be empty") when the
  Blazor circuit is still connecting; the harness verifies the bound value before submitting and
  the failure has not recurred since.
- LocalDB can hand back a dead named pipe on the first query after the gate kills the host ("The
  pipe has been ended"). The harness turns connection pooling off and retries a failed query once.
- .NET's command-line configuration reads `--key value`, so a bare switch such as `--break-layout`
  swallows the next argument as its value. Put configuration overrides
  (`--XafLayoutBuilder:FailFastOnLayoutErrors=false`) before it.
- XAF traces to `eXpressAppFramework.log` beside the host executable, a file that grows across runs;
  a check that reads it must search only what its own run appended.
- mcpRoslyn does not pick up a `.csproj` change made during a session. After the Tests project gained
  a ProjectReference to the Module, `find_references` missed the test-project callers until
  `reload_workspace` was run (Tests went from 1 to 2 project references and from 11 to 14 documents).
  Reported to the mcpRoslyn maintainers. Run `reload_workspace` after any project-file change.
- Everything else XAF-specific is in `docs/api-notes.md` with file and line references.
