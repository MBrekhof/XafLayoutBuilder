using System.Diagnostics;
using Microsoft.Playwright;

// E2E phase gate for XafLayoutBuilder (start document section 8). Same shape as XafReportScheduler's harness.
// Run with:  dotnet run --project XafLayoutBuilder.E2ETests
// Exit codes: 0 pass, 1 fail, 2 Playwright browser missing.
//
// Builds and starts the sample on :5100, logs in as Admin, then:
//   E2E 1   Order_DetailView renders the builder layout (hidden member absent, group and tab order, collapsible group)
//   E2E 2   Order_ListView column order, OrderDate-descending sort, hidden column offered by the column chooser
//   E2E 3   Order_LookupListView (via ServiceOrder.OriginalOrder) shows only Number, Customer
//   E2E 5a  exporting the untouched layout reproduces Order.Layout.cs; Customer's column caption round-trips
//   E2E 4   a user-layer difference that moves OrderDate into Details wins over the builder
//   E2E 5   Export Layout To Code prints OrderDate under Details
//   E2E 6   deleting the user differences brings the builder layout back
//   Session 5: the host started with --break-layout exits at startup with XLB001
// Writes Admin's ModelDifferences rows in the LocalDB catalog XafLayoutBuilder.Sample, restarting the host around
// those writes, and leaves Admin's user model empty. Screenshots: bin/Debug/net10.0/screenshots.

const string BaseUrl = "http://localhost:5100";
const string ConnectionString = @"Data Source=(localdb)\mssqllocaldb;Integrated Security=SSPI;Initial Catalog=XafLayoutBuilder.Sample;Encrypt=False";

var repoRoot = FindRepoRoot();
var blazorProj = Path.Combine(repoRoot, "XafLayoutBuilder.Sample.Blazor.Server");
var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
Directory.CreateDirectory(screenshotDir);
Console.WriteLine($"Screenshots: {screenshotDir}");

Process? app = null;
IPage? page = null;
IPlaywright? playwright = null;
IBrowser? browser = null;
var appOutput = new System.Text.StringBuilder();
var failed = false;
var missingBrowser = false;

