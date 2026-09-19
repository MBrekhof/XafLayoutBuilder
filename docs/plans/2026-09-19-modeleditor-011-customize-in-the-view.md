# MODELEDITOR-011: the layout designer, from the Model Editor

Card #1703. Scope: `docs/model-editor-scope.md`, "Layout designer". DevExpress 26.1 source paths are under
`C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp`.

## What WinForms does

Selecting a view's `Layout` node in the WinForms Model Editor opens a drag-and-drop designer inside the editor
(`ModelEditorControl.ShowLayoutIfNeed` 513-525): a `WinLayoutManager` over the layout node, with fake data, so the form can
be arranged without opening a record.

## The premise this plan started with was wrong (Codex plan review, three P1s, verified in source and then measured)

The first draft said XAF Blazor's editor cannot be reached from a model node, because `LayoutEditor` takes the running
layout component and a DetailView needs an object, and that the helper types are internal. All three are false:

- `XafApplication.CreateDetailView(objectSpace, detailViewId, isRoot)` passes a null object on purpose
  (`XafApplication.cs` 2242-2249), and `DetailView`'s constructor accepts `obj == null` (`DetailView.cs` 194-207). **A
  DetailView can be built with no business object at all.**
- `LayoutEditorConfirmationController.LayoutEditorInstance` is public (45-69) and `LayoutEditor.ToggleCustomizationMode`
  is public (208-213), so customization can also be started programmatically once the editor exists.
- `LayoutEditorTreeMenu`, `LayoutEditorContextMenu`, the proxies and the DTOs are public types; only some members of
  `LayoutEditor` are internal. I had read internal *properties* as internal *types*.

**Measured** (spike through the gate, screenshots `spike-011-designer.png` and `spike-011-layouteditor.png`): a DetailView
built for `Order_DetailView` with no object renders the whole form from the builder's layout, all editors empty, and the
form's own context menu opens XAF's layout editor over it: the Customization window with the Layout Tree View (Main →
Order → Number, Customer, Order Date; Live group caption → Notes) and Hidden Items (ID, Sync Token). That is the WinForms
designer's job done by XAF's own editor, without a record.

Two things the spike had to get right, both measured:

- **The view must be root.** `DisableNestedLayoutEditorController` (`Blazor/Layout/LayoutEditor`) targets
  `Nesting.Nested` and switches customization off for a nested DetailView that is not the main window's edit view; created
  nested, the form rendered but its context menu had no Customize Layout.
- `BlazorLayoutManager.CustomizationFormEnabled` (64-67) must be set before the view is shown.

## Design

- `ModelEditing.DetailViewToCustomize(node)`: the `IModelDetailView` the node is or belongs to (the view node itself, or a
  `IModelViewLayout` whose parent is one). Null for everything else, so a ListView's columns and a DashboardView's layout
  (no class to build a view for) do not offer it.
- `ModelEditorController.ShowLayoutDesigner(application, modelDetailView)`: an object space for the view's class, a **root**
  DetailView with no object, `CustomizationFormEnabled = true`, shown in a popup; the object space is disposed with it.
- The component's "Customize layout" button refuses while `Session.HasPendingEdits`, because the layout editor saves the
  model itself; the message says to Save or Reload first.
- Not done (ponytail): starting customization mode for the user, so the form's context menu is one gesture rather than two.
  `BlazorLayoutManager.LayoutEditorCreated` and `.LayoutEditor` are internal (111-115), and the public
  `LayoutEditorConfirmationController.LayoutEditorInstance` is set only once the editor has rendered, so there is no clean
  moment to call `ToggleCustomizationMode` from outside; support request item 16.
- Not done: a columns designer for a ListView's `Columns` node. The WinForms one is an internal `GridListEditorDesigner`
  with fake data; in XAF Blazor the column chooser belongs to a rendered grid (`ColumnChooserController` acts through the
  grid instance), which the running ListView already offers, and the Model Editor edits column nodes directly.
- **Reset Layout**, the other half of the scope entry, needs no code: Reset node already undoes the selected Layout node.

## Codex diff review (2026-09-19): one P1, two P2s, all fixed

1. **P1: the button was offered in the shared editor**, where it cannot work: XAF's layout editor saves through
   `Application.SaveModelChanges`, the user's own differences, so a layout customized from Edit Shared Model would land in
   that administrator's model instead of the shared one. It is hidden there now.
2. **P2: Save and New.** The designer is a root DetailView, which in edit mode offers Save and New, so a user could create
   an empty business record from a layout designer. The view is read-only (`ViewEditMode.View`); customization belongs to
   the layout manager, not the view's edit mode, so the editor still runs. The gate asserts the button is absent.
3. **P3: the gate's right-click used a fixed offset**, which a layout change could move onto the Lines grid. It tries
   several offsets and asserts the menu appeared.

Checked clean: the node rule and its null cases; the object space and view lifetime on the ordinary closes (the popup
manager disposes the window, a root view disposes its object space, and the store guards repeated disposal), and no
conflict with an open session or with MODELEDITOR-016's suppression.

## Tests

Unit: `DetailViewToCustomize` gives the view for a DetailView node and for its Layout node, and null for a ListView's
Columns node, a column and a class node.

Gate: from `Views/Order_DetailView/Layout`, press Customize layout; the popup shows the form built without a record;
right-click the gap between the groups, choose Customize Layout, and XAF's Layout Tree View lists the builder's groups.
The gate reads the form's box and right-clicks the strip between the two group panels, which is where the form's own
context menu opens (the bottom strip is the Lines grid and swallows it).
