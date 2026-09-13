# API notes (DevExpress XAF 26.1)

Findings from section 7 of the start document, verified 2026-09-11 against dxdocs and the
installed source at `C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp`.
Each line says where it was verified. Skill material for `skills/xaf-layout-builder/SKILL.md`.

## Generators and updaters

- `ModelDetailViewLayoutNodesGenerator` (namespace `DevExpress.ExpressApp.Model.NodeGenerators`)
  generates the content of the `IModelViewLayout` node. An updater is
  `ModelNodesGeneratorUpdater<ModelDetailViewLayoutNodesGenerator>` with
  `public override void UpdateNode(ModelNode node)`; `node` is the Layout node
  (`IModelViewLayout`), `node.Parent` is the `IModelDetailView`. [dxdocs: class page; source
  `Model/NodeGenerators/ModelDetailViewLayoutNodesGenerator.cs` line 84]
- `ModelListViewColumnsNodesGenerator` generates the `IModelColumns` node; `node.Parent` is the
  `IModelListView`. [dxdocs; source `Model/NodeGenerators/ModelListViewNodesGenerator.cs` line 390]
- Register in `ModuleBase.AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters)` via
  `updaters.Add(new X())`. Updaters run at the Application Model zero (generated) layer; module,
  admin and user differences apply on top. [dxdocs 404125, 113315]
- **Model cache bypasses `UpdateNode`, and exists only in WinForms (CACHE-001).** With a cache,
  `ModelNodesGeneratorBase.GenerateNodes` calls only `UpdateCachedNode` (default no-op) because the
  cached model already contains the updater output. [source `Model/ModelNodeGenerator.cs` lines
  50-64, 113-122] XAF creates a cache manager only when `EnableModelCache` is set and
  `GetModulesVersionInfoFilePath()` is not empty (`XafApplication.cs` lines 1709-1712). The base
  returns null (line 1329), and the only override in the 26.1 sources is `WinApplication`'s
  (`DevExpress.ExpressApp.Win/WinApplication.cs` line 686). In XAF Blazor the cache is therefore
  never loaded or saved, and the generator layer, updaters included, is built on every start
  (`ApplicationModelsManager.cs` lines 241-262). Where the cache is used, it is reloaded only while
  every module's version matches the `ModulesVersionInfo` file (`XafApplication.cs` lines 436-463;
  dxdocs, `XafApplication.EnableModelCache`), so a changed spec in an application module needs that
  module's version bumped or `Model.Cache.xafml` deleted.
- `ModelNodesGeneratorUpdater<T>` also implements `ISupportCachedNodesGeneratorUpdater`; `T` must
  derive from `ModelNodesGeneratorBase`. [source `Model/ModelNodesGeneratorUpdater.cs`]
- **An updater that throws leaves its partial work behind.** `GenerateNodes` runs the stock generator
  and then every updater, inside `ModelNode._RunNodesGenerator1`, whose `finally` sets
  `IsNodesGeneratorInProgress = false`, which marks the node `Done`. Generation is not retried, so
  code that catches the exception sees whatever the updater had written by then. Both updaters
  therefore check everything before they change anything (APPLY-001). [source
  `Model/ModelNodeGenerator.cs` lines 50-65; `Model/Core/ModelNode.cs` lines 2219-2229 and 445-458]
- **Which members get a DetailView editor.** The items generator creates one only when
  `GetShouldGenerateMember` passes: `IMemberInfo.IsVisible` (that is `[Browsable]` plus
  `HideInUI.ModelMember`), not the reference to the owner, no `HideInUI.DetailViewEditor`, and a
  simple, class or interface member type. It never reads `IsVisibleInDetailView`; only the layout
  generator does. A `[VisibleInDetailView(false)]` member therefore keeps its `Items` entry and can
  be placed, so XLB001 names `[Browsable(false)]` and `[HideInUI]` instead (DIAG-001). [source
  `Model/NodeGenerators/ModelDetailViewNodesGenerator.cs` lines 115-135;
  `DC/Internal/XafMemberInfoInternal.cs` lines 75-80;
  `Model/NodeGenerators/ModelDetailViewLayoutNodesGenerator.cs` line 242]
