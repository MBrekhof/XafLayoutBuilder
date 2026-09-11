# XafLayoutBuilder

Typed, compile-checked C# for DevExpress XAF view layouts, poured into the Application Model as
generated-layer defaults. The running Blazor app stays the visual designer. The source of truth
becomes a fluent builder next to the business class instead of `Model.xafml`.

**Status: proof of concept, complete against its start document** (`XafLayoutBuilder-START.md`).
Built and tested with DevExpress XAF 26.1.4, .NET 10, EF Core 10, SQL Server LocalDB and Blazor
Server. One subject (the sample `Order`), one end-to-end gate, and the limitations listed below.

## What it looks like

```csharp
public partial class Order : ISupportViewLayoutCustomization
{
    public static DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<Order>.Create()
            .Group("Header", g => g
                .Caption("Order")
                .Flow(FlowDirection.Horizontal)
                .Item(x => x.Number)
                .Item(x => x.Customer)
                .Item(x => x.OrderDate))
            .Group("Details", g => g
                .Collapsible()
                .Item(x => x.Notes, relativeSize: 100))
            .Tabs("Tabs", t => t
                .TabFor(x => x.Lines, imageName: "BO_Order_Item")
                .TabFor(x => x.Attachments))
            .Hide(x => x.SyncToken)
            .Build();

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<Order>.Create()
            .Column(x => x.Number, width: 90)
            .Column(x => x.Customer)
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
            .Hide(x => x.SyncToken)
            .Lookup(l => l
                .Column(x => x.Number)
                .Column(x => x.Customer))
            .Build();
}
```

That file is the sample's only layout source for `Order`; the sample module has no XAFML for it.

## What you get

- **Compile-checked members.** Renaming a property breaks the build, not the running app.
- **Ordinary XAF layering.** The builder output is the generated layer, so module XAFML,
  administrators and users can still customise on top, and their changes win.
- **Fail-fast startup.** A broken layout stops the application at startup with a numbered
  diagnostic that names the view and the member.
- **The app is the designer.** "Export Layout To Code" prints any type's current layout, every
  layer applied, back as the same C#. Arrange it in the running app, paste the code, commit.
- **A skill for agents.** [`skills/xaf-layout-builder/SKILL.md`](skills/xaf-layout-builder/SKILL.md)
  documents the whole API surface for Claude Code, so an agent writes C# instead of XAFML.

## Screenshots

From the E2E gate's run on the sample.

| | |
|---|---|
| ![DetailView from the builder](docs/screenshots/01-detailview-builder-layout.png) | ![ListView columns](docs/screenshots/02-listview-columns.png) |
| DetailView from the builder: captioned Header, collapsible Details, tabs, SyncToken hidden. | ListView: builder column order, sorted by Order Date descending. |
| ![Column chooser](docs/screenshots/03-column-chooser.png) | ![Lookup ListView](docs/screenshots/04-lookup-listview.png) |
| The hidden Sync Token column stays available in the column chooser. | The lookup shows only the columns from `.Lookup(...)`. |
| ![User layer wins](docs/screenshots/05-user-layer-wins.png) | ![Export Layout To Code](docs/screenshots/06-export-layout-to-code.png) |
| A user difference moved Order Date into Details and wins over the builder. | The export prints that merged layout as builder C#. |
| ![After reset](docs/screenshots/07-after-user-reset.png) | |
| With the user's differences deleted, the builder layout is back. | |

## Run it

Requirements: .NET 10 SDK, a DevExpress 26.1 NuGet feed (licensed), SQL Server LocalDB. The E2E
gate also needs Playwright's Chromium.

```bash
dotnet build XafLayoutBuilder.slnx
dotnet test XafLayoutBuilder.Tests
dotnet run --project XafLayoutBuilder.E2ETests
```

The gate builds the sample, starts it on port 5100, asserts, stops it, and exits 0 (pass),
1 (fail) or 2 (Chromium missing). For exit 2, install the browser once:

```powershell
pwsh XafLayoutBuilder.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
```

To look around yourself, run `dotnet run --project XafLayoutBuilder.Sample.Blazor.Server`, open
http://localhost:5000 and log in as `Admin` with an empty password. The sample creates the LocalDB
catalog `XafLayoutBuilder.Sample` and its users on first start in Debug builds.

## Use it in your own solution

1. Reference `XafLayoutBuilder.Module`, which brings `XafLayoutBuilder.Core`.
2. Require the module from yours:
   `RequiredModuleTypes.Add(typeof(XafLayoutBuilder.Module.XafLayoutBuilderModule));`
3. Add a partial `{Type}.Layout.cs` implementing `ISupportViewLayoutCustomization`, or call
   `LayoutRegistry.Register<T>(detail, columns)` for types you do not own.
4. Optionally set `XafLayoutBuilderModule.EnableExport` from your configuration, so administrators
   see the export action without a debugger attached. Never from an environment variable.

