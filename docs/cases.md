# Cases from generated applications

XafLayoutBuilder is a proof of concept for declaring XAF layouts in fluent C#. Applications that other
tools generate with it, BPG first, produce layouts nobody would write by hand for a sample. This is the
route by which such a layout becomes a test case here.

## When a generated application fails

BPG's pipeline test starts every generated application with `FailFastOnLayoutErrors` on, so a layout
the technique cannot handle fails there, at startup, with a numbered diagnostic. First decide whose
defect it is:

- **The generator's** (a member name that does not exist, a group id used twice, a spec the builder
  rightly rejects): it stays on the generator's board. XafLayoutBuilder did its job by refusing it.
- **The technique's** (a valid spec that the builder, an updater, the startup check or the exporter gets
  wrong, or an XLB diagnostic that blames the wrong thing): it comes here.

## What to bring

- The entity's generated `{Entity}.cs` and `{Entity}.Layout.cs`, or the registered spec as JSON
  (`LayoutSpecJson.Serialize`).
- The host output or the `eXpressAppFramework.log` lines with the XLB diagnostic and stack trace.
- The package version and the DevExpress version the application used.
- What the form or list should have looked like.

## How to reduce it

1. Reproduce with the smallest member set on a sample entity: add the members to `Customer`, `Order` or a
   new entity in `XafLayoutBuilder.Sample.Module`, never a copy of the generated class.
2. If the problem is in Core (a builder rule, validation, the printer or JSON), write a failing unit test
   in `XafLayoutBuilder.Tests` and stop there.
3. If it needs the Application Model, add a failing assertion to the E2E gate, with a fixture switch like
   `--break-layout` when the case must not change the normal sample.
4. Fix, run the gate, get a review, commit with the card id, and bump `PackageVersion` in
   `Directory.Build.props` before pushing the packages, so the generator can pick the fix up.

## Shapes to watch first

From reading a generated application (Driver) and BPG's generator, 2026-09-13:

- aggregated collections that land on tabs, and enums;
- calculated `PersistentAlias` properties: does XAF give them a view item, must they be placed or hidden?
- Image, File and rich-text properties, where BPG also writes editor settings into `Model.xafml` next to
  the builder layout;
- chart and pivot ListViews with non-default ids, which the builder must leave alone;
- types the generator does not own (`ApplicationUser`, `ReportDataV2`), which go through `LayoutRegistry`;
- large models, where the startup check's cost becomes visible (a 220-entity application hinted at it).
