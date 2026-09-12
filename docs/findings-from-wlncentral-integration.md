# Findings from putting XafLayoutBuilder into a production application

Recorded 2026-09-12, from vendoring the proof of concept (at `a88680a`) into **WLNCentral** — an XAF
26.1.4 / .NET 10 / EF Core 10 LIMS with a Blazor Server host, a WinForms client, Hangfire jobs and
~220 entities. One entity was converted (`JobDefinition`); nothing was migrated wholesale.

Three adversarial review passes by Codex against that integration surfaced the items below. They are
recorded here because they are **upstream's**, not the integration's: every one of them reproduces in
this repository as written. Line references are to `a88680a`; symbol names are given too, since the
integration's copies have drifted.

Nothing here has been run against a live application — the integrating machine had no local
`appsettings.json`, so the whole set is source-verified, not runtime-verified. Each item says what
was checked.

---

## 1. P1 — the updaters mutate before they validate, so a failure leaves a half-applied layout

`DetailViewLayoutUpdater.UpdateNode` removes the generated layout tree (`foreach (var element in
layout.ToList()) element.Remove();`, ~line 28), *then* rebuilds — discovering XLB001 partway through
`Add(...)` (~line 68) and XLB002 only at the end (~line 42). `ListViewColumnsUpdater` likewise writes
columns and reindexes before `AddColumn` can throw XLB003 (~line 79).

**Why it matters more than it looks.** In the POC the exception propagates and you see it. In an
application that catches it — see item 5 — XAF has already marked generation `Done` (the generator's
`finally` clears `IsNodesGeneratorInProgress`; `ModelNode.cs:445`, `:2219` in the installed 26.1
source). The view then renders from a **partially replaced layout**, silently missing whatever came
after the failure. On a data-entry form that is a field nobody is asked to fill in.

**Fix applied downstream:** both checks read only the spec and the view's `Items`, so they can run
first. Validate, then delete and rebuild — the updater becomes all-or-nothing, and a rejected spec
leaves XAF's own generated layout intact (ungrouped, complete, safe).

*Verified by source inspection of the updaters and the installed DevExpress generation lifecycle.*

## 2. P1 — `LayoutSpecResolver` does not unwrap `TargetInvocationException`

`LayoutSpecResolver.Invoke` calls the spec factories through `MethodInfo.Invoke` (`LayoutRegistry.cs`
~line 56). A factory that throws `LayoutSpecException` — placing the same member twice, for instance,
which `LayoutBuilder` itself raises — comes back wrapped in `TargetInvocationException`.

Every `catch (LayoutSpecException)` upstream of that point therefore misses it, and the exception
escapes to whatever is driving the model build.

**Fix applied downstream:** catch `TargetInvocationException` and rethrow the inner exception with
`ExceptionDispatchInfo.Capture(ex.InnerException).Throw()`.

*Verified by source inspection.*

## 3. P2 — the startup check aborts on the first failure

`LayoutStartupCheck.Check` walks `BOModel` and throws on the first bad view, so the remaining types
are never examined. One broken layout hides every other broken layout, and you fix them one
application start at a time.

It matters more once a consumer can swallow the exception (item 5): the run is then recorded as
completed in `Completed` while most views were never actually checked.

**Fix applied downstream:** one attempt per view, accumulating messages, then a single
`LayoutSpecException` naming every broken view. (Per *view*, not per class — a failing DetailView
should not skip its own ListView and lookup.)

*Verified by source inspection.*

## 4. P2 — raw specs bypass the builder's structural checks

`DetailLayoutSpec` is a public record, `LayoutRegistry.Register` accepts a hand-built one, and `with`
can reshape any spec. The fluent builder rejects these shapes; the raw path does not, and
`LayoutRegistry` only checks that members exist:

- **A member both placed and hidden.** `Members()` minus `HiddenMembers` — the natural way to compute
  "what is placed" — then omits it from validation while the rebuild still places it. If it has no
  `Items` entry, XLB001 fires *after* the delete (item 1).
- **Two sibling nodes sharing an id**, including an `UnplacedMembers.AppendToGroup("Other")` catch-all
  colliding with a real root group called `Other`. That surfaces as XAF's own
  `DuplicateModelNodeIdException` (`ModelNode.cs:481-490`), which is *not* a `LayoutSpecException`, so
  no handler anywhere expects it.