The full surface, the rules and the checklist for changing a class are in the skill.

## Startup diagnostics

| Code | Meaning |
|---|---|
| XLB001 | A placed member has no view item, for example because it is `[Browsable(false)]`. |
| XLB002 | A visible member is neither placed nor hidden in the DetailView layout. |
| XLB003 | A column names a collection, or something that is not a member of the type. |
| XLB004 | A type has a spec but no default view to apply it to. |

## How it works

Two generator updaters apply the specs when XAF builds the generated model layer, one for DetailView
layouts and one for ListView columns. A startup check forces XAF to generate every view that has a
spec, so layout errors surface before the first user. The exporter walks the merged model back into
specs, and a printer turns specs into the builder C#.
[docs/how-it-works.md](docs/how-it-works.md) explains the design and where XAF pushed back.
[docs/api-notes.md](docs/api-notes.md) holds the verified XAF API facts with source references.

## Limitations

**Out of scope by design** (start document section 2):

- Localised captions. A separate repository.
- ListView bands. Not investigated beyond the model node's existence.
- WinForms. The module is platform neutral, but nothing was run on WinForms.
- Composing a derived class's layout from its base. A derived class keeps XAF's default layout
  unless it declares its own.
- Runtime editing. No chat, MCP or AI at runtime; the repository gives agents a target.
- Reconciling the builder with module XAFML for the same view. Module XAFML simply applies on top.

**Scope limits of this proof of concept:**

- Only the default views are handled: `{Type}_DetailView`, `{Type}_ListView` and
  `{Type}_LookupListView`. View variants, nested ListViews and custom views are left to XAF.
- Member lambdas must be simple member access. `x => x.Customer.Name` is rejected.
- Builder changes appear after a restart. With XAF's model cache enabled, the cache must be
  invalidated as well; the sample does not enable it.
- Administrator and user differences override the builder, by design. A user who customised a
  property keeps seeing their value after the builder changes, until their differences are reset.
- Every visible member must be placed or hidden in a DetailView layout, so adding a property to a
  class with a layout means touching the layout.

**The export:**

- The popup has no copy button and no option to write the file. A copy button needs Blazor JS
  interop, which the platform-neutral module cannot host. Select all and copy.
- Exported ListViews list every unshown column as `.Hide(...)`, except the key. The model does
  not record whether a column was hidden or never mentioned. The result renders the same but is
  more verbose than hand-written code.
- Sort priority follows column order in the export.
- A group caption XAF derived earlier can survive a user's change and is then exported as an
  explicit caption. In the sample, Details keeps the caption "Notes" after Order Date moves in.
- The action is for administrators only, and only with a debugger attached or `EnableExport` set.

**Engineering caveats:**

- The column updater writes XAF's internal `GeneratedIndex` value by name, so that administrators'
  frozen column sets keep working. A rename in a future DevExpress release would show up as a wrong
  column order in E2E 2. The frozen-columns behaviour itself is reasoned from source, not tested.
- The updaters and the exporter need a live Application Model and are tested only through the E2E
  gate. The start document asked for an exporter unit test; the round trip is asserted in the gate
  instead (E2E 5a).
- E2E 4 writes the user-layer XAFML that XAF's layout editor would persist, rather than driving the
  editor's drag and drop, and restarts the host around the write because XAF Blazor saves the user
  model through a deferred dispatcher.
- The startup check runs once per `XafApplication`, which in XAF Blazor means once per circuit.
- Only the combination above was tested. The sample seeds `Admin` and `User` with empty passwords
  in Debug builds; it is a demonstration, not a production template.

## Repository layout

```
XafLayoutBuilder.Core/                  builder, LayoutSpec records, JSON, C# printer (no DevExpress reference)
XafLayoutBuilder.Module/                generator updaters, registry, startup check, exporter, export action
XafLayoutBuilder.Sample.Module/         Customer, Order (+ lines, attachments), ServiceOrder : Order
XafLayoutBuilder.Sample.Blazor.Server/  XAF Blazor host from the DevExpress 26.1 template
XafLayoutBuilder.Tests/                 xUnit tests for Core
XafLayoutBuilder.E2ETests/              C# Playwright console app, the gate
skills/xaf-layout-builder/SKILL.md      the Claude Code skill
docs/                                   how-it-works, api-notes, screenshots
```

## Related repositories

- **XafMergerTool** stays the answer for stock XAF, where XAFML is the only source form. It can gain
  an "export as builder C#" option by referencing `XafLayoutBuilder.Core`.
- **BPG** can emit `LayoutSpec` for generated applications, as C# through the printer or later as
  JSON (`LayoutSpecJson` already round-trips).
- **xafskills** receives a copy of the skill on release.

Origin: DevExpress Support Center ticket T1206756 (Model Editor and non-Windows development).