- **Differences target node paths, so converting a view does not carry them over, and the next save
  deletes them.** Stored differences are stacked as layers (`ModelApplicationHelper.AddLayer` ->
  `InsertLayerAtCoreInLock`) and a node resolves by id through the layer chain. A difference node
  that no earlier layer has and that is not marked `IsNewNode` is not merged:
  `ModelNode.CreateMasterNode` sets it aside as unusable (`AddUnusableNodeAndRemoveFromList`). So
  once the builder has replaced the tree, a difference aimed at the stock `Main/SimpleEditors/...`
  is neither rendered nor part of the merged model (the export does not print it), while nodes in
  the same difference that target builder paths still apply. `ModelDifferenceDbStore.SaveDifference`
  writes each aspect of the usable layer only; `FileModelStore.SaveDifference` is the store that
  also writes the unusable model, to `UnusableNodes*.xafml` (source only, not observed). With the
  database user store, the first save after the conversion therefore removes the orphaned difference
  for good. XAF Blazor saves a user's model when it is flushed: at Log Off, and when a new circuit
  for the same user loads its differences (a reload, a second tab); disposing the application
  discards the pending save, so killing the host saves nothing. Observed with a canary and gated in
  DIFF-001. (`ModelNode.Merge`/`ApplyDiff`, which creates missing nodes, is the explicit merge API,
  not the runtime path.) `FreezeLayout` clones the whole layout into the difference layer and resets
  the master, so a frozen layout does not depend on the generated tree. [source
  `Model/Core/ModelApplication.cs` lines 865-869; `Model/Core/ModelNode.cs` lines 793-812, 1126-1133,
  1405-1429, 2080-2101, 2316-2319; `ModelDifferenceDbStore.cs` lines 173-215;
  `ModelDifferenceStore.cs` lines 345 and 493-500; `DevExpress.ExpressApp.Blazor/BlazorApplication.cs`
  lines 103-131 and 270-276; `DevExpress.ExpressApp.Blazor/Services/AppState/UserModelSaveDispatcher.cs`
  lines 76-98 and 138-141; `Model/DomainLogics/ModelViewLogic.cs` lines 121-135]
- **A module scans its own assembly, so it must not share one.** `ModuleBase.GetModuleUpdaters`
  instantiates every `ModuleUpdater` in `GetType().Assembly` that has an `(IObjectSpace, Version)`
  constructor, and `DatabaseUpdater.GetModuleUpdaters` concatenates every module's list without
  removing duplicates. `ModuleBase.DiffsStore` defaults to a `ResourcesModelStore` over the same
  assembly. Copying `XafLayoutBuilderModule` into an existing module project would therefore run that
  project's updaters twice and read its model difference resource a second time (DOCS-002). [source
  `ModuleBase.cs` lines 207-213 and 240-248; `Updating/DatabaseUpdater.cs` lines 100-108]

## Layout model interfaces (`Model/IModelDetailView.cs`)

- `IModelViewLayout : IModelNode, IModelList<IModelViewLayoutElement>` (line 86).
- `IModelViewLayoutElement`: `string Id`, `double RelativeSize` (percentage of parent) (line 89).
- `IModelLayoutGroup : IModelViewLayoutElement, IModelLayoutElementWithCaption,
  IModelList<IModelViewLayoutElement>, ...`: `FlowDirection Direction` (default Vertical),
  `string ImageName`, `bool IsCollapsibleGroup`, `bool IsGroupCollapsed`, plus `Caption` and
  `bool? ShowCaption` from the caption interfaces (line 342).
- `IModelTabbedGroup : IModelViewLayoutElement, IModelLayoutElementWithCaption,
  IModelList<IModelLayoutGroup>`: children **must be `IModelLayoutGroup`** (one per tab); has its
  own `Direction` (default Horizontal) and `MultiLine` (WinForms only) (line 364).
- `IModelLayoutViewItem : IModelLayoutItem, IModelLayoutElementWithCaptionOptions, ...`:
  `IModelViewItem ViewItem` (the `IModelDetailView.Items` entry) and `ShowCaption` (line 384).
- A collection property tab is exactly what the stock generator builds: `IModelTabbedGroup` →
  `IModelLayoutGroup` (id = member name, `ShowCaption = true`) → `IModelLayoutViewItem`
  (id = member name, `ViewItem = editor`, `ShowCaption = false`). [source
  `ModelDetailViewLayoutNodesGenerator.CreateTabsLayoutGroup` / `CreateLayoutItem`, lines 350-365]
- Stock root: `AddNode<IModelLayoutGroup>("Main")`, `Index = 0`, `Direction = Vertical`,
  `ShowCaption = false`. The stock generator only places property editors whose
  `ModelMember.IsVisibleInDetailView` is null or true. [lines 89, 364-376]
- `FlowDirection` used by the model is `DevExpress.ExpressApp.Layout.FlowDirection`
  (`Vertical`, `Horizontal`). Core has its own enum with the same names; the applier maps them.

## Blazor rendering facts (observed in the E2E DOM, session 3)

- A layout group renders a header (`.dxbl-group-header` inside `.dxbl-group`, `aria-expanded` on
  the `[role=group]` element) only when `ShowCaption` is true. `IsCollapsibleGroup = true` on a
  group whose caption is not shown renders as a plain group with no toggle. The applier therefore
  forces `ShowCaption = true` for `Collapsible()`.
- Captioned but not collapsible: header without a button. Collapsible: header contains the toggle
  `button`. That button is how the E2E tells the two apart.
- A group with no explicit `Caption` and a single item takes that item's caption as its header
  (the `Details` group shows "Notes"). XAF default domain logic; not something the builder sets.