**Fix applied downstream:** a `PlacedMembers()` helper that walks the tree (never subtraction), plus
two new diagnostics — a member both placed and hidden, and duplicate sibling ids including the
catch-all collision — both checked before any mutation.

*Verified by source inspection; the duplicate-node behaviour against the installed 26.1 source.*

## 5. Design gap — fail-fast at startup can stop a whole host, with no way to opt out

`XafLayoutBuilderModule.Setup` subscribes to `SetupComplete` unconditionally and the check throws.
For the sample that is exactly right. In a real host it is not a screen-level failure:

- A Blazor host with compatibility 26.1+ runs XAF's model warm-up, which awaits `Setup()` in a hosted
  service and rethrows; the host then fails to start. **Every user is down because of one layout
  typo, in code that was deployed and reviewed.**
- WinForms calls `Setup()` before `Start()`, so the client simply never opens.

The README's "fail-fast startup" is a feature — but production wants the *diagnosis* without the
*outage*. Suggested upstream shape: a `FailFastOnLayoutErrors` switch (default `true` in DEBUG,
`false` in RELEASE) where the disabled path logs and continues. **That is only safe once item 1 is
fixed**, otherwise continuing means rendering the half-applied layout. Downstream also widened the
degraded catch to any exception, on the grounding that "a layout problem must never take the host
down" cannot be guaranteed by enumerating exception types.

*Host behaviour verified against the installed DevExpress warm-up path and the consuming
application's `Program.cs`; the resulting outage is inferred from that path, not executed.*

## 6. The XLB001 message blames the wrong attribute

> `XLB001 {view.Id}: member '{member}' has no Items entry. Is it [Browsable(false)] or
> [VisibleInDetailView(false)]?`

`[VisibleInDetailView(false)]` does **not** remove the `Items` entry. XAF's items generator never
reads that flag — only the layout generator does (`ModelDetailViewLayoutNodesGenerator.cs:242` reads
`IsVisibleInDetailView`; `ModelDetailViewNodesGenerator.cs` does not). So such a member has an editor
and can legitimately be placed by a layout; only `[Browsable(false)]` produces XLB001.

Worth fixing in the message, and worth stating in the skill: the two attributes are not
interchangeable, and a tool that treats them as equivalent will reject valid layouts. (Downstream had
exactly that bug in a guard test before this was checked.)

*Verified in the installed DevExpress 26.1 source.*

## 7. README overclaims what survives conversion

> "**Ordinary XAF layering.** The builder output is the generated layer, so module XAFML,
> administrators and users can still customise on top, and their changes win."

True of *precedence*, misleading about *survival*. The updater replaces the generated tree with
different group paths, and an override works by path: a stored difference aimed at stock
`Main/SimpleEditors/...` no longer resolves once the layout is `Main/Header/...`. Higher precedence
cannot preserve a node that no longer exists.

So converting a view people have already personalised **can lose their layout customisations**, and
this applies to module XAFML exactly as it does to user differences. What does survive is a higher
layer that *defines* a node rather than targeting one (`IsNewNode`, and `FreezeLayout`, which copies
the layout up into that layer).

Worth a sentence in the README and in the migration checklist: check `ModelDifference` for a view
before converting it.

*Verified against `ModelNode.cs` merge behaviour and the stock layout generator's group names.*

## 8. Consumer guidance — do not vendor this into an existing module's assembly

Not an upstream defect, but the first thing that bit the integration, and worth a line in "Use it in
your own solution".

`ModuleBase` scans **its own assembly** for `ModuleUpdater` implementations and for `.xafml`
resources. Dropping `XafLayoutBuilderModule` into an application's existing module assembly therefore
makes it rediscover that application's own database updaters — anything with an
`(IObjectSpace, Version)` constructor — and XAF concatenates every eligible module's updater list
without deduplicating. In the integration that would have re-run the user and role seeders on the
next database update. The same scan re-reads the application's model resources as a second diffs
store.

Keeping the module in its own assembly (as this repository does) avoids all of it. The recommendation
is simply: reference the projects, or vendor them as their own project — never as a folder inside an
existing module.

*Verified: `ModuleBase.cs:207-213`, `DatabaseUpdater.cs:100-108` in the installed 26.1 source, against
the consuming application's updater constructors.*

---

## Not in scope here

The integration did not exercise: localised captions, ListView bands, layout composition across a
class hierarchy, the Blazor clipboard/download add-on, or "Export Layout To Code" at runtime. The
existing limitations in the README still stand as written.
