# XafLayoutBuilder

Typed, compile-checked C# for DevExpress XAF view layouts, poured into the Application Model as
generated-layer defaults. The visual designer stays the running Blazor app; the source of truth
becomes a fluent builder next to the business class, not `Model.xafml`.

**Status: proof of concept, session 1 of 7.** The solution builds, the sample app runs with
XAF's default layouts, and the E2E gate logs in. No builder yet. See
`XafLayoutBuilder-START.md` for the design and `SESSION_HANDOFF.md` for progress.

```bash
dotnet build XafLayoutBuilder.slnx
dotnet test XafLayoutBuilder.Tests
dotnet run --project XafLayoutBuilder.E2ETests
```

Requires .NET 10 SDK, DevExpress 26.1 NuGet feed, SQL Server LocalDB.