- The ListView stays in the DOM on its own inactive tab when a DetailView opens from it, so
  assertions must be scoped to `.detail-view-content`, not `body`.
- Form layout DOM: `.dxbl-fl` > `.dxbl-row` > `[role=group].dxbl-fl-group` (Main) >
  groups / `.dxbl-fl-gt` (tabbed group, `.dxbl-tabs`) > `.dxbl-fl-item` with
  `label.xaf-item-<member>` and `.dxbl-fl-ctrl`. Tab headers: `.dxbl-tabs-item` with
  `div.xaf-item-<member>`; the `imageName` renders as `img.xaf-layout-tab-icon`.

- Grid header cells (`th.dxbl-grid-header`) include the filter button's accessibility text
  "No filter applied"; strip it before comparing captions. The first header is the selection
  column ("Selection").
- Column chooser: right-click a header → context menu item "Column Chooser" (XAF's
  `ColumnChooserController` action, also reachable from the toolbar's hidden actions). The
  chooser lists every model column including `Index = -1` ones.
- Lookup editor (`LookupPropertyEditor.DefaultUseViewMode = true` in the template's Program.cs):
  view mode shows a link plus an "Edit" button; edit mode shows "Add" and "Open or close the
  drop-down window" buttons. The dropdown is a `.dxbl-dropdown` holding a grid of the
  LookupListView's columns without `<th>` headers; its first `innerText` line is the tab-separated
  header row. Other `.dxbl-dropdown` elements (filter menus) exist in the DOM, so select by content.
- XAF hides an aggregated child's back-reference (`OrderLine.Order`) in the nested DetailView; a
  lookup to `Order` has to come from a plain reference such as `ServiceOrder.OriginalOrder`.

## Columns model (`Model/IModelListView.cs`)

- **Nested ListViews (VIEW-001).** `ModelViewsNodesGenerator` generates `{DeclaringType}_{Collection}_ListView`
  for every list-editor-compatible collection, in the generated layer, with `ModelClass` set to the
  element type and the collection's `IMemberInfo` stored as the helper value
  `ModelViewsNodesGenerator.NestedListViewMemberInfo` (`Model/NodeGenerators/ModelNestedListViewNodesGenerator.cs`
  lines 46-60). Its Columns node therefore runs the columns generator and `ListViewColumnsUpdater`.
  The stock generator hides the back-reference: the column whose member is the collection's
  `AssociatedMemberInfo` when the collection is an association (`ModelListViewNodesGenerator.cs`
  lines 138-146, `IsParentProperty`). A view that exists only in a difference layer (module XAFML, a
  Model Editor clone) runs only `ModelNodesDefaultInterfaceGenerator`, never its own nodes generator
  or that generator's updaters (`Model/Core/ModelNode.cs` lines 2186-2206).
- **Views declared in code (VIEW-001).** DevExpress's own modules add a view to the generated layer
  from a `ModelNodesGeneratorUpdater<ModelViewsNodesGenerator>`: `views.AddNode<IModelDetailView>(id)`
  and `ModelClass = …`, after which XAF generates its items and layout on demand
  (`DevExpress.ExpressApp.Dashboards/GeneratorUpdaters/DashboardsViewsNodesGenerator.cs` lines 44-70,
  `DevExpress.ExpressApp.ReportsV2.Blazor/Module.cs` lines 152-164). `DeclaredViewsUpdater` does the
  same for `LayoutRegistry.AddDetailView`/`AddListView`, so the layout and columns generators, and this
  module's updaters, run for those views. The in-process model confirms it: before the updaters looked
  views up by id, a declared DetailView came back with XAF's stock `SimpleEditors` layout.
- **Bands (BAND-001).** `IModelListView.BandsLayout` is an `IModelList<IModelBand>` with `Enable`;
  bands are nodes under it, columns stay under `Columns` and join a band through
  `IModelBandedColumn.OwnerBand` (`Model/IModelBandsLayout.cs` lines 52-83). Every runtime column is an
  `IModelBandedColumn` (`ModelBandsLayoutHelper` casts them, `Model/DomainLogics/ModelBandViewLogic.cs`
  line 59), and a band's default caption is its id (`Get_Caption`, line 84). No generator fills
  `BandsLayout`. XAF Blazor uses bands only when `Enable` is set and groups data columns by
  `OwnerBand` (`DxGridBase/DxGridColumnsListEditorModelSynchronizer.cs` line 99,
  `DxGridBandLayoutModelSynchronizer.cs` lines 180-210); dxdocs (List Views: Banded Column Layout)
  says Blazor does not support multi-level bands. Siblings and bands sort by `Index`
  (`ModelBandedLayoutItemComparer`, `Model/ColumnModelNodesComparer.cs` line 48). Writing `BandsLayout`
  from the columns updater stays in the generated layer, because the generator-in-progress counter
  belongs to the layer's root (`Model/Core/ModelNode.cs` lines 430, 460-463, 1249-1251).

- `IModelColumn : IModelMemberViewItem`: `int Width`, `DevExpress.Data.ColumnSortOrder SortOrder`,
  `int SortIndex`, `int GroupIndex`, `GroupInterval GroupInterval` (line 146). `Index` comes from
  `IModelNode`.
- **A grouped column keeps its sort order but not its sort index** (SORT-001). DxGrid never lets a
  column hold both a `SortIndex` and a `GroupIndex` (`GridUtils.cs` 289-290), clears only
  `GroupIndex` when a grouped column's sort is cleared (`GridColumnHelper.cs` 390-394), and XAF's
  wrapper ignores a `SortIndex` set on a grouped column (`DxGridColumnWrapperBase.cs` 147-150).
  `ColumnsListEditor.SynchronizeModel` copies all three into the model as they are (lines 93-99), and
  grouping leaves `VisibleIndex` alone, so a grouped, shown column reads `SortOrder` set,
  `SortIndex = -1`, `GroupIndex >= 0`. The grid sorts grouped columns first, by `GroupIndex`, then
  the others by `SortIndex` (`GridColumnHelper.SortedColumns`, lines 75-79); the exporter ranks
  sort priority the same way. Paths under `Components\Sources\Blazor\DevExpress.Blazor.Grid\Grid\`
  and `DevExpress.ExpressApp\DevExpress.ExpressApp.Blazor\Editors\DxGridBase\`.
- Lookup ListViews are marked with the node value `ModelViewsNodesGenerator.IsLookupListView`
  (`"IsLookupView"`); the columns generator branches on it. [`ModelListViewNodesGenerator.cs` 390-396]
- **Index handling in the first layer:** after generating, if `node.IsInFirstLayer` the generator
  copies each column's `Index` into a private value (`GeneratedIndexValueName`) and clears
  `Index`. Session 4 must set `Index` *after* that, i.e. in the updater (which runs after
  `GenerateNodesCore`), and should check whether the domain logic that reads the generated index
  interferes. [lines 397-402]
- `Index = -1` as "hidden but available in the column chooser": the stock generator itself sets
  `columnInfo.Index = -1` for members it does not show (line 336). The Blazor grid maps it to
  `VisibleIndex = -1` (`DxGridColumnWrapperBase.GetIsVisible`) and only fetches columns with
  `Index` null or > -1 (`DxGridListEditorBase.RequiredProperties`, lines 916-920), so the column
  exists but is not shown.
- **Lookup ListViews have almost no columns.** `GenerateLookupListViewColumns` creates columns only
  for `FriendlyKeyProperty`, the display property (sorted ascending) and members whose
  `IModelMember.IsVisibleInLookupListView` is true (`IsVisibleInView` switches on the generation
  mode, lines 147-157); if that yields no columns at all it falls back to the ordinary column set
  (lines 377-383). The default ListView creates a column for every visible member (unshown ones
  with `Index = -1`). So a `.Lookup(l => l.Column(x => x.Customer))` needs the updater to **add**
  the column: `columns.AddNode<IModelColumn>(name)` + `PropertyName = name`, which is what the
  generator's internal `CreateMemberViewItemInternal` does (lines 437-442; its `View_ID` value only
  matters for list-property editors). Found by the XLB003 fail-fast on the first session 4 run.
  The same applies to hidden members: they get a column too, so the chooser can offer them.
- **Session 4 decision on Index (revised after Codex review):** the updater does what the stock
  generator does: `SetValue<int?>("GeneratedIndex", n)` and `ClearValue("Index")` on the column
  node. `ModelColumnDomainLogic.Get_Index` (`ModelViewLogic.cs` 417-432) returns that value while
  `Index` is null, and returns -1 for every generated column when the admin set
  `IModelListView.FreezeColumnIndices`. Storing `Index` directly would have made a later-added
  listed column visible despite the freeze. The value name is an internal constant
  (`ModelListViewColumnsNodesGeneratorBase.GeneratedIndexValueName`), so the literal is repeated in
  the updater with a comment. Unmentioned and hidden columns get generated index -1 and their
  default sort cleared (the stock generator sorts the display member ascending, which would
  otherwise fight the spec's sort).

- **`FreezeColumnIndices`, verified in the gate (FREEZE-001).** Turning it on in a layer
  (`Set_FreezeColumnIndices`, not a new node) force-sets `Index` to the current value on every
  column in that layer; turning it off clears those values again. `IModelColumn.Index`'s domain
  logic (`Get_Index`), consulted only when no layer set `Index`, returns the generated index while
  the view is not frozen and `-1` while it is. A column added to a spec after the freeze (it has no
  explicit `Index` in the freezing layer) therefore stays hidden but offered by the column chooser.
  That holds only because the updater orders columns through `GeneratedIndex` instead of writing
  `Index`. [source `Model/DomainLogics/ModelViewLogic.cs` lines 85-101 and 417-433]
  **Only from an application-level layer.** Measured in the sample host (2026-09-13): the same
  freeze in module XAFML, or in an extra store added through `CreateCustomModelDifferenceStore`
  (`e.AddExtraDiffStore(id, new FileModelStore(folder, name))`, which keeps the default `Model.xafml`
  store because `Handled` stays false, `XafApplication.cs` lines 416-434), hides a later-added
  column. Stored in a user's own differences (`ModelDifferenceDbStore`), it has no effect: a
  freeze-only user difference still shows every column. XAFML values load through
  `SetSerializedValue` with the layer detached, not through the domain-logic setter
  (`Model/ModelXmlWriter.cs` lines 171-197); why the user-layer value does not reach `Get_Index` was
  not traced.
- **Which unshown columns the exporter prints as hidden (EXPORT-001).** In the merged model a column
  the spec hid and one it never mentioned look the same, so `ListViewColumnsUpdater` stamps every
  column the spec hides (model value `XafLayoutBuilder.HiddenColumn`). Every other unshown column is
  printed too, except a generated leftover, an unstamped column with `GeneratedIndex` -1: the stock
  generator gives every column it makes a generated index (`Model/NodeGenerators/ModelListViewNodesGenerator.cs`
  lines 398-402) and the updater does the same. So a column the generated layer showed and a later
  layer hid is printed, and so is one a later layer added (no `GeneratedIndex`), which applying the
  export would otherwise lose from the column chooser. Not decided by `HasValue("Index")`: `HasValue` does read through the layers
  (`Model/Core/ModelNode.cs` lines 2435-2447), but a freeze sets `Index` on every column of its layer
  (above), and `IModelColumn` carries `ApplyDiffValuesMap(GeneratedIndex, Index)`
  (`Model/IModelListView.cs` line 145, applied in `ModelNode.cs` line 1556), which turns a generated
  index into a stored `Index` when node values are copied. Either would mark every unshown column hidden.
  **Not with XAF's model cache, which only WinForms uses (top of this file).** Both markers, this one and
  `DetailViewLayoutUpdater.CatchAllMarker`, are stored as bools, and `GetSerializedValue` keeps a
  non-string value for the cache only when it finds a value type for its name (`Model/Core/ModelNode.cs`
  lines 3061-3071); a cached start runs `UpdateCachedNode`, not `UpdateNode`, so they are not set
  again. With the cache on, the export would drop the spec's `.Hide(...)` calls and print a catch-all
  group as ordinary items. Left as is while nothing is run on WinForms (CACHE-001).

## Startup forcing (session 5)

- `XafApplication.SetupComplete` (`XafApplication.cs` line 2947) fires from `OnSetupComplete`
  once `Model` exists; `BlazorApplication.OnSetupComplete` calls the base first. A handler can
  read `application.Model.BOModel` (`IModelBOModel : IModelList<IModelClass>`,
  `CommonInterfaces.cs` 165) and each class's `DefaultDetailView` / `DefaultListView` /
  `DefaultLookupListView` (lines 250-258).
- Reading `IModelNode.NodeCount` on a view's `Layout` or `Columns` node generates its children
  (`ModelNode.EnsureNodes`), which runs the generator updaters. That is how the diagnostics are
  forced before the first user.
- In the Blazor template the application is built while the ASP.NET host starts (the exception
  stack goes through `Microsoft.Extensions.Hosting.Internal.Host.ForeachService`), so an exception
  from `SetupComplete` is unhandled and terminates the process before Kestrel listens.
- **One Application Model per process in ASP.NET Core.** `SharedApplicationModelManagerContainer`
  creates the `ApplicationModelManager` once, under a lock, and hands the same one to every
  application instance, so the generated layer, and with it the updaters, runs once per process
  rather than once per circuit. [source `DevExpress.ExpressApp.AspNetCore/Services/Shared/SharedApplicationModelManagerContainer.cs`
  lines 42-67]
- **An Application Model can be built in a unit test, the way the Model Editor builds one** (MODEL-001).
  `DesignerModelFactory.CreateModulesManager(module, assembliesPath)` creates an `ApplicationModulesManager`, adds the
  module's `RequiredModuleTypes` and the module, and loads them into `XafTypesInfo.Instance`;
  `CreateApplicationModel(module, modulesManager, ModelStoreBase.Empty)` sets up an `ApplicationModelManager` with the
  modules' generator updaters and returns the model, with no `XafApplication`, host or database. Nodes are generated on
  first access, so reading a view's `Layout` or `Columns` runs the stock generators and this module's updaters. A test
  module that requires `SystemModule` and `XafLayoutBuilderModule` and exports `[DomainComponent]` classes needs no
  EF Core types info source. Those classes must be top-level public types: a type is visible, and so gets a BOModel
  class and views, only when `Type.IsPublic`, which is false for every nested type. `XafTypesInfo.Instance` is
  process-wide. [source `Utils/DesignerModelFactory.cs` lines 374-391 and 399-430; `ApplicationModelsManager.cs`
  lines 360-429; `DC/BaseTypeInfoSource.cs` lines 137-144; `DC/NonPersistentTypeInfoSource.cs` lines 85-100;
  observed in `XafLayoutBuilder.Tests/ModelSpikeTests.cs`]
- **Where `Tracing` writes.** `Tracing.Tracer.LogError(Exception)` writes the exception's type,
  message and stack trace to `eXpressAppFramework.log` in the executable's folder, through a
  `TextWriterTraceListener`; not to the console or `ILogger`. The degraded path of
  `FailFastOnLayoutErrors` logs there. [dxdocs 112575, 112576; source
  `DevExpress.Persistent.Base/Tracing.cs` lines 469 and 762]
- **Opening a ListView also generates its class's DetailView layout.** Observed in the E2E gate
  (TEST-001, 2026-09-13), not traced to a source line: with fail-fast off, opening `Order_ListView`
  made `DetailViewLayoutUpdater` log `Order_DetailView`'s broken spec although no DetailView was
  opened. A broken DetailView spec therefore surfaces as soon as its ListView is used, and a logged
  message alone does not say which path reported it; the startup check's own report is its single
  `N layout problems` entry.

## Exporter and user layer (session 6)

- `ModelNode.HasValue(name)` is public and tells a stored value from a computed default, **but for
  localizable values (`Caption`) it checks the current language aspect**
  (`GetValueCurrentAspectIndex`, `ModelNode.cs` 848), so a caption the updater set in the default
  aspect reads as "no value" once a language is active. The exporter compares against XAF's
  default instead: `ModelLayoutGroupLogic.Get_Caption` returns the single view item's caption for a
  one-item group, otherwise the group id (`IModelDetailView.cs` 172-180). `RelativeSize` and
  `Width` are not localizable, so `HasValue` works for them.
- The same trap applies to a **column** caption, which falls back to its member's caption
  (`IModelColumn : IModelMemberViewItem`, `CommonInterfaces.cs` 665). The exporter compares with
  `IModelColumn.ModelMember.Caption`; until session 7 it used `HasValue` there and silently dropped
  an explicitly set column caption. Now covered by E2E 5a through `Customer.Layout.cs`.
- XAF Blazor's loading toast is `Templates/LoadingIndicatorComponent.razor`: a span with the
  localized text "Loading". The E2E waits for it to disappear before taking a screenshot, matching
  the text as a substring because it renders with an ellipsis.
- `IModelLayoutGroup.ImageName` also has a computed default (`Get_ImageName`, from the property
  editor's view image for one-item groups); the exporter treats an empty string as "not set".
- **User model persistence in XAF Blazor is deferred.** `BlazorApplication.LoadUserDifferences`
  (line 103) first flushes the `IUserModelSaveDispatcher`'s in-memory copy of the user model to the
  store, then loads. A `ModelDifferences` row written from outside is therefore overwritten before
  it is read unless the process is restarted first. `SaveModelDifferencesController` compares the
  serialised user layer with a cached string and saves on change.
- `ModelDifferenceDbStore` finds the user row by `UserId` (the security user id as an invariant
  string, lowercase Guid here) and `ContextId` (`"Blazor"` in the template); the aspect with
  `Name = ""` holds the XAFML (`ModelDifferenceDbStore.cs` 94-150, 173-212).
- XAF EF Core deferred deletion adds a query filter `GCRecord == 0`
  (`EFCoreDeferredDeletionRegistration.cs` 81). Rows inserted with `GCRecord NULL` are invisible.
- A user-layer XAFML that moves an item: under `<LayoutGroup Id="Header">` write
  `<LayoutItem Id="OrderDate" Removed="True" />`, under the target group
  `<LayoutItem Id="OrderDate" ViewItem="OrderDate" Index="1" IsNewNode="True" />`. Verified by
  E2E 4-6: the layout renders, exports and resets accordingly. The layout editor writes this same
  shape, plus a `RelativeSize` on every item of the groups it touched (read from `ModelDifferenceAspects`
  after a drag, E2E4-001), which the export then prints as `relativeSize:` arguments.
- The user layer also stores `DocumentManagerState` (the open tabs); a fresh circuit restores the
  last active view, which can interrupt a Playwright navigation.
- Actions in `PredefinedCategory.Tools` render as a "Tools" tab next to Home and View in the
  Blazor template.
- The export popup is a DetailView of a `NonPersistentBaseObject` with one unlimited-size string;
  `AddNonPersistent()` in the host is required (the template has it).
- The Blazor layout editor (`Layout/LayoutEditor/LayoutEditor.razor`) moves elements only by
  drag-and-drop; its context menu offers hide/show text, rename, best fit, collapsible toggles and
  reset. Measured in the sample (E2E4-001): the form's context menu (right-click an empty area) has
  Customize Layout and Reset Layout; the Customization window carries the class
  `xaf-layouteditor-menu` with a Layout Tree View and Hidden Items; no element has a `draggable`
  attribute, so the drag is pointer-event based and Playwright drives it with mouse down, stepped
  moves and mouse up; closing the window leaves the change saved in the user's `ModelDifferences`
  row in the same session. A grid header's context menu has Hide This Column.
- `IModelMemberViewItem.PropertyName` is the bound member; the node `Id` is free text and only
  usually the same. Anything printed as a member lambda must come from `PropertyName`
  (`CommonInterfaces.cs` 665).
- `IModelViews` is an `IModelList<IModelView>`, so `application.Model.Views[id]` resolves a view by
  id. The startup check uses that instead of `IModelClass.DefaultDetailView` and friends
  (`CommonInterfaces.cs` 250-258), which a model difference can repoint to another view.

## Runtime Model Editor (MODELEDITOR-001 spike)

Paths under `DevExpress.ExpressApp\` unless another assembly is named.

- XAF Blazor 26.1 has no runtime Model Editor. The WinForms Edit Model action is gated by
  `IRequestSecurity.IsGranted(new ModelOperationPermissionRequest())`
  (`DevExpress.ExpressApp.Win/SystemModule/EditModelController.cs` 67-71, 79-81). That request is
  answered by `ModelPermissionRequestProcessor`, which looks only for a `ModelOperationPermission`
  (`DevExpress.ExpressApp.Security/SecurityStrategy/ModelPermissionRequestProcessor.cs` 56-57);
  `PermissionsExtractor` adds one only for a role with `CanEditModel`, and `IsAdministrative` adds
  `IsAdministratorPermission` instead (`PermissionPolicy/PermissionsExtractor.cs` 51-56). So an
  administrator without `CanEditModel` may not edit the model; the sample's Administrators role sets it.
- Values: `ModelNode.GetValue(string)` reads through every layer (`Model/Core/ModelNode.cs` 2450-2453);
  `SetValue(string, object)` (2598-2601) and `ClearValue(string)` (2368-2383) write the writable
  layer, the running application's user differences; `IsValueModified(string)` (899-903, public,
  `EditorBrowsable(Never)`) says whether the writable layer holds the value.
- Value list: `ModelNode.NodeInfo.ValuesInfo` (`Model/Core/ModelNodeInfo.cs` 411) always carries `Id`,
  `Index`, `IsNewNode` and `IsRemovedNode` (175-181). `ModelValueInfo` gives `Name`, `PropertyType`,
  `IsReadOnly` (`Model/Core/ModelValueInfo.cs` 76-80). Visibility and read-only rules come from the
  public `FastModelEditorHelper` (`Model/FastModelEditorHelper.cs` 114-140 `IsPropertyModelBrowsableVisible`,
  185-211 `IsReadOnly`).
- Hosting a Razor component: a `BlazorPropertyEditorBase` whose `CreateComponentModel` returns a
  `ComponentModelBase` with properties named like the component's parameters (dxdocs 405922;
  `DevExpress.ExpressApp.Blazor/Editors/BlazorPropertyEditorBase.cs` 156-168), registered with
  `[PropertyEditor(typeof(string), alias, false)]` and picked by `[EditorAlias]`; `IComplexViewItem.Setup`
  supplies the `XafApplication`.
- Saving: `XafApplication.SaveModelChanges()` (`XafApplication.cs` 2497-2506) saves the last layer when it
  is the user differences, the same call the Blazor layout editor makes
  (`DevExpress.ExpressApp.Blazor/Layout/LayoutEditor/LayoutEditor.razor.cs` 278).
- Observed in the gate (MODELEDITOR-001): the save is written at once. A localizable value such as a
  view `Caption` lands in the user's `ModelDifferenceAspects` row of the current culture (`Name`
  `en-US`), not in the default aspect (`Name` empty), so a check of the stored model must read every
  aspect. A new circuit shows the edit: `BlazorApplication.LoadUserDifferences` flushes the user's
  deferred save before it loads (`DevExpress.ExpressApp.Blazor/BlazorApplication.cs` 103-111,
  `Services/AppState/UserModelSaveDispatcher.cs` 76-98).
- XAF Blazor 26.1 warms the application up by default (`Optimization.WarmUpApplication`,
  `DevExpress.ExpressApp.Blazor/Services/StartupExtensions.cs` 222), and each application's model is then
  collapsed (`XafApplication.cs` 495-500, `Model/Core/ModelApplication.cs` 699-724) and cached: `GetValue`
  answers from the root master's value cache (`ModelNode.cs` 2503-2524, `IsCached` 3694-3701). `SetValue`
  updates that cache (`UpdateCachedValue`, 2668), `ClearValue` does not (2368-2390): after a clear the writable
  layer no longer holds the value (`IsValueModified` false) but `GetValue` still returns it, until the model
  is built again (the next circuit). Measured in the gate with trace output, MODELEDITOR-001. `Undo()` does
  refresh the cache (`UpdateCache`, 609-637) but takes back every modification of the node. The in-process
  test model is not warmed up, so a unit test cannot see this.
- Consequence for the editor: an unsaved edit cannot be rolled back by clearing it. Edits stay pending in
  the editor and are written to the model only on Save, so closing the popup simply drops them. No public
  call refreshes a node's cached values: `UpdateCache`, `UpdateLocalCacheRecursive` and `CacheAllNodeValues`
  are internal (`ModelNode.cs` 518-538, 3607-3635), the public dependency updaters need the root's internal
  `localCache` (`ModelNodeValuesCache.cs` 371-387), and `Undo()` takes back the node's other modifications.
  So Save reloads the page (MODELEDITOR-002): the new circuit builds the model again from the saved
  differences, which is right after a reset, as the WinForms editor restarts every window after editing
  (`WinApplication.EditModel` 782-817).
- The database store keeps an aspect whose differences became empty: `ModelDifferenceDbStore.SaveDifference` writes
  one `ModelDifferenceAspect` row per aspect but skips an aspect whose XML is empty (`ModelDifferenceDbStore.cs`
  194-213). Measured in the gate (MODELEDITOR-002): after a saved reset of a view caption the `en-US` row, which held
  only that caption, still held it, and the reload brought it back. The default aspect does not empty this way; it
  keeps the node structure an edit created. The editor blanks such rows after its save with public API only
  (`StoredAspectCleanup`): it captures the host's `ModelDifferenceDbStore` from `CreateCustomUserModelDifferenceStore`
  (subscribed on `SetupComplete`, after the template's module set it), finds the row with `FindModelDifference` /
  `FindModelDifferenceAspect` (`ModelDifferenceDbStore.cs` 262-297) through the store's `CreateObjectSpaceHandler`
  (83), and writes `EmptyXafml` (80). The store's `ModelDifferenceType` is internal (87); the application's single
  persistent `IModelDifference` class stands in for it. It keeps the store's own version guard: `SaveDifference`
  writes only when the stored difference's `Version` is not newer than the saved layer's (`ModelDifferenceDbStore.cs`
  181), so a difference an administrator copied to the user meanwhile is left alone. It blanks only the aspects this
  save emptied (`ModelEditing.AspectsEmptiedBy`, empty after the pending edits are applied and not before): an ordinary
  save does not bump `Version`, and an aspect already empty in a circuit's model may hold what another tab of the same
  user saved since. The editor's session keeps those aspects until the save and the cleanup succeeded, so a failed
  save that is retried (no pending edits left, the aspect already empty) still blanks them, but only while they are
  still empty: an aspect the user filled again before the retry is dropped from the set. The XAF Blazor template creates the user store itself
  (`new ModelDifferenceDbStore(app, typeof(ModelDifference), false, "Blazor")` in its Blazor module).
- A warmed-up model can be built in-process (`WarmedUpModelTests`): `ApplicationOptions().Optimization.WarmUpApplication`
  sets the process-wide flag (`Services/Core/Internal/ApplicationOptions.cs` 67-88); `Collapse()` needs
  `ModelMultipleMasterStore.Instance`, which only `BlazorApplication`'s constructor sets (`BlazorApplication.cs` 82;
  without it `Collapse` throws a NullReferenceException); the running application uses `FastModelNodeLockHelper`,
  warms a shared model up and collapses each application's own model built from the same manager
  (`ApplicationWarmUpService.cs` 121-179, `ApplicationModelsManager.CreateModelApplication` 418-429). The shared
  values cache (`ModelNodeSharedValuesCache.Instance`, `Model/Core/ModelNodeValuesCache.cs` 143-177) is process-wide
  and `WarmUp()` fills it only while it is empty (`ModelApplication.cs` 519-526); clear it before each warm-up, as
  `ApplicationWarmUpService.PrepareForWarmUp` does (104-110), or a second warm-up in the same process reads the
  first model's values. Hooks tried on the way: the Razor component's `Dispose` ran after the gate had reopened the
  editor; the popup view's `Closed` event does run on Cancel (`SystemModule/DialogController.cs` 146-149,
  `DevExpress.ExpressApp.Blazor/BlazorWindow.cs` 66-80, `View.cs` 286-302), but the clear it made hit the cache.

## Still open

- Bands (`IModelListView.BandsLayout`, `IModelBandsLayout` is added as a child node at
  `ModelListViewNodesGenerator.cs` line 82). Not investigated further; phase 2 at the earliest.
- `ModelNodesGeneratorUpdater` registration timing vs `CustomizeTypesInfo`: updaters run during
  model generation, which happens after all modules' `CustomizeTypesInfo`. Confirmed indirectly by
  the sample (Order's spec resolves in the updater); not traced in source.
