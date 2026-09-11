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
- **Model cache bypasses `UpdateNode`.** With `ModelApplicationBase.EnableModelCache = true`,
  `ModelNodesGeneratorBase.GenerateNodes` calls only `UpdateCachedNode` (default no-op) because the
  cached model already contains the updater output. The sample does not enable the cache; a host
  that does must rely on cache invalidation to pick up builder changes. [source
  `Model/ModelNodeGenerator.cs` lines 50-64, 113-122]
- `ModelNodesGeneratorUpdater<T>` also implements `ISupportCachedNodesGeneratorUpdater`; `T` must
  derive from `ModelNodesGeneratorBase`. [source `Model/ModelNodesGeneratorUpdater.cs`]

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

- `IModelColumn : IModelMemberViewItem`: `int Width`, `DevExpress.Data.ColumnSortOrder SortOrder`,
  `int SortIndex`, `int GroupIndex`, `GroupInterval GroupInterval` (line 146). `Index` comes from
  `IModelNode`.
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

## Still open

- Bands (`IModelListView.BandsLayout`, `IModelBandsLayout` is added as a child node at
  `ModelListViewNodesGenerator.cs` line 82). Not investigated further; phase 2 at the earliest.
- `ModelNodesGeneratorUpdater` registration timing vs `CustomizeTypesInfo`: updaters run during
  model generation, which happens after all modules' `CustomizeTypesInfo`. Confirmed indirectly by
  the sample (Order's spec resolves in the updater); not traced in source.