try
{
    Step("Build Blazor.Server");
    RunOrThrow("dotnet", $"build \"{blazorProj}\" -v q --nologo");

    Step("Start Blazor app on :5100");
    // Codex review (session 1): a foreign process already serving :5100 would let the gate pass
    // without ever starting this checkout's host. Refuse to run against an occupied port.
    if (await IsServing()) throw new Exception($"{BaseUrl} is already serving before the harness started its host; stop that process first.");
    app = StartApp(blazorProj, appOutput);
    await WaitForHttpOk(app, appOutput);

    Step("Launch Chromium");
    playwright = await Playwright.CreateAsync();
    try
    {
        browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    }
    catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
    {
        missingBrowser = true;
        throw new Exception("Chromium not installed. Run: pwsh XafLayoutBuilder.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium", ex);
    }
    page = await NewPage(browser);

    Step("Log in as Admin");
    await Login(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-01-home.png") });

    Step("Open Order ListView and assert the seeded rows render");
    await page.GotoAsync($"{BaseUrl}/Order_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle });
    // Wait on seeded text, not a DevExpress grid CSS class: those change between releases.
    await page.GetByText("ORD-001", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 30_000 });
    var body = await page.InnerTextAsync("body");
    Assert(body.Contains("ORD-001"), "ListView shows ORD-001");
    Assert(body.Contains("SRV-001"), "ListView shows the ServiceOrder SRV-001 (inheritance pair appears in the base list)");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-02-order-listview.png") });

    Step("E2E 1: Order_DetailView renders the builder layout");
    await page.GetByText("ORD-001", new() { Exact = true }).First.ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("Order_DetailView", StringComparison.OrdinalIgnoreCase), new() { Timeout = 20_000 });
    // NetworkIdle fires while the ListView is still on screen; wait for the form to bind ORD-001
    // into an editor before reading the DOM, otherwise the grid's column headers satisfy the assert.
    await page.WaitForFunctionAsync("() => [...document.querySelectorAll('input')].some(i => i.value === 'ORD-001')",
        null, new() { Timeout = 30_000 });
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-03-order-detailview.png") });
    await File.WriteAllTextAsync(Path.Combine(screenshotDir, "e2e-03-order-detailview.html"), await page.ContentAsync());
    // The ListView stays in the DOM on its own (inactive) tab, so scope every assertion to the detail form.
    var form = page.Locator(".detail-view-content").First;
    var detailText = await form.InnerTextAsync();
    Assert(!detailText.Contains("Sync Token"), "SyncToken is not in the detail form (Hide)");
    Assert(await form.Locator("label.xaf-item-synctoken, .xaf-item-synctoken").CountAsync() == 0, "no SyncToken editor element exists in the form DOM");
    // Main group's row holds the top-level nodes in builder order: Header (caption Order), Details (caption from Notes), tabbed group.
    var topLevel = await form.EvaluateAsync<string[]>(@"f => {
        const main = f.querySelector('[role=group].dxbl-fl-group');
        return [...main.querySelector(':scope > .dxbl-row').children].map(c =>
            c.classList.contains('dxbl-fl-gt') ? 'tabs' : (c.querySelector(':scope > .dxbl-group > .dxbl-group-header')?.innerText.trim() ?? 'group'));
    }");
    Assert(string.Join(",", topLevel) == "Order,Notes,tabs", $"top-level layout nodes are Header, Details, Tabs in that order (got {string.Join(",", topLevel)})");
    var tabTitles = await form.EvaluateAsync<string[]>("f => [...f.querySelectorAll('.dxbl-fl-gt .dxbl-tabs-item')].map(t => t.innerText.trim())");
    Assert(string.Join(",", tabTitles) == "Lines,Attachments", $"tabbed group has exactly the tabs Lines, Attachments in that order (got {string.Join(",", tabTitles)})");
    var iNumber = detailText.IndexOf("Number", StringComparison.Ordinal);
    var iNotes = detailText.IndexOf("Notes", StringComparison.Ordinal);
    var iLines = detailText.IndexOf("Lines", StringComparison.Ordinal);
    Assert(iNumber >= 0 && iNotes > iNumber && iLines > iNotes, $"groups render in builder order Header < Details < Tabs ({iNumber},{iNotes},{iLines})");
    var groups = await form.EvaluateAsync<string[]>(@"f => [...f.querySelectorAll('[role=group].dxbl-fl-group')].map(g => {
        const h = g.querySelector(':scope > .dxbl-group > .dxbl-group-header');
        return (h ? h.innerText.trim() : '(no header)') + ' | ' + g.className + ' | aria-expanded=' + g.getAttribute('aria-expanded')
             + ' | header=' + (h ? h.className + ' btn=' + !!h.querySelector('button, [role=button]') : '-');
    })");
    foreach (var g in groups) Console.WriteLine("    group: " + g);
    var notesInCollapsible = await form.EvaluateAsync<bool>(@"f => {
        const label = f.querySelector('label.xaf-item-notes');
        const group = label && label.closest('[role=group]');
        // A captioned group also has a header and aria-expanded; only a collapsible one has the toggle button in it.
        const header = group && group.querySelector(':scope > .dxbl-group > .dxbl-group-header');
        return !!header && !!header.querySelector('button, [role=button]');
    }");
    Assert(notesInCollapsible, "Notes is inside a collapsible group (group header has the collapse toggle)");
    var headerNotCollapsible = await form.EvaluateAsync<bool>(@"f => {
        const header = f.querySelector('label.xaf-item-number').closest('[role=group]').querySelector(':scope > .dxbl-group > .dxbl-group-header');
        return !!header && !header.querySelector('button, [role=button]');
    }");
    Assert(headerNotCollapsible, "Header group shows its caption but has no collapse toggle");

    Step("E2E 2: Order_ListView columns, order, sort, column chooser");
    await page.GotoAsync($"{BaseUrl}/Order_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByText("ORD-001", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 30_000 });
    // Other tabs (Users, the DetailView) keep their grids in the DOM; scope to the active tab panel.
    var grid = page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First;
    var allGrids = await page.EvaluateAsync<string[]>(@"() => [...document.querySelectorAll('.dxbl-grid')].map(g => [...g.querySelectorAll('th.dxbl-grid-header')].map(h => h.textContent.trim().replace(/\s+/g,' ')).join('|'))");
    foreach (var g in allGrids) Console.WriteLine("    grid headers in DOM: " + g);
    // Header cells include the filter button's a11y text ("No filter applied"); strip it.
    var headers = await grid.EvaluateAsync<string[]>(@"g => [...g.querySelectorAll('th.dxbl-grid-header')].map(h => h.textContent.replace(/No filter applied/g,'').trim().replace(/\s+/g,' ')).filter(t => t && t !== 'Selection')");
    Console.WriteLine("    headers: " + string.Join(" | ", headers));
    Assert(string.Join(",", headers) == "Number,Customer,Order Date", $"columns are Number, Customer, Order Date in that order (got {string.Join(",", headers)})");
    var firstCells = await grid.EvaluateAsync<string[]>("g => [...g.querySelectorAll('tr[role=row]')].map(r => r.querySelector('td.xaf-action')?.innerText.trim()).filter(t => t)");
    Console.WriteLine("    rows: " + string.Join(" | ", firstCells));
    Assert(string.Join(",", firstCells) == "ORD-003,ORD-001,SRV-001,ORD-002", $"rows sorted by OrderDate descending (got {string.Join(",", firstCells)})");
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-04-order-listview-columns.png") });
    // Column chooser: XAF Blazor exposes it as the ColumnChooser action (image-only, HiddenActions container).
    await grid.Locator("th.dxbl-grid-header").Filter(new() { HasText = "Number" }).First.ClickAsync(new() { Button = MouseButton.Right });
    await page.WaitForTimeoutAsync(800);
    var menuItems = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.dxbl-context-menu-item, .dxbl-menu-item, [role=menuitem]')].map(m => m.innerText.trim()).filter(t => t)");
    Console.WriteLine("    header context menu: " + string.Join(" | ", menuItems));
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-05-header-menu.png") });
    var chooserItem = page.Locator("[role=menuitem], .dxbl-context-menu-item, .dxbl-menu-item").Filter(new() { HasText = "Column Chooser" }).First;
    Assert(await chooserItem.CountAsync() > 0, "header context menu offers Column Chooser");
    await chooserItem.ClickAsync();
    // The chooser's title lives outside the element that lists the columns; find the list by a known column caption.
    var chooser = page.Locator(".dxbl-popup, .dxbl-grid-column-chooser, .dxbl-column-chooser").Filter(new() { HasText = "Order Date" }).First;
    await chooser.WaitForAsync(new() { Timeout = 10_000 });
    var chooserText = await chooser.InnerTextAsync();
    Console.WriteLine("    column chooser text: " + chooserText.Replace("\n", " / "));
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-06-column-chooser.png") });
    Assert(chooserText.Contains("Sync Token"), "SyncToken is offered in the column chooser (hidden, not removed)");
    await page.Keyboard.PressAsync("Escape");

    Step("E2E 3: Order_LookupListView shows only Number and Customer");
    // ServiceOrder.OriginalOrder is a plain reference to Order, so its editor uses Order_LookupListView.
    await page.GotoAsync($"{BaseUrl}/Order_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByText("SRV-001", new() { Exact = true }).First.ClickAsync();
    await page.WaitForFunctionAsync("() => [...document.querySelectorAll('input')].some(i => i.value === 'SRV-001')", null, new() { Timeout = 30_000 });
    var activeForm = page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First;
    var lookupItem = activeForm.Locator(".dxbl-fl-item").Filter(new() { Has = page.Locator("label.xaf-item-originalorder") }).First;
    await lookupItem.WaitForAsync(new() { Timeout = 15_000 });
    async Task<string[]> Buttons() => await lookupItem.EvaluateAsync<string[]>("i => [...i.querySelectorAll('button')].map(b => b.className + ' title=' + (b.title || b.getAttribute('aria-label') || ''))");
    foreach (var b in await Buttons()) Console.WriteLine("    lookup button: " + b);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-07-serviceorder-detail.png") });
    await lookupItem.Locator("button").First.ClickAsync(); // view mode -> edit mode (LookupPropertyEditor.DefaultUseViewMode)
    await page.WaitForTimeoutAsync(1000);
    foreach (var b in await Buttons()) Console.WriteLine("    lookup button (edit mode): " + b);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-08-order-lookup-editmode.png") });
    // The lookup dropdown is a .dxbl-dropdown holding a grid of Order_LookupListView's columns. Several other
    // (filter-menu) dropdowns exist in the DOM, so identify it by content. Its grid has no <th> headers; the
    // first text line is the header row, tab-separated.
    // Clicking the dropdown button opens it and a re-render closes it again; Alt+ArrowDown from the input keeps it open.
    var lookupDropdown = page.Locator(".dxbl-dropdown:not(.dxbl-popup-hidden)").Filter(new() { HasText = "ORD-001" }).First;
    await page.WaitForTimeoutAsync(1500);
    string[] dropdownLines = [];
    for (var attempt = 0; attempt < 3 && dropdownLines.Length == 0; attempt++) {
        await lookupItem.Locator("input").First.FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowDown");
        try {
            await lookupDropdown.WaitForAsync(new() { Timeout = 5000 });
            dropdownLines = (await lookupDropdown.InnerTextAsync(new() { Timeout = 5000 })).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (TimeoutException) { Console.WriteLine($"    dropdown not open after attempt {attempt + 1}"); }
    }
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-09-order-lookup-open.png") });
    Assert(dropdownLines.Length > 0, "the Original Order lookup dropdown opened");
    var lookupHeaders = dropdownLines[0].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    Console.WriteLine("    lookup headers: " + string.Join(" | ", lookupHeaders));
    Assert(string.Join(",", lookupHeaders) == "Number,Customer", $"lookup shows only Number and Customer (got {string.Join(",", lookupHeaders)})");
    Assert(!string.Join("\n", dropdownLines).Contains("Order Date"), "lookup does not show Order Date");
    Assert(dropdownLines.Any(l => l.Contains("Acme Corp")), "lookup rows show the Customer column's values");
    await page.Keyboard.PressAsync("Escape");

    Step("E2E 5a: exporting the untouched builder layout reproduces the source (section 6 round trip)");
    await OpenOrd001Detail(page);
    var roundTrip = await ExportLayoutCode(page, Path.Combine(screenshotDir, "e2e-09b-export-roundtrip.png"));
    await File.WriteAllTextAsync(Path.Combine(screenshotDir, "e2e-09b-exported-roundtrip.cs"), roundTrip);
    await ClosePopup(page);
    var layoutSource = await File.ReadAllTextAsync(Path.Combine(repoRoot, "XafLayoutBuilder.Sample.Module", "BusinessObjects", "Order.Layout.cs"));
    Assert(Squash(BuilderExpression(roundTrip, "LayoutBuilder<Order>.Create()")) == Squash(BuilderExpression(layoutSource, "LayoutBuilder<Order>.Create()")),
        "exported DetailView builder equals the one in Order.Layout.cs (modulo whitespace)");
    var sourceColumns = BuilderExpression(layoutSource, "ListViewColumnsBuilder<Order>.Create()");
    var exportedColumns = BuilderExpression(roundTrip, "ListViewColumnsBuilder<Order>.Create()");
    Assert(Squash(WithoutHideCalls(exportedColumns)) == Squash(WithoutHideCalls(sourceColumns)),
        "exported columns and lookup equal the source apart from Hide calls");
    var exportedHides = HideCalls(exportedColumns);
    Assert(HideCalls(sourceColumns).All(exportedHides.Contains),
        $"every column the source hides is hidden in the export (export hides: {string.Join(" ", exportedHides)})");
    // Column captions are localizable model values; this is the case that needs the exporter's default-caption rule.
    await page.GotoAsync($"{BaseUrl}/Customer_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByText("Acme Corp", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 30_000 });
    var customerHeaders = await page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First.EvaluateAsync<string[]>(
        @"g => [...g.querySelectorAll('th.dxbl-grid-header')].map(h => h.textContent.replace(/No filter applied/g,'').trim().replace(/\s+/g,' ')).filter(t => t && t !== 'Selection')");
    Assert(string.Join(",", customerHeaders) == "Customer name,City", $"Customer_ListView shows the captioned column (got {string.Join(",", customerHeaders)})");
    var customerExport = await ExportLayoutCode(page, Path.Combine(screenshotDir, "e2e-09c-export-customer.png"));
    await ClosePopup(page);
    Assert(customerExport.Contains(".Column(x => x.Name, caption: \"Customer name\")"), "the column caption round-trips through the export");
    Assert(customerExport.Contains("public static DetailLayoutSpec? BuildDetailViewLayout() =>"), "export prints the Customer class");

    Step("E2E 4: the user layer wins: Admin moves OrderDate into Details");
    // XAF Blazor's layout editor persists its result through Application.SaveModelChanges into the user
    // ModelDifference store (ContextId "Blazor"). Driving its drag-and-drop with Playwright is out of proportion for
    // the POC, so the harness writes the same difference XML the editor would. E2E 6 deletes it again.
    var adminId = SqlScalar("SELECT LOWER(CAST(ID AS NVARCHAR(36))) FROM PermissionPolicyUser WHERE UserName = 'Admin'")
        ?? throw new Exception("Admin user not found in the sample database");
    const string MoveOrderDateXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Application>
          <Views>
            <DetailView Id="Order_DetailView">
              <Layout>
                <LayoutGroup Id="Main">
                  <LayoutGroup Id="Header">
                    <LayoutItem Id="OrderDate" Removed="True" />
                  </LayoutGroup>
                  <LayoutGroup Id="Details">
                    <LayoutItem Id="OrderDate" ViewItem="OrderDate" Index="1" IsNewNode="True" />
                  </LayoutGroup>
                </LayoutGroup>
              </Layout>
            </DetailView>
          </Views>
        </Application>
        """;
    KillApp(ref app); // the running host's deferred save would otherwise flush its in-memory user model over our rows
    ResetUserModel(adminId);
    Sql($"""
        DECLARE @d UNIQUEIDENTIFIER = NEWID();
        INSERT INTO ModelDifferences (ID, UserId, ContextId, Version, GCRecord) VALUES (@d, '{adminId}', 'Blazor', 0, 0);
        INSERT INTO ModelDifferenceAspects (ID, Name, Xml, OwnerID, GCRecord) VALUES (NEWID(), '', @xml, @d, 0);
        """, ("@xml", MoveOrderDateXml)); // GCRecord = 0: XAF's deferred-deletion query filter hides NULL rows
    app = await RestartApp(app, blazorProj, appOutput);
    await OpenOrd001Detail(page);
    Console.WriteLine("    user diff rows for Admin: " + SqlScalar($"SELECT COUNT(*) FROM ModelDifferences WHERE UserId = '{adminId}'")
        + ", aspect mentions OrderDate: " + SqlScalar($"SELECT MAX(CASE WHEN CAST(a.Xml AS NVARCHAR(MAX)) LIKE '%OrderDate%' THEN 1 ELSE 0 END) FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}'"));
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-10-user-layer-orderdate-in-details.png") });
    Assert(await OrderDateGroup(page) == "Details", "after reload OrderDate renders inside the Details group (user layer over builder)");

    Step("E2E 5: Export Layout To Code shows the merged layout as builder C#");
    var exported = await ExportLayoutCode(page, Path.Combine(screenshotDir, "e2e-11-export-popup.png"));
    await File.WriteAllTextAsync(Path.Combine(screenshotDir, "e2e-11-exported-Order.Layout.cs"), exported);
    Console.WriteLine("    exported:\n" + string.Join("\n", exported.Split('\n').Select(l => "      " + l.TrimEnd())));
    Assert(exported.Contains("public partial class Order : ISupportViewLayoutCustomization"), "export is the Order partial class");
    Assert(System.Text.RegularExpressions.Regex.IsMatch(exported, @"\.Group\(""Details""[\s\S]*?\.Item\(x => x\.OrderDate[,)]"),
        "exported code places OrderDate in the Details group");
    var headerBlock = exported[exported.IndexOf(".Group(\"Header\"", StringComparison.Ordinal)..exported.IndexOf(".Group(\"Details\"", StringComparison.Ordinal)];
    Assert(!headerBlock.Contains("OrderDate"), "exported Header group no longer contains OrderDate");
    Assert(exported.Contains(".TabFor(x => x.Lines, imageName: \"BO_Order_Item\")") && exported.Contains(".Hide(x => x.SyncToken)"),
        "export keeps the builder's tabs and hidden member");
    Assert(exported.Contains(".Caption(\"Order\")") && !exported.Contains(".Caption(\"Details\")") && !exported.Contains(".Caption(\"Lines\")"),
        "export prints the explicit Header caption and not XAF's computed captions");
    Assert(!exported.Contains(".Hide(x => x.ID)"), "export does not list the key as a hidden column");
    Assert(exported.Contains(".Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)") && exported.Contains(".Lookup(l => l"),
        "export includes the ListView columns and the lookup");
    await ClosePopup(page);

    Step("E2E 6: resetting the user model brings the builder layout back");
    KillApp(ref app);
    ResetUserModel(adminId);
    app = await RestartApp(app, blazorProj, appOutput);
    await OpenOrd001Detail(page);
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-12-user-layer-reset.png") });
    Assert(await OrderDateGroup(page) == "Header", "after reset OrderDate renders inside the Header group again");

    Step("Session 5: a broken layout is reported at startup (host started with --break-layout)");
    KillApp(ref app);
    lock (appOutput) appOutput.Clear();
    // The XAF Blazor host builds the application (and so the model) while the host starts, so the diagnostic
    // kills the process before it ever listens. Expect: no HTTP, non-zero exit, XLB001 in the output.
    app = StartApp(blazorProj, appOutput, "--break-layout");
    var exited = app.WaitForExit(90_000);
    if (exited) app.WaitForExit(); // flushes the async stdout/stderr readers
    string log; lock (appOutput) log = appOutput.ToString();
    var line = log.Split('\n').FirstOrDefault(l => l.Contains("XLB001"))?.Trim() ?? "(not in app output)";
    Console.WriteLine("    app output: " + line);
    Assert(exited, "host process exits instead of serving");
    Assert(app.ExitCode != 0, $"host exit code is non-zero (got {app.ExitCode})");
    Assert(!await IsServing(), "nothing is serving on :5100 after the failed start");
    Assert(line.Contains("XLB001"), "XLB001 is reported in the host output");
    Assert(line.Contains("Customer_DetailView") && line.Contains("InternalCode"), "the diagnostic names the view id and the member");

    Console.WriteLine("\n=== E2E PASSED ===");
}
catch (Exception ex)
{
    failed = true;
    Console.WriteLine($"\n=== E2E FAILED ===\n{ex}");
    Console.WriteLine("\n--- app stdout/stderr (last 60 lines) ---");
    lock (appOutput) Console.WriteLine(string.Join('\n', appOutput.ToString().Split('\n').TakeLast(60)));
    if (page is not null)
    {
        Console.WriteLine($"\n--- page URL at failure: {page.Url}");
        try
        {
            var bodyText = (await page.InnerTextAsync("body")).Trim().Replace("\n", " ");
            Console.WriteLine($"--- body text (first 800 chars): {bodyText[..Math.Min(800, bodyText.Length)]}");
            var debugPath = Path.Combine(screenshotDir, $"e2e-failure-{DateTime.Now:HHmmss}.png");
            await page.ScreenshotAsync(new() { Path = debugPath });
            Console.WriteLine($"--- debug screenshot: {debugPath}");
        }
        catch (Exception diagEx) { Console.WriteLine($"--- diagnostics failed: {diagEx.Message}"); }
    }
}
finally
{
    Step("Stop app, free port 5100");
    if (browser is not null) { try { await browser.CloseAsync(); } catch { /* best-effort */ } }
    playwright?.Dispose();
    KillApp(ref app);
}
return missingBrowser ? 2 : (failed ? 1 : 0);

// ---------- helpers ----------

static void Step(string name) => Console.WriteLine($"\n--- {name}");

static void Assert(bool condition, string what)
{
    if (!condition) throw new Exception($"Assert failed: {what}");
    Console.WriteLine($"    [ok] {what}");
}

static async Task Login(IPage page)
{
    // Blazor Server circuit-connect race: DOMContentLoaded fires on the static shell before
    // the SignalR circuit attaches handlers, so an early Fill() can be dropped server-side.
    // Wait for NetworkIdle and verify the value actually bound before submitting.
    await page.GotoAsync($"{BaseUrl}/LoginPage", new() { WaitUntil = WaitUntilState.NetworkIdle });
    var userField = page.Locator("input[type='text'], input[name*='sername']").First;
    await userField.WaitForAsync(new() { Timeout = 20_000 });
    for (var i = 0; i < 10; i++)
    {
        await userField.FillAsync("Admin");
        if (await userField.InputValueAsync() == "Admin") break;
        await Task.Delay(300);
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Log In" }).ClickAsync();
    await page.WaitForURLAsync(url => !url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase), new() { Timeout = 20_000 });
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });
}

// A fresh page load starts a new Blazor circuit, which reloads the user model differences.
static async Task OpenOrd001Detail(IPage page)
{
    // A fresh circuit may restore the last open view (DocumentManagerState) and interrupt the first navigation.
    for (var attempt = 0; attempt < 3; attempt++) {
        try { await page.GotoAsync($"{BaseUrl}/Order_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle }); }
        catch (PlaywrightException ex) when (ex.Message.Contains("interrupted")) { await page.WaitForLoadStateAsync(LoadState.NetworkIdle); continue; }
        if (page.Url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase)) { await Login(page); continue; }
        if (page.Url.Contains("Order_ListView", StringComparison.OrdinalIgnoreCase)) break;
    }
    await page.GetByText("ORD-001", new() { Exact = true }).First.ClickAsync();
    await page.WaitForFunctionAsync("() => [...document.querySelectorAll('input')].some(i => i.value === 'ORD-001')", null, new() { Timeout = 30_000 });
}

// "Header" when OrderDate shares its group with Number, "Details" when it shares it with Notes, else the group's id-ish header.
static async Task<string> OrderDateGroup(IPage page) =>
    await page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First.EvaluateAsync<string>(@"f => {
        const g = m => f.querySelector('label.xaf-item-' + m)?.closest('[role=group]');
        const od = g('orderdate');
        if (!od) return '(missing)';
        if (od === g('number')) return 'Header';
        if (od === g('notes')) return 'Details';
        return od.querySelector(':scope > .dxbl-group > .dxbl-group-header')?.innerText.trim() ?? '(other)';
    }");

static async Task<Process> RestartApp(Process? app, string blazorProj, System.Text.StringBuilder appOutput)
{
    KillApp(ref app);
    lock (appOutput) appOutput.Clear();
    var restarted = StartApp(blazorProj, appOutput);
    await WaitForHttpOk(restarted, appOutput);
    return restarted;
}

static void ResetUserModel(string userId)
{
    Sql($"DELETE a FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{userId}'; DELETE FROM ModelDifferences WHERE UserId = '{userId}';");
}

static int Sql(string sql, params (string Name, string Value)[] parameters)
{
    using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
    foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
    return cmd.ExecuteNonQuery();
}

static string? SqlScalar(string sql)
{
    using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
    return cmd.ExecuteScalar()?.ToString();
}

// XAF Blazor shows a "Loading..." toast while a server callback runs; wait it out so screenshots are clean.
static async Task WaitForNoLoading(IPage page)
{
    // Substring, not exact: the toast's text is "Loading…" in some renders and sits next to a spinner element.
    try { await page.GetByText("Loading").First.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 }); }
    catch (TimeoutException) { /* screenshot anyway */ }
    await page.WaitForTimeoutAsync(300); // let the fade-out finish
}

// Tools tab -> Export Layout To Code; returns the printed class from the popup's memo.
static async Task<string> ExportLayoutCode(IPage page, string screenshotPath)
{
    // PredefinedCategory.Tools renders as a "Tools" tab next to Home and View in the Blazor template.
    await page.GetByText("Tools", new() { Exact = true }).First.ClickAsync();
    var exportAction = page.GetByText("Export Layout To Code", new() { Exact = true }).First;
    await exportAction.WaitForAsync(new() { Timeout = 10_000 });
    await exportAction.ClickAsync();
    const string isExport = "t => t.value.startsWith('using XafLayoutBuilder.Core;')";
    await page.WaitForFunctionAsync($"() => [...document.querySelectorAll('textarea')].some({isExport})", null, new() { Timeout = 15_000 });
    var code = await page.EvaluateAsync<string>($"() => [...document.querySelectorAll('textarea')].find({isExport}).value");
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = screenshotPath });
    return code;
}

static async Task ClosePopup(IPage page)
{
    var cancel = page.Locator(".dxbl-popup, .dxbl-modal").GetByRole(AriaRole.Button, new() { Name = "Cancel" });
    if (await cancel.CountAsync() > 0) await cancel.First.ClickAsync();
    else await page.Keyboard.PressAsync("Escape");
}

// Round-trip helpers: cut one builder expression out of C# text and compare modulo whitespace.
static string BuilderExpression(string code, string start)
{
    var from = code.IndexOf(start, StringComparison.Ordinal);
    if (from < 0) throw new Exception($"'{start}' not found in the code");
    var to = code.IndexOf(".Build()", from, StringComparison.Ordinal);
    return code[from..(to + ".Build()".Length)];
}

static string Squash(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "");
static string[] HideCalls(string s) => System.Text.RegularExpressions.Regex.Matches(s, @"\.Hide\(x => x\.\w+\)").Select(m => m.Value).ToArray();
static string WithoutHideCalls(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s*\.Hide\(x => x\.\w+\)", "");

static async Task<IPage> NewPage(IBrowser browser)
{
    var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
    page.SetDefaultTimeout(30_000);
    page.SetDefaultNavigationTimeout(30_000);
    return page;
}

static Process StartApp(string blazorProj, System.Text.StringBuilder appOutput, string extraArgs = "")
{
    // launchSettings.json's applicationUrl overrides ASPNETCORE_URLS unless --no-launch-profile
    // is passed; --urls on the command line wins over both. Belt and braces.
    var psi = new ProcessStartInfo("dotnet",
        $"run --no-build --no-launch-profile --project \"{blazorProj}\" --urls {BaseUrl} {extraArgs}")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        EnvironmentVariables = { ["ASPNETCORE_URLS"] = BaseUrl, ["ASPNETCORE_ENVIRONMENT"] = "Development" },
    };
    var p = Process.Start(psi)!;
    p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (appOutput) appOutput.AppendLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (appOutput) appOutput.AppendLine(e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    return p;
}

static void KillApp(ref Process? app)
{
    if (app is null) return;
    try { app.Kill(entireProcessTree: true); app.WaitForExit(10_000); } catch { /* already gone */ }
    app.Dispose();
    app = null;
}

static async Task<bool> IsServing()
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try { return (await http.GetAsync(BaseUrl)).IsSuccessStatusCode; }
    catch { return false; }
}

static async Task WaitForHttpOk(Process app, System.Text.StringBuilder appOutput)
{
    for (var i = 0; i < 90; i++)
    {
        if (app.HasExited) break;
        if (await IsServing()) { Console.WriteLine($"    app ready at {BaseUrl}"); return; }
        await Task.Delay(2000);
    }
    string tail;
    lock (appOutput) tail = string.Join('\n', appOutput.ToString().Split('\n').TakeLast(40));
    throw new Exception($"app did not become ready at {BaseUrl}\n--- app stdout/stderr (last 40 lines) ---\n{tail}");
}

static void RunOrThrow(string file, string args)
{
    var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })!;
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"`{file} {args}` exited with {p.ExitCode}");
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (dir.GetFiles("*.slnx").Length > 0) return dir.FullName;
    throw new Exception("repo root (*.slnx) not found above " + AppContext.BaseDirectory);
}
