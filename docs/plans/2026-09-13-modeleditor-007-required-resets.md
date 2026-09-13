# MODELEDITOR-007 required resets

**Goal:** A required reset must not save a previously created custom view without its class.

**Design:** Extend the editor's existing conservative rule for required resets on newly added nodes to saved user-created nodes and their descendants. These nodes have no independent generated node to fall back to; calculators may supply a value, but the warmed-up model cannot reliably preview it after a reset. Keep the edit pending, mark the required value missing, and refuse Save before changing the differences. Supplying the value explicitly repairs the edit. Preserve resets on generated nodes and optional values.

**Tech stack:** C#, .NET 10, XAF 26.1.4, EF Core, xUnit and C# Playwright.

1. Add warmed-up-model regression tests for Reset and the empty required-reference choice on a saved custom DetailView; verify refusal leaves its XML intact and an explicit value repairs the edit. Add a control for a generated view's inherited ModelClass.
2. Run the regression tests before the fix and confirm the missing refusal.
3. Update `ModelEditing.MissingRequired` to recognize required clears on or below user-created nodes, using verified model metadata without mutating the live model to probe a fallback.
4. Add a browser regression for a required reset on the already-saved custom column in the gate, including reload verification and a screenshot.
5. Record the limitation and API evidence in `docs/api-notes.md`, update the MODELEDITOR-007 changelog line, and run build, unit tests and the complete E2E gate.
6. Review the final diff and update the README and session handoff. Leave all changes uncommitted.
