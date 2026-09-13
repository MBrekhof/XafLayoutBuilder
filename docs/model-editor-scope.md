# Runtime Model Editor for XAF Blazor: scope

What the XAF 26.1 WinForms Model Editor does, read from the installed sources on 2026-09-13, as the measure for the
Blazor editor in `XafLayoutBuilder.ModelEditor` (MODELEDITOR-001 onwards). Card ids name the card that covers a feature.

Paths: **W** = `DevExpress.ExpressApp.Win\Core\ModelEditor\`, **X** = `DevExpress.ExpressApp\`, **VC** =
`W\ModelEditorControllers\ModelEditorViewController.cs`. **[N]** logic uses model APIs only and can be reused or
ported; **[W]** is bound to XtraTreeList/XtraVerticalGrid/XtraLayout/Forms. **(Never)** = `[EditorBrowsable(Never)]`.

Every piece of the WinForms UI is **[W]**. What can be reused sits in `ModelNode`, `ModelEditorHelper` (the whole class
is (Never), `X\Model\ModelEditorHelper.cs` 50), the public `FastModelEditorHelper` (`X\Model\FastModelEditorHelper.cs`),
and model-only classes in the Win assembly that can be ported: `ExtendModelInterfaceAdapter`, `LinksNodeHelper`,
`ModelValidator`.

## Tree (MODELEDITOR-003)

- Caption: `FastModelEditorHelper.GetModelNodeDisplayValue` (349) reads `[DisplayProperty]`, falling back to Id;
  `ExtendModelInterfaceAdapter.GetDisplayPropertyValue` (`W\LinkCollection\ExtendModelInterfaceAdapter.cs` 383). [N]
- Image: `GetImage`/`GetSvgImage` (same file 367-376), ImageLoader. [W]
- Sort: `Index`, then display value (`W\LinkCollection\ModelTreeListNodeComparer.cs` 44).
- Modified nodes bold: `ModelTreeList.cs` 127-140 via `ModelNode.HasModification` ((Never), `ModelNode.cs` 885).
- Show/Hide Links (VC 224-246), Group/Ungroup (VC 2161-2176).
- Search: `W\Filter\SearchControl.cs` + `FilterModelTreeList` build a virtual tree matching display text
  case-insensitively (259), highlighting (336-342); incremental search in `ModelTreeList.cs` 116. [W], trivial to port.

## Property grid (MODELEDITOR-003)

- Descriptions: `SetDescription` (VC 843-880) → `FastModelEditorHelper.GetPropertyDescription`/`GetNodeDescription` →
  `ModelEditorHelper.GetPropertyDescription` (292): `[Description]` plus type and owner interfaces. [N]
- Categories and alphabetic sort; row icons for localizable, key, required (`ModelAttributesPropertyGridHelper.cs` 265-280).
- Visible: `CalculatePropertyVisible` (505-540): `[Browsable]`, `FastModelEditorHelper.IsPropertyModelBrowsableVisible`
  (114-140), `ModelHideProperties`, `ModelVirtualTreeDisplayProperties`, `Index` hidden under the root. [N]
- Read-only: `FastModelEditorHelper.IsReadOnly` (185-234); key/Id read-only unless `IsNewValueModified` (Never); the
  message from `ModelReadOnlyAttribute.Message` (322). [N]
- Modified values bold: `ModelNode.IsValueModified` ((Never), 899), the writable layer only. Required headers bold
  italic, calculated/link headers underlined (104-128).
- Reset value: `ResetProperty` (680-687) → `ModelNode.ClearValue`. In XAF Blazor's warmed-up model ClearValue leaves the
  value cache stale (MODELEDITOR-002, docs/api-notes.md).

## Node operations (MODELEDITOR-004)

- Add child: VC 2222-2286; items from `LinksNodeHelper.GetCreatableItems` (`W\LinkCollection\LinksNodeHelper.cs` 53) →
  `FastModelEditorHelper.GetChildNodeTypes` (294: list child types plus `ModelVirtualTreeAddItemAttribute`, nothing for
  a read-only node), filtered by `ModelVirtualTreeCreatableItemsFilterAttribute` and `...RequiredPathFilterAttribute`
  (LinksNodeHelper 76-117); `ModelNode.AddNode(id, type)`. A new `IModelMember` gets `IsCustom` and `IsCalculated`
  (VC 881-886); the new node must pass validation.
- Delete: VC 1900-1930 with confirmation; `FastModelEditorHelper.CanDeleteNode` (235); `IModelNode.Remove()`.
- Copy/Paste/Clone: VC 1986, 2003, 2199 → `ModelEditorHelper.AddCloneNode` (388, (Never)) → `ModelNode.AddClonedNode`;
  enabled by `FastModelEditorHelper.CanAddNode` (312).
- Move/reorder: drag-and-drop `ModelEditorDragDropController.cs` (move = clone + delete 127-139, Ctrl copy 240-250,
  Shift reorder via internal `ChangeNodeIndex` setting `Index`); Up/Down VC 2063-2136.
- Reset node differences: `ModelNode.Undo()` (609), enabled on `HasModification` (VC 1936-1985, 2539).
- Generate Content: VC 2033-2056; `ModelEditorHelper.IsGenerateContentNode` (231), `GenerateContent` (242-257). (MODELEDITOR-009)

## Lookups, types, navigation (MODELEDITOR-005)

- Lookup lists: `CreateCustomRepositoryItem` (800-849): `ModelValueInfo.PersistentPath` (`[DataSourceProperty]`) →
  `ModelNodePersistentPathHelper.FindValueByPath`; `[DataSourceCriteria]` through `CriteriaWrapper` +
  `ExpressionEvaluator` (448-478); "(none)" unless Required; views sorted by `ViewNamesCalculator.SortByInheritanceHierarchy`.
- Field pickers for `PropertyName`, `LookupProperty`, `TargetPropertyName` (`W\AttributeList\ModelNodeLookup.cs`
  191, 399-447); `PreferredLanguage` combo (388).
- Type values: `TypeConverter(StringToTypeConverterBase/Ex)` on model properties (`X\Model\CommonInterfaces.cs` 204,
  273, 554, 643). [N]
- Navigation: Open Related Object (`node.GetValue(prop) as ModelNode`, 742), Open Source Property (`RefValue` 339-387,
  `ModelValueCalculatorAttribute`, `NodeInfo.GetSourceNodePath`), Go to Source (VC 1030, 2158), Back/Forward
  (VC 1064-1079), "View in Model" from a running view (`Win\SystemModule\ViewInModelController.cs` 125-137).

## Special editors (MODELEDITOR-006)

- Criteria: `CriteriaModelEditorControl` (`W\AttributeList\CriteriaModelEditorControl.cs` 61-245), `[CriteriaOptions]
  .ObjectTypeMemberName` → `ITypeInfo` → XtraFilterControl; declared on `IModelListView.cs` 102, 107,
  `CommonInterfaces.cs` 434, 604, 608, `IModelDashboardView.cs` 63. [W]
- Expression: `ExpressionModelEditorControl` (CommonInterfaces.cs 295). [W]
- Multiline strings (`ModelPropertyEditorInterfaces.cs` 64).
- Image gallery: `ImageGalleryModelEditorControl.cs` 44-58, ImageLoader. [W]
- Mask: `MaskModelEditorControl.cs` 110, attached by the Win extender only. [W]

## Validation (MODELEDITOR-007)

- `W\ModelValidator.cs` 47-93 [N]: rule set from type rules plus `RuleRequiredField` for `FastModelEditorHelper.IsRequired`
  (169) and key properties, hidden properties dropped, only nodes with `HasModification || IsNewNode`.
- `ValidateNode` (VC 1299) on focus change, expand, save, navigation and add; blocks the action; row errors plus a
  popup (VC 1378-1443). No global error list.
- "Show Unusable Data" (VC 946-969, `GetFullUnusableModel` (Never)); unusable-nodes warning on save (732-737).

## Localization (MODELEDITOR-008)

- Language combo: VC 1054 → `SetCurrentAspectByName` (1486) sets `ModelApplicationBase.CurrentAspectProvider.CurrentAspect`;
  names from `ModelEditorHelper.GetAspectNames` (354). Disabled while a layout designer shows (VC 2377).
- Languages Manager: `ShowCulturesManager` (VC 1446-1482) → `ModelApplicationBase.AddAspect` (`ModelApplication.cs` 368).
- Localization window `W\Localization\LocalizationController.cs`: grid of localizable values, filters (504-532),
  Bing translation, CSV import/export, mark as translated, undo = ClearValue in the default aspect
  (`LocalizationItem.cs` 127-137). Items (983-1040) mostly [N].

## Differences, merge, modules (MODELEDITOR-009)

- Show differences XML: VC 1080, 2187 → `ModelEditorHelper.GetNodeInLayer(node, LastLayer).Xml`, current aspect only.
- Merge Differences: VC 2140-2157, 2289-2319 → `ModelEditorHelper.MoveNodeToOtherLayer` (393, (Never)) → internal
  `ModelNode.MoveNodeToOtherLayer` (3552) into the application `Model.xafml` store (`WinApplication.cs` 503-508);
  disabled in design mode and standalone. No layers view.
- Loaded Modules grid (`ModelEditorControl.cs` 470).

## Shared differences, save and refresh (MODELEDITOR-010)

- WinForms saves only the user layer: `CreateUserModelDifferenceStore()` → `FileModelStore(..., "Model.User")`
  (`WinApplication.cs` 439-444), `SaveDifference(LastLayer)` (VC 721-743); the application store only through Merge.
- Reload: `ModelEditorHelper.RereadLastLayer` (Never) → `ApplicationModelManager.RecreateModel` (protected internal,
  `ApplicationModelsManager.cs` 310). Closing asks Save / discard (`ModelEditorForm.cs` 91-121).
- Refresh: `WinApplication.EditModel` (782-817) closes all windows, edits the live model in place, then calls
  `ApplicationModelManager.AddCustomMembersFromModelToTypeInfo(Model)`, `RefreshShowViewStrategy` and
  `ShowStartupWindow()`; `EditModelController` clears the enum descriptor cache (63-65). The model is not rebuilt;
  every window, view and controller is.
- XAF Blazor keeps administrator (shared) differences in `ModelDifferences` rows without a user; they sit below the user
  layer and are not writable through the live model.

## Layout designer (MODELEDITOR-011)

- `ModelEditorControl.ShowLayoutIfNeed` (513-525): `IModelViewLayout` → `ModelEditorLayoutManagerProvider` (WinLayoutManager
  with a customization form, writes with `layoutManager.SaveModel()`); `IModelColumns` → internal `GridListEditorDesigner`
  with fake data; Reset Layout = `ModelNode.Undo()` on the layout node (VC 700-720). Fully [W]. XAF Blazor has its own
  in-view layout editor (`DevExpress.ExpressApp.Blazor/Layout/LayoutEditor`).

## Not in WinForms either

No undo/redo stack (per-value ClearValue, per-node Undo, Reload), no global error list, no layers view, `FindAndFocusEntry`
is a stub (VC 1587). Editor settings (splitters, expanded nodes, language) live in `IModelModelEditorSettings`
(`WinApplication.cs` 509). Security gate: `ModelOperationPermissionRequest` (`Win\SystemModule\EditModelController.cs` 72-83).
