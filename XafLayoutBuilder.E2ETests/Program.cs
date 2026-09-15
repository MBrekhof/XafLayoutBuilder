using System.Diagnostics;
using Microsoft.Playwright;

// E2E phase gate for XafLayoutBuilder (start document section 8). Same shape as XafReportScheduler's harness.
// Run with:  dotnet run --project XafLayoutBuilder.E2ETests
// Exit codes: 0 pass, 1 fail, 2 Playwright browser missing.
//
// Builds and starts the sample on :5100, logs in as Admin, then:
//   E2E 1   Order_DetailView renders the builder layout (hidden member absent, group and tab order, collapsible group);
//           the Lines tab's nested ListView follows OrderLine's columns spec without the Order back-reference (VIEW-001)
//   E2E 2   Order_ListView column order, OrderDate-descending sort, hidden column offered by the column chooser
//   E2E 3   Order_LookupListView (via ServiceOrder.OriginalOrder) shows only Number, Customer; SRV-001's form extends Order's
//           layout (HIER-001)
//   VIEW-001 Order_Compact_ListView and Order_Compact_DetailView, declared in code by the sample, show their own columns
//            and layout
//   BAND-001 Order_Banded_ListView's band header Order spans Number and Customer
//   GROUP-001 Order_Grouped_ListView opens grouped by Customer (a group row per customer, Customer no data header) with
//            the group panel holding Customer
//   APPEAR-001 Order.Layout.cs's appearance rules (Appearance add-on): Globex's order numbers bold in DarkRed in
//            Order_ListView, Acme's unchanged; the Header group's caption DarkBlue on ORD-001, Notes unchanged; the Order
//            export (E2E 5a) prints the rules back equal to the source
//   E2E 5a  exporting the untouched layout reproduces Order.Layout.cs; Customer's column caption round-trips
//   E2E 4   Admin drags OrderDate into Details in XAF's layout editor and hides the Customer column from the grid header
//           menu (E2E4-001); the user layer wins over the builder
//   E2E 5   Export Layout To Code prints OrderDate under Details and hides Customer, but not the never-mentioned Notes
//   E2E 7   Copy Layout To Clipboard (Blazor add-on, Tools tab) puts the printed class on the clipboard
//   E2E 9   Download Layout File hands over Order.Layout.cs with that same text
//   JSON-001 Export Layout To JSON, read back and printed as C#, gives the popup's builder expressions; Download Layout
//            JSON hands over Order.layout.json with that same document
//   E2E 6   deleting the user differences brings the builder layout back
//   DIFF-001 a user difference aimed at the stock path Main/SimpleEditors is not rendered and is gone from the stored
//            user model after the next save, while the same difference's caption on a builder group applies and is kept
//   E2E 8   Customer's .Unplaced(AppendToGroup("Other")) collects City instead of failing startup
//   MODELEDITOR-001 Edit Model (ModelEditor add-on): a caption edit closed with Cancel is dropped; a saved caption is stored
//            in Admin's user model and shows after Save's reload; MODELEDITOR-002: a saved Reset takes it away at once;
//            MODELEDITOR-003: the search finds the view and a value's description shows; MODELEDITOR-013: the tree's Views
//            node shows its icon; MODELEDITOR-004: a column added
//            in the editor shows after Save and is gone again after deleting it, and a model save from a second logon does
//            not store a node added in the open editor but not saved; MODELEDITOR-005: the DetailView drop-down sets
//            Order_ListView's form, View in Model selects the open view's node, Go to and Back navigate, a reset restores it;
//            MODELEDITOR-006: switching the filter builder from Criteria to Filter drops Criteria's draft, typed Criteria
//            open in the filter builder over Order's fields, invalid text keeps the builder
//            open and the value unchanged, and Apply writes valid criteria back, ImageName
//            offers the image names, the saved Criteria leave Order_ListView one row, a column's ToolTip is a text area, and a
//            reset of the Criteria lists every order again; MODELEDITOR-007: a column's PropertyName left empty is marked
//            required and Save is refused naming the node and the value, and closing the editor saves nothing;
//            resetting a saved custom column's required PropertyName is refused and the column survives a reload;
//            MODELEDITOR-008: a language added in the editor is offered in its language combo, a caption translated to nl-NL
//            lists in the translate view, is stored in the nl-NL aspect row, stays out of en-US and shows in nl-NL, and a
//            saved reset in nl-NL takes it out of that row
//   FREEZE-001 with --extra-column, Notes is a fourth column; after an administrator froze the column set it stays hidden
//   NEST-001 with --nested-column, Order_ListView shows Customer.City as a fourth column filled with each customer's city,
//            and the export prints it as .Column(x => x.Customer.City)
//   Session 5: the host started with --break-layout exits at startup reporting XLB001, and for Order both a throwing
//              detail factory and XLB003 (registered factories are checked at startup, each view on its own)
//   TEST-001:  adding --break-factory (a registered columns factory throwing a non-layout exception), fail-fast
//              startup stops on that exception
//   CHECK-002: both fixtures with FailFastOnLayoutErrors off serve XAF's own layout and log every failure, including
//              the factory's exception and Order's failures that the check only reaches after it
// Writes Admin's ModelDifferences rows in the LocalDB catalog XafLayoutBuilder.Sample, restarting the host around
// those writes, and leaves Admin's user model empty. Screenshots: bin/Debug/net10.0/screenshots.

const string BaseUrl = "http://localhost:5100";
// Pooling off: the harness talks to LocalDB right after killing the host, and a pooled connection
// left over from that moment comes back as a dead named pipe.
const string ConnectionString = @"Data Source=(localdb)\mssqllocaldb;Integrated Security=SSPI;Initial Catalog=XafLayoutBuilder.Sample;Encrypt=False;Pooling=false";

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
    // An earlier run that aborted between E2E 4 and E2E 6 leaves Admin's user differences in the database, and
    // E2E 5a would then export that layout instead of the builder's. Always start from an empty user model.
    try
    {
        var leftOver = SqlScalar("SELECT COUNT(*) FROM ModelDifferences d JOIN PermissionPolicyUser u ON u.ID = d.UserId WHERE u.UserName = 'Admin'");
        if (leftOver != "0") Console.WriteLine($"    clearing {leftOver} left-over user model row(s) for Admin");
        Sql("DELETE a FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID JOIN PermissionPolicyUser u ON u.ID = d.UserId WHERE u.UserName = 'Admin'; DELETE d FROM ModelDifferences d JOIN PermissionPolicyUser u ON u.ID = d.UserId WHERE u.UserName = 'Admin';");
    }
    // 4060: the catalog does not exist yet. 208: it exists but the schema does not. Either way this is the first run
    // on this machine and the sample is about to create both; anything else is a real connection or permission fault.
    catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 4060 or 208)
    {
        Console.WriteLine("    no sample database yet; the app will create it on startup");
    }
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
    await page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = BaseUrl });

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
    // VIEW-001: the Lines tab's nested ListView (Order_Lines_ListView) takes OrderLine's columns spec, without the Order
    // back-reference that spec lists: XAF keeps it hidden in a nested view.
    await form.GetByText("Widget", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 15_000 });
    var lineHeaders = await form.Locator(".dxbl-grid").First.EvaluateAsync<string[]>(
        @"g => [...g.querySelectorAll('th.dxbl-grid-header')].map(h => h.textContent.replace(/No filter applied/g,'').trim().replace(/\s+/g,' ')).filter(t => t && t !== 'Selection')");
    Assert(string.Join(",", lineHeaders) == "Unit Price,Quantity,Product",
        $"the Lines tab's nested ListView shows OrderLine's columns spec without the back-reference (got {string.Join(",", lineHeaders)})");
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
    // HIER-001: ServiceOrder's layout extends Order's. Original Order joins the Header group (captioned Order), and its own
    // Service group holds Service Date and Technician.
    var serviceGroups = await activeForm.EvaluateAsync<string[]>(@"f => ['originalorder', 'servicedate', 'technician'].map(m => {
        const label = f.querySelector('label.xaf-item-' + m);
        const group = label && label.closest('[role=group]');
        return group?.querySelector(':scope > .dxbl-group > .dxbl-group-header')?.innerText.trim() ?? '(none)';
    })");
    Assert(string.Join(",", serviceGroups) == "Order,Service,Service",
        $"SRV-001's form extends Order's layout: Original Order in Header, Service Date and Technician in Service (got {string.Join(",", serviceGroups)})");
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

    Step("VIEW-001: a ListView declared in code shows its own columns");
    await OpenListView(page, "Order_Compact_ListView", "ORD-001");
    var compactHeaders = await GridHeaders(page);
    Assert(string.Join(",", compactHeaders) == "Number,Order Date",
        $"Order_Compact_ListView, declared with LayoutRegistry.AddListView, shows Number, Order Date (got {string.Join(",", compactHeaders)})");

    Step("VIEW-001: a DetailView declared in code renders its own layout");
    var ord001Key = SqlScalar("SELECT LOWER(CAST(ID AS NVARCHAR(36))) FROM Orders WHERE Number = 'ORD-001'")
        ?? throw new Exception("ORD-001 not found in the sample database");
    // XAF Blazor opens a DetailView at "{viewId}/{objectKey}" (dxdocs, Ways to Display a View).
    await page.GotoAsync($"{BaseUrl}/Order_Compact_DetailView/{ord001Key}", new() { WaitUntil = WaitUntilState.NetworkIdle });
    await page.WaitForFunctionAsync("() => [...document.querySelectorAll('input')].some(i => i.value === 'ORD-001')", null, new() { Timeout = 30_000 });
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-09a-declared-detailview.png") });
    var compactText = await page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First.InnerTextAsync();
    Assert(compactText.Contains("Compact order") && compactText.Contains("Order Date"),
        $"Order_Compact_DetailView, declared with LayoutRegistry.AddDetailView, shows its Compact order group (got {compactText.Replace('\n', ' ')})");
    Assert(!compactText.Contains("Notes") && !compactText.Contains("Customer"), "Order_Compact_DetailView leaves out what its declared layout hides");

    Step("BAND-001: a band header spans its columns");
    await OpenListView(page, "Order_Banded_ListView", "ORD-001");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-09b-banded-listview.png") });
    var bandedHeaders = await GridHeaders(page);
    var bandSpan = await page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First.EvaluateAsync<int>(
        @"g => [...g.querySelectorAll('th')].find(h => h.textContent.replace(/No filter applied/g,'').trim() === 'Order')?.colSpan ?? 0");
    Assert(bandSpan == 2, $"the band header Order spans Number and Customer (colspan {bandSpan}; headers {string.Join(",", bandedHeaders)})");
    Assert(new[] { "Number", "Customer", "Order Date" }.All(bandedHeaders.Contains),
        $"the banded ListView still shows Number, Customer and Order Date (got {string.Join(",", bandedHeaders)})");

    Step("GROUP-001: a ListView grouped by Customer when it opens, with the group panel shown");
    await OpenListView(page, "Order_Grouped_ListView", "Order Date");
    var groupedGrid = page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First;
    // Read from the running sample: groups start collapsed, one row "Customer: Acme Corp (Count: 2)" per customer and no
    // order rows; the group panel above the header row holds Customer, which leaves the data headers.
    await groupedGrid.GetByText("Customer: Acme Corp").First.WaitForAsync(new() { Timeout = 30_000 });
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-09g-grouped-listview.png") });
    var groupedText = System.Text.RegularExpressions.Regex.Replace(await groupedGrid.InnerTextAsync(), @"\s+", " ");
    Assert(groupedText.Contains("Customer: Acme Corp (Count: 2)") && groupedText.Contains("Customer: Globex (Count: 2)"),
        $"Order_Grouped_ListView opens grouped by Customer, one group row per customer (got {groupedText})");
    var groupedHeaders = await GridHeaders(page);
    Assert(string.Join(",", groupedHeaders) == "Number,Order Date", $"the grouped Customer column is no data header (got {string.Join(",", groupedHeaders)})");
    Assert(groupedText.Split("Selection")[0].Contains("Customer"), $"the group panel above the header row holds Customer (got {groupedText})");

    Step("APPEAR-001: Order.Layout.cs's appearance rules colour Globex's order numbers and the Header caption");
    await OpenListView(page, "Order_ListView", "ORD-003");
    var orderGrid = page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First;
    // Read from the running sample: XAF puts the rule's style on the cell through a generated CSS class, so the cell's computed
    // colour and weight are what a user sees. DarkRed is rgb(139, 0, 0).
    // Pairs come back as "key|value" strings: Playwright 1.49 for .NET turns a JS object into an empty Dictionary (probed
    // 2026-09-14), while string[] works, as everywhere else in this gate.
    static Dictionary<string, string> Pairs(string[] entries) {
        var pairs = new Dictionary<string, string>();
        foreach (var entry in entries) {
            var parts = entry.Split('|', 2);
            pairs.TryAdd(parts[0], parts.Length > 1 ? parts[1] : "");
        }
        return pairs;
    }
    const string NumberStylesScript = @"g => ['ORD-001', 'ORD-003', 'SRV-001'].map(n => {
        const td = [...g.querySelectorAll('td')].find(c => c.textContent.trim() === n);
        const s = td && getComputedStyle(td);
        return n + '|' + (s ? s.color + ' ' + s.fontWeight : 'missing');
    })";
    try { await page.WaitForFunctionAsync("() => [...document.querySelectorAll('[role=tabpanel].dxbl-active td')].some(c => c.textContent.trim() === 'ORD-003' && getComputedStyle(c).color === 'rgb(139, 0, 0)')", null, new() { Timeout = 10_000 }); }
    catch (TimeoutException) { /* the assertion below reports the styles */ }
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-09h-appearance-listview.png") });
    var numberStyles = Pairs(await orderGrid.EvaluateAsync<string[]>(NumberStylesScript));
    var numberStylesText = string.Join(", ", numberStyles.Select(p => $"{p.Key}: {p.Value}"));
    Assert(numberStyles["ORD-003"] == "rgb(139, 0, 0) 700" && numberStyles["SRV-001"] == "rgb(139, 0, 0) 700",
        $"Globex's orders show their Number bold in DarkRed (got {numberStylesText})");
    Assert(numberStyles["ORD-001"] != "rgb(139, 0, 0) 700", $"Acme's ORD-001 keeps the grid's own style (got {numberStylesText})");
    // The layout rule colours the caption text of the Header group ("Order"); DarkBlue is rgb(0, 0, 139). Notes stays as it was.
    await OpenOrd001Detail(page);
    var captionColors = Pairs(await page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First.EvaluateAsync<string[]>(
        @"f => [...f.querySelectorAll('[role=group].dxbl-fl-group')]
            .map(g => g.querySelector(':scope > .dxbl-group > .dxbl-group-header'))
            .filter(Boolean)
            .map(h => {
                const text = [...h.querySelectorAll('*')].find(e => [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) ?? h;
                return h.innerText.trim() + '|' + getComputedStyle(text).color;
            })"));
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-09i-appearance-detailview.png") });
    var captionColorsText = string.Join(", ", captionColors.Select(p => $"{p.Key}: {p.Value}"));
    Assert(captionColors.GetValueOrDefault("Order") == "rgb(0, 0, 139)", $"the Header group's caption Order is DarkBlue (got {captionColorsText})");
    Assert(captionColors.GetValueOrDefault("Notes") is { } notesColor && notesColor != "rgb(0, 0, 139)",
        $"the Notes group's caption keeps its own colour (got {captionColorsText})");

    Step("E2E 5a: exporting the untouched builder layout reproduces the source (section 6 round trip)");
    await OpenOrd001Detail(page);
    var roundTrip = await ExportLayoutCode(page, Path.Combine(screenshotDir, "e2e-09b-export-roundtrip.png"));
    await File.WriteAllTextAsync(Path.Combine(screenshotDir, "e2e-09b-exported-roundtrip.cs"), roundTrip);
    await ClosePopup(page);
    var layoutSource = await File.ReadAllTextAsync(Path.Combine(repoRoot, "XafLayoutBuilder.Sample.Module", "BusinessObjects", "Order.Layout.cs"));
    Assert(roundTrip.Contains("namespace XafLayoutBuilder.Sample.Module.BusinessObjects;"), "export declares the business object's namespace");
    Assert(roundTrip.Contains("public partial class Order : ISupportViewLayoutCustomization, ISupportAppearanceRules {"), "export declares the partial class, with its appearance rules (APPEAR-001)");
    Assert(NormalizeCode(BuilderExpression(roundTrip, "AppearanceBuilder<Order>.Create()")) == NormalizeCode(BuilderExpression(layoutSource, "AppearanceBuilder<Order>.Create()")),
        "exported appearance rules equal the ones in Order.Layout.cs (APPEAR-001)");
    Assert(roundTrip.Contains("Views: Order_DetailView, Order_ListView, Order_LookupListView."), "export names the views it read");
    Assert(NormalizeCode(BuilderExpression(roundTrip, "LayoutBuilder<Order>.Create()")) == NormalizeCode(BuilderExpression(layoutSource, "LayoutBuilder<Order>.Create()")),
        "exported DetailView builder equals the one in Order.Layout.cs (modulo indentation)");
    Assert(NormalizeCode(BuilderExpression(roundTrip, "ListViewColumnsBuilder<Order>.Create()")) == NormalizeCode(BuilderExpression(layoutSource, "ListViewColumnsBuilder<Order>.Create()")),
        "exported columns and lookup equal the ones in Order.Layout.cs, Hide calls included (EXPORT-001)");
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
    await File.WriteAllTextAsync(Path.Combine(screenshotDir, "e2e-09c-exported-Customer.Layout.cs"), customerExport);
    var customerSource = await File.ReadAllTextAsync(Path.Combine(repoRoot, "XafLayoutBuilder.Sample.Module", "BusinessObjects", "Customer.Layout.cs"));
    Assert(customerExport.Contains(".Unplaced(UnplacedMembers.AppendToGroup(\"Other\"))"),
        "the export keeps the opt-in instead of freezing the catch-all group into explicit items");
    Assert(!customerExport.Contains(".Hide(x => x.City)"), "a member the catch-all collected is not exported as hidden");
    Assert(NormalizeCode(BuilderExpression(customerExport, "LayoutBuilder<Customer>.Create()")) == NormalizeCode(BuilderExpression(customerSource, "LayoutBuilder<Customer>.Create()")),
        "exported Customer DetailView builder equals the one in Customer.Layout.cs");

    Step("E2E 4: the user layer wins: Admin drags OrderDate into Details in XAF's layout editor");
    // E2E4-001: the way a user does it. Right-click an empty area of the form, Customize Layout, drag Order Date below
    // Notes, close the Customization window. DevExpress drags with pointer events (no draggable attribute), so the mouse
    // moves in steps. The editor saves into Admin's user model differences within the session, so no restart is needed.
    // E2E 6 deletes the difference again.
    var adminId = SqlScalar("SELECT LOWER(CAST(ID AS NVARCHAR(36))) FROM PermissionPolicyUser WHERE UserName = 'Admin'")
        ?? throw new Exception("Admin user not found in the sample database");
    await OpenOrd001Detail(page);
    await WaitForNoLoading(page);
    var editedForm = page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First;
    var formBox = await editedForm.BoundingBoxAsync() ?? throw new Exception("the Order form has no bounding box");
    // The strip at the bottom of the form, under the Lines grid, is empty form area.
    await page.Mouse.ClickAsync(formBox.X + formBox.Width / 2, formBox.Y + formBox.Height - 8, new() { Button = MouseButton.Right });
    await page.GetByText("Customize Layout", new() { Exact = true }).First.ClickAsync();
    var layoutEditor = page.Locator(".xaf-layouteditor-menu").First;
    await layoutEditor.GetByText("Layout Tree View", new() { Exact = true }).WaitForAsync(new() { Timeout = 15_000 });
    var orderDateItem = await LayoutItemBox(editedForm, "orderdate");
    var notesItem = await LayoutItemBox(editedForm, "notes");
    var (fromX, fromY) = (orderDateItem.X + orderDateItem.Width / 2, orderDateItem.Y + orderDateItem.Height / 2);
    var (toX, toY) = (notesItem.X + notesItem.Width / 2, notesItem.Y + notesItem.Height + 4); // just below Notes, inside Details
    await page.Mouse.MoveAsync(fromX, fromY);
    await page.Mouse.DownAsync();
    for (var step = 1; step <= 20; step++) {
        await page.Mouse.MoveAsync(fromX + (toX - fromX) * step / 20, fromY + (toY - fromY) * step / 20);
        await page.WaitForTimeoutAsync(40);
    }
    await page.Mouse.UpAsync();
    await page.WaitForFunctionAsync("() => /Details\\s+Notes\\s+Order Date/.test(document.querySelector('.xaf-layouteditor-menu')?.innerText ?? '')",
        null, new() { Timeout = 10_000 });
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-10a-layout-editor-after-drag.png") });
    await layoutEditor.Locator("button").First.ClickAsync(); // the Customization window's close button
    await layoutEditor.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

    // The same user hides the Customer column from the grid header's context menu; E2E 5 checks the export (EXPORT-001).
    await OpenListView(page, "Order_ListView", "ORD-001");
    await page.Locator("[role=tabpanel].dxbl-active th", new() { HasText = "Customer" }).First.ClickAsync(new() { Button = MouseButton.Right });
    await page.GetByText("Hide This Column", new() { Exact = true }).First.ClickAsync();
    var headersAfterHide = await GridHeaders(page);
    for (var attempt = 0; attempt < 10 && headersAfterHide.Contains("Customer"); attempt++) {
        await page.WaitForTimeoutAsync(300);
        headersAfterHide = await GridHeaders(page);
    }
    Assert(string.Join(",", headersAfterHide) == "Number,Order Date", $"Hide This Column hid Customer (got {string.Join(",", headersAfterHide)})");

    await OpenOrd001Detail(page);
    Console.WriteLine("    user diff rows for Admin: " + SqlScalar($"SELECT COUNT(*) FROM ModelDifferences WHERE UserId = '{adminId}'")
        + ", aspect mentions OrderDate: " + SqlScalar($"SELECT MAX(CASE WHEN CAST(a.Xml AS NVARCHAR(MAX)) LIKE '%OrderDate%' THEN 1 ELSE 0 END) FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}'"));
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-10-user-layer-orderdate-in-details.png") });
    Assert(await OrderDateGroup(page) == "Details", "after the drag OrderDate renders inside the Details group (user layer over builder)");

    Step("E2E 5: Export Layout To Code shows the merged layout as builder C#");
    var exported = await ExportLayoutCode(page, Path.Combine(screenshotDir, "e2e-11-export-popup.png"));
    await File.WriteAllTextAsync(Path.Combine(screenshotDir, "e2e-11-exported-Order.Layout.cs"), exported);
    Console.WriteLine("    exported:\n" + string.Join("\n", exported.Split('\n').Select(l => "      " + l.TrimEnd())));
    Assert(exported.Contains("public partial class Order : ISupportViewLayoutCustomization"), "export is the Order partial class");
    var detailsBlock = exported[exported.IndexOf(".Group(\"Details\"", StringComparison.Ordinal)..exported.IndexOf(".Tabs(\"Tabs\"", StringComparison.Ordinal)];
    // The layout editor also stores a relative size on every item of the groups it touched, so match the call's start.
    Assert(detailsBlock.Contains(".Item(x => x.OrderDate"), "exported code places OrderDate inside the Details group");
    var headerBlock = exported[exported.IndexOf(".Group(\"Header\"", StringComparison.Ordinal)..exported.IndexOf(".Group(\"Details\"", StringComparison.Ordinal)];
    Assert(!headerBlock.Contains("OrderDate"), "exported Header group no longer contains OrderDate");
    Assert(exported.Contains(".TabFor(x => x.Lines, imageName: \"BO_Order_Item\")") && exported.Contains(".Hide(x => x.SyncToken)"),
        "export keeps the builder's tabs and hidden member");
    Assert(exported.Contains(".Caption(\"Order\")") && !exported.Contains(".Caption(\"Details\")") && !exported.Contains(".Caption(\"Lines\")"),
        "export prints the explicit Header caption and not XAF's computed captions");
    Assert(!exported.Contains(".Hide(x => x.ID)"), "export does not list the key as a hidden column");
    Assert(exported.Contains(".Hide(x => x.Customer)"), "a column the user layer hid after the builder showed it is exported as hidden (EXPORT-001)");
    Assert(!exported.Contains(".Hide(x => x.Notes)"), "a column the spec never mentioned is not exported as hidden (EXPORT-001)");
    Assert(exported.Contains(".Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)") && exported.Contains(".Lookup(l => l"),
        "export includes the ListView columns and the lookup");

    Step("E2E 7: Copy Layout To Clipboard puts the code on the clipboard");
    await ClosePopup(page);
    await page.EvaluateAsync("() => navigator.clipboard.writeText('')");
    await page.GetByText("Tools", new() { Exact = true }).First.ClickAsync();
    var copyAction = page.GetByText("Copy Layout To Clipboard", new() { Exact = true }).First;
    await copyAction.WaitForAsync(new() { Timeout = 10_000 });
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-14-copy-action.png") });
    await copyAction.ClickAsync();
    await WaitForNoLoading(page);
    var clipboard = "";
    for (var attempt = 0; attempt < 10 && clipboard.Length == 0; attempt++)
    {
        clipboard = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        if (clipboard.Length == 0) await page.WaitForTimeoutAsync(500);
    }
    Console.WriteLine($"    clipboard: {clipboard.Length} chars, first line: {clipboard.Split('\n').FirstOrDefault()?.Trim()}");
    Assert(clipboard.Contains("public partial class Order : ISupportViewLayoutCustomization"), "the clipboard holds the printed class");
    // Everything except the leading comments, which carry a timestamp to the minute: the popup and the copy can
    // straddle a minute boundary, and that difference says nothing about the printed layout.
    static string WithoutComments(string code) =>
        string.Join("\n", code.Replace("\r", "").Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
    await File.WriteAllTextAsync(Path.Combine(screenshotDir, "e2e-14-clipboard.txt"), clipboard);
    if (WithoutComments(clipboard) != WithoutComments(exported))
    {
        var fromClipboard = WithoutComments(clipboard).Split('\n');
        var fromPopup = WithoutComments(exported).Split('\n');
        for (var i = 0; i < Math.Max(fromClipboard.Length, fromPopup.Length); i++)
            if (i >= fromClipboard.Length || i >= fromPopup.Length || fromClipboard[i] != fromPopup[i])
            {
                Console.WriteLine($"    first difference at line {i + 1}:");
                Console.WriteLine($"      popup    : {(i < fromPopup.Length ? fromPopup[i] : "(end)")}");
                Console.WriteLine($"      clipboard: {(i < fromClipboard.Length ? fromClipboard[i] : "(end)")}");
                break;
            }
    }
    Assert(WithoutComments(clipboard) == WithoutComments(exported), "the clipboard holds the same class the popup showed");

    Step("E2E 9: Download Layout File hands over the .Layout.cs file");
    // Running an action drops the toolbar back to the Home tab, so select Tools again.
    await page.GetByText("Tools", new() { Exact = true }).First.ClickAsync();
    var downloadAction = page.GetByText("Download Layout File", new() { Exact = true }).First;
    await downloadAction.WaitForAsync(new() { Timeout = 10_000 });
    var download = await page.RunAndWaitForDownloadAsync(async () => await downloadAction.ClickAsync(), new() { Timeout = 30_000 });
    var downloadedPath = Path.Combine(screenshotDir, "e2e-15-downloaded-Order.Layout.cs");
    await download.SaveAsAsync(downloadedPath);
    var downloaded = await File.ReadAllTextAsync(downloadedPath);
    Console.WriteLine($"    downloaded {download.SuggestedFilename}, {downloaded.Length} chars");
    Assert(download.SuggestedFilename == "Order.Layout.cs", $"the file is named after the type (got {download.SuggestedFilename})");
    Assert(WithoutComments(downloaded) == WithoutComments(exported), "the downloaded file holds the same class the popup showed");

    Step("JSON-001: Export Layout To JSON and Download Layout JSON describe the layout the C# export printed");
    // Both forms come from one export. Read back through LayoutSpecJson and printed as C#, the JSON document has to give
    // the builder expressions the C# popup showed in E2E 5 (the same merged layout, user layer included).
    await page.GetByText("Tools", new() { Exact = true }).First.ClickAsync();
    var exportJsonAction = page.GetByText("Export Layout To JSON", new() { Exact = true }).First;
    await exportJsonAction.WaitForAsync(new() { Timeout = 10_000 });
    await exportJsonAction.ClickAsync();
    const string isJsonExport = "t => t.value.trimStart().startsWith('{')";
    await page.WaitForFunctionAsync($"() => [...document.querySelectorAll('textarea')].some({isJsonExport})", null, new() { Timeout = 15_000 });
    var exportedJson = (await page.EvaluateAsync<string>($"() => [...document.querySelectorAll('textarea')].find({isJsonExport}).value")).Replace("\r", "");
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-19-export-json-popup.png") });
    await ClosePopup(page);
    var jsonSpecs = XafLayoutBuilder.Core.LayoutSpecJson.Deserialize<XafLayoutBuilder.Core.LayoutSpecs>(exportedJson);
    Assert(jsonSpecs.Detail?.TypeName == "XafLayoutBuilder.Sample.Module.BusinessObjects.Order" && jsonSpecs.Columns?.TypeName == jsonSpecs.Detail?.TypeName,
        "the JSON document holds Order's detail layout and its columns");
    var jsonAsCode = XafLayoutBuilder.Core.CSharpLayoutPrinter.PrintClass("XafLayoutBuilder.Sample.Module.BusinessObjects", "Order", jsonSpecs.Detail, jsonSpecs.Columns);
    foreach (var expressionStart in new[] { "LayoutBuilder<Order>.Create()", "ListViewColumnsBuilder<Order>.Create()" })
        Assert(NormalizeCode(BuilderExpression(jsonAsCode, expressionStart)) == NormalizeCode(BuilderExpression(exported, expressionStart)),
            $"the JSON, printed as C#, gives the C# popup's {expressionStart} expression");

    await page.GetByText("Tools", new() { Exact = true }).First.ClickAsync();
    var downloadJsonAction = page.GetByText("Download Layout JSON", new() { Exact = true }).First;
    await downloadJsonAction.WaitForAsync(new() { Timeout = 10_000 });
    var jsonDownload = await page.RunAndWaitForDownloadAsync(async () => await downloadJsonAction.ClickAsync(), new() { Timeout = 30_000 });
    var jsonDownloadedPath = Path.Combine(screenshotDir, "e2e-19-downloaded-Order.layout.json");
    await jsonDownload.SaveAsAsync(jsonDownloadedPath);
    var downloadedJson = (await File.ReadAllTextAsync(jsonDownloadedPath)).Replace("\r", "");
    Console.WriteLine($"    downloaded {jsonDownload.SuggestedFilename}, {downloadedJson.Length} chars");
    Assert(jsonDownload.SuggestedFilename == "Order.layout.json", $"the JSON file is named after the type (got {jsonDownload.SuggestedFilename})");
    Assert(downloadedJson == exportedJson, "the downloaded JSON is the document the popup showed");

    Step("E2E 6: resetting the user model brings the builder layout back");
    KillApp(ref app);
    ResetUserModel(adminId);
    app = await RestartApp(app, blazorProj, appOutput);
    await OpenOrd001Detail(page);
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-12-user-layer-reset.png") });
    Assert(await OrderDateGroup(page) == "Header", "after reset OrderDate renders inside the Header group again");

    Step("DIFF-001: a user difference aimed at a stock layout path the builder replaced is ignored, then dropped at the next save");
    // A difference stored before a view was converted still names XAF's stock paths (Main/SimpleEditors/Order/...). The
    // builder replaced that tree, so the node has no lower layer and is not marked IsNewNode: ModelNode.CreateMasterNode
    // sets it aside as unusable instead of merging it, and ModelDifferenceDbStore.SaveDifference writes only the usable
    // layer, so the next save of that user's model drops it. The Details caption is the control: same difference, a path
    // the builder generates. Log Off flushes XAF Blazor's deferred save, so the stored XML is read after it.
    const string OrphanedStockPathXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Application>
          <Views>
            <DetailView Id="Order_DetailView">
              <Layout>
                <LayoutGroup Id="Main">
                  <LayoutGroup Id="Details" Caption="Live group caption" />
                  <LayoutGroup Id="SimpleEditors" ShowCaption="True" Caption="Stock group caption">
                    <LayoutGroup Id="Order">
                      <LayoutItem Id="Number" RelativeSize="30" />
                    </LayoutGroup>
                  </LayoutGroup>
                </LayoutGroup>
              </Layout>
            </DetailView>
          </Views>
        </Application>
        """;
    KillApp(ref app);
    ResetUserModel(adminId);
    Sql($"""
        DECLARE @d UNIQUEIDENTIFIER = NEWID();
        INSERT INTO ModelDifferences (ID, UserId, ContextId, Version, GCRecord) VALUES (@d, '{adminId}', 'Blazor', 0, 0);
        INSERT INTO ModelDifferenceAspects (ID, Name, Xml, OwnerID, GCRecord) VALUES (NEWID(), '', @xml, @d, 0);
        """, ("@xml", OrphanedStockPathXml));
    app = await RestartApp(app, blazorProj, appOutput);
    await OpenOrd001Detail(page);
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-18-orphaned-stock-path.png") });
    var orphanForm = page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First;
    var orphanFormText = await orphanForm.InnerTextAsync();
    Assert(orphanFormText.Contains("Live group caption"), "control: the same difference's caption on the builder's Details group is applied");
    Assert(!orphanFormText.Contains("Stock group caption"), "the group aimed at the stock path Main/SimpleEditors is not rendered");
    Assert(await orphanForm.Locator("label.xaf-item-number").CountAsync() == 1, "Number is rendered once");
    Assert(await OrderDateGroup(page) == "Header", "the builder's Header group is intact");
    await page.GetByRole(AriaRole.Button, new() { Name = "Admin", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Log Off", Exact = true }).ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase), new() { Timeout = 20_000 });
    var storedDiff = SqlScalar($"SELECT CAST(a.Xml AS NVARCHAR(MAX)) FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}' AND a.Name = ''") ?? "";
    Assert(storedDiff.Contains("Live group caption"), "control: the saved user model keeps the Details caption");
    Assert(!storedDiff.Contains("SimpleEditors"), "the saved user model no longer holds the orphaned stock path");
    await Login(page);

    Step("E2E 8: unplaced members land in the catch-all group instead of failing startup");
    await page.GotoAsync($"{BaseUrl}/Customer_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByText("Acme Corp", new() { Exact = true }).First.ClickAsync();
    await page.WaitForFunctionAsync("() => [...document.querySelectorAll('input')].some(i => i.value === 'Acme Corp')", null, new() { Timeout = 30_000 });
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-13-unplaced-group.png") });
    var customerForm = page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First;
    var customerGroups = await customerForm.EvaluateAsync<string[]>(
        @"f => [...f.querySelectorAll('[role=group].dxbl-fl-group')].map(g => g.querySelector(':scope > .dxbl-group > .dxbl-group-header')?.innerText.trim() ?? '(no header)')");
    Console.WriteLine("    Customer groups: " + string.Join(" | ", customerGroups));
    Assert(customerGroups.Contains("Other"), $"the catch-all group is rendered (got {string.Join(",", customerGroups)})");
    var cityInOther = await customerForm.EvaluateAsync<bool>(@"f => {
        const group = f.querySelector('label.xaf-item-city')?.closest('[role=group]');
        return group?.querySelector(':scope > .dxbl-group > .dxbl-group-header')?.innerText.trim() === 'Other';
    }");
    Assert(cityInOther, "City, which the layout never mentions, sits in the Other group");

    Step("MODELEDITOR-001: Edit Model changes a view caption in the running model and saves it to the user model");
    // The add-on's tree and value grid over Application.Model. The edit lands in Admin's own differences and Save calls
    // XafApplication.SaveModelChanges, so a fresh page load, a new circuit reading the user model from the database, shows
    // it. FREEZE-001, next, resets the user model.
    const string EditedCaption = "Orders edited at runtime";
    await OpenListView(page, "Order_ListView", "ORD-001");
    // Codex review: an edit closed without Save never reaches the model, so it cannot ride along with a later model save.
    var modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    // MODELEDITOR-003: the search finds the view by its id, and a value's description shows when its name is clicked.
    var search = modelEditor.Locator(".xlb-model-search");
    await search.FillAsync("Order_ListView");
    await search.PressAsync("Enter");
    await modelEditor.Locator("[data-result='Views/Order_ListView']").WaitForAsync(new() { Timeout = 15_000 });
    await search.FillAsync("");
    await search.PressAsync("Enter");
    await modelEditor.Locator("[data-node='Views/Order_ListView']").WaitForAsync(new() { Timeout = 10_000 });
    // MODELEDITOR-013: tree nodes carry the Visual Studio Model Editor's icons, served by XAF Blazor's image service.
    var viewsIconLoads = await modelEditor.Locator("[data-node='Views'] img.xlb-node-icon")
        .EvaluateAsync<bool>("img => img.decode().then(() => true, () => false)");
    Assert(viewsIconLoads, "the Views node in the Model Editor tree shows its icon");
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-value-name").ClickAsync();
    // The click is a server round trip; wait for the panel to re-render before reading it.
    try { await modelEditor.Locator(".xlb-description", new() { HasText = "Property type" }).WaitForAsync(new() { Timeout = 10_000 }); }
    catch (TimeoutException) { /* the assertion below reports what the panel shows */ }
    var description = await modelEditor.Locator(".xlb-description").InnerTextAsync();
    Assert(description.Contains("Property type"), $"the Caption value's description shows (got '{description.Trim()}')");
    var captionInput = modelEditor.Locator("tr[data-value='Caption'] input");
    var originalCaption = await captionInput.InputValueAsync();
    await captionInput.FillAsync("Cancelled edit");
    await captionInput.PressAsync("Tab"); // the input posts its change on blur
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await ClosePopup(page);
    await modelEditor.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    captionInput = modelEditor.Locator("tr[data-value='Caption'] input");
    var reopenedCaption = await captionInput.InputValueAsync();
    Assert(reopenedCaption == originalCaption, $"a caption edit closed with Cancel is taken back (expected '{originalCaption}', got '{reopenedCaption}')");
    await captionInput.FillAsync(EditedCaption);
    await captionInput.PressAsync("Tab");
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-21-model-editor.png") });
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-22-model-editor-caption.png") });
    // A caption is localizable: XAF stores it in the aspect row of the current culture (Name 'en-US'), not in the default
    // aspect (Name ''), so every aspect of Admin's user model is read.
    var editedStored = SqlScalar($"SELECT STRING_AGG(CAST(a.Xml AS NVARCHAR(MAX)), '') FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}'") ?? "";
    Assert(editedStored.Contains(EditedCaption), "the saved user model holds the caption set in the Model Editor");
    Assert((await page.InnerTextAsync("body")).Contains(EditedCaption), "Save reloads the application and Order_ListView shows the edited caption");

    // MODELEDITOR-002: in the warmed-up model a cleared value keeps its cached value, so without the reload Save does a
    // saved Reset would leave the edited caption on screen.
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-reset").ClickAsync();
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-23-model-editor-reset.png") });
    Assert(!(await page.InnerTextAsync("body")).Contains(EditedCaption), "a saved Reset takes the edited caption away at once");
    // The database store keeps an aspect whose differences became empty (the en-US row held only this caption); the editor
    // blanks that row, or the caption would come back on every load.
    var resetStored = SqlScalar($"SELECT STRING_AGG(CAST(a.Xml AS NVARCHAR(MAX)), '') FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}'") ?? "";
    Assert(!resetStored.Contains(EditedCaption), "after the saved Reset no stored aspect of Admin's user model holds the caption");

    // MODELEDITOR-008: localizable values per language. The language combo picks the aspect; a caption translated to nl-NL is
    // stored in the nl-NL aspect row, stays out of the en-US application, and shows once the browser's culture is nl-NL (the
    // request-culture cookie XAF's own language switcher writes). "Add" makes a language the host does not list.
    Step("MODELEDITOR-008: a caption translated to nl-NL in the Model Editor shows in nl-NL and not in en-US");
    const string DutchCaption = "Orders in het Nederlands";
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    await modelEditor.Locator(".xlb-new-language").FillAsync("de-DE");
    await modelEditor.Locator(".xlb-add-language").ClickAsync();
    await modelEditor.Locator(".xlb-model-editor-message", new() { HasText = "Language added" }).WaitForAsync(new() { Timeout = 10_000 });
    await OpenComboList(modelEditor.Locator(".xlb-language"));
    // The list is open before its items render; wait for the added one, then assert on what is there.
    var addedOption = page.GetByRole(AriaRole.Option, new() { Name = "de-DE", Exact = true }).First;
    try { await addedOption.WaitForAsync(new() { Timeout = 10_000 }); }
    catch (TimeoutException) { /* the assertion below reports what the list offers */ }
    Assert(await addedOption.IsVisibleAsync(), "the language added in the Model Editor is offered in its language combo");
    await page.GetByRole(AriaRole.Option, new() { Name = "nl-NL", Exact = true }).First.ClickAsync();
    // The rows are rendered again for the language; typing into the old input would be lost with it.
    await modelEditor.Locator("table.xlb-values[data-aspect='nl-NL']").WaitForAsync(new() { Timeout = 10_000 });
    captionInput = modelEditor.Locator("tr[data-value='Caption'] input");
    await captionInput.FillAsync(DutchCaption);
    await captionInput.PressAsync("Tab");
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await modelEditor.Locator(".xlb-translate").ClickAsync();
    var translateRow = modelEditor.Locator("[data-translate='Views/Order_ListView|Caption']");
    await translateRow.WaitForAsync(new() { Timeout = 10_000 });
    Assert(await translateRow.Locator("input").InputValueAsync() == DutchCaption, "the translate view lists Order_ListView's Caption with the pending Dutch text");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-28-model-editor-translate.png") });
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    var dutchStored = SqlScalar($"SELECT CAST(a.Xml AS NVARCHAR(MAX)) FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}' AND a.Name = 'nl-NL'") ?? "";
    Assert(dutchStored.Contains(DutchCaption), "the Dutch caption is stored in the nl-NL aspect row of Admin's user model");
    Assert(!(await page.InnerTextAsync("body")).Contains(DutchCaption), "in en-US Order_ListView keeps its English caption");
    await SetCulture(page, "nl-NL");
    await OpenListView(page, "Order_ListView", "ORD-001");
    Assert((await page.InnerTextAsync("body")).Contains(DutchCaption), "in nl-NL Order_ListView shows the Dutch caption");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-29-model-editor-dutch.png") });
    await SetCulture(page, "en-US");
    await OpenListView(page, "Order_ListView", "ORD-001");
    // A reset in nl-NL empties that aspect; its stored row is blanked like the en-US one above, so the caption stays gone.
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    await PickComboItem(page, modelEditor.Locator(".xlb-language"), "nl-NL");
    await modelEditor.Locator("table.xlb-values[data-aspect='nl-NL']").WaitForAsync(new() { Timeout = 10_000 });
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-reset").ClickAsync();
    await modelEditor.Locator("tr[data-value='Caption'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    var dutchReset = SqlScalar($"SELECT CAST(a.Xml AS NVARCHAR(MAX)) FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}' AND a.Name = 'nl-NL'") ?? "";
    Assert(!dutchReset.Contains(DutchCaption), "after the saved reset the nl-NL aspect row no longer holds the Dutch caption");

    // MODELEDITOR-004: a column added in the editor, with its required PropertyName and an Index, shows after Save; deleting it
    // in the editor takes it away again.
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView/Columns");
    await PickComboItem(page, modelEditor.Locator(".xlb-new-type"), "Column");
    // XAF generates a hidden column for every property, Notes included, so the new column gets an id of its own.
    await modelEditor.Locator(".xlb-new-id").FillAsync("EditorNotes");
    await modelEditor.Locator(".xlb-new-id").PressAsync("Tab");
    await modelEditor.Locator(".xlb-add").ClickAsync();
    await modelEditor.Locator("[data-selected='Views/Order_ListView/Columns/EditorNotes']").WaitForAsync(new() { Timeout = 10_000 });
    foreach (var (name, value) in new[] { ("PropertyName", "Notes"), ("Index", "3") })
    {
        var valueInput = modelEditor.Locator($"tr[data-value='{name}'] input");
        await valueInput.FillAsync(value);
        await valueInput.PressAsync("Tab");
        // The values of an added node are written at once, so the row offers Reset rather than showing "unsaved".
        await modelEditor.Locator($"tr[data-value='{name}'] .xlb-reset").WaitForAsync(new() { Timeout = 10_000 });
    }
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-24-model-editor-added-column.png") });
    var addedHeaders = await GridHeaders(page);
    Console.WriteLine("    headers after adding a column in the Model Editor: " + string.Join(" | ", addedHeaders));
    Assert(addedHeaders.Contains("Notes"), $"the column added in the Model Editor shows after Save (got {string.Join(",", addedHeaders)})");
    Step("MODELEDITOR-007: a required reset on a saved custom column is refused before writing differences");
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView/Columns/EditorNotes");
    await modelEditor.Locator("tr[data-value='PropertyName'] .xlb-reset").ClickAsync();
    await modelEditor.Locator("tr[data-value='PropertyName'] .xlb-required-missing").WaitForAsync(new() { Timeout = 10_000 });
    await modelEditor.Locator(".xlb-save").ClickAsync();
    var resetRequiredMessage = modelEditor.Locator(".xlb-model-editor-message", new() { HasText = "PropertyName required" });
    await resetRequiredMessage.WaitForAsync(new() { Timeout = 10_000 });
    Assert((await resetRequiredMessage.InnerTextAsync()).Contains("Views/Order_ListView/Columns/EditorNotes"),
        "the refused required reset names the saved custom column");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-27-model-editor-required-reset.png") });
    await ClosePopup(page);
    await modelEditor.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    await OpenListView(page, "Order_ListView", "ORD-001"); // full navigation: rebuild the model from its stored differences
    var headersAfterRequiredReset = await GridHeaders(page);
    Assert(headersAfterRequiredReset.Contains("Notes"), "the saved custom column survives a refused required reset and reload");
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView/Columns/EditorNotes");
    Assert(await modelEditor.Locator("tr[data-value='PropertyName'] input").InputValueAsync() == "Notes",
        "the saved custom column still has its required PropertyName after reload");
    await modelEditor.Locator(".xlb-delete").ClickAsync();
    await modelEditor.Locator(".xlb-delete", new() { HasText = "Keep" }).WaitForAsync(new() { Timeout = 10_000 });
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    var deletedHeaders = await GridHeaders(page);
    Assert(!deletedHeaders.Contains("Notes"), $"the column deleted in the Model Editor is gone after Save (got {string.Join(",", deletedHeaders)})");
    // A model save the editor did not start must not store a node added in the open editor: here the deferred save XAF flushes
    // when the same user logs on in a second tab (BlazorApplication.LoadUserDifferences).
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView/Columns");
    await PickComboItem(page, modelEditor.Locator(".xlb-new-type"), "Column");
    await modelEditor.Locator(".xlb-new-id").FillAsync("EditorUnsaved");
    await modelEditor.Locator(".xlb-new-id").PressAsync("Tab");
    await modelEditor.Locator(".xlb-add").ClickAsync();
    await modelEditor.Locator("[data-selected='Views/Order_ListView/Columns/EditorUnsaved']").WaitForAsync(new() { Timeout = 10_000 });
    var secondTab = await NewPage(browser);
    await Login(secondTab);
    await secondTab.CloseAsync();
    var foreignSaved = SqlScalar($"SELECT STRING_AGG(CAST(a.Xml AS NVARCHAR(MAX)), '') FROM ModelDifferenceAspects a JOIN ModelDifferences d ON d.ID = a.OwnerID WHERE d.UserId = '{adminId}'") ?? "";
    Assert(!foreignSaved.Contains("EditorUnsaved"), "a model save from a second logon does not store a node added in the open Model Editor but not saved");
    await ClosePopup(page);
    await modelEditor.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

    Step("MODELEDITOR-005: a lookup sets Order_ListView's DetailView; View in Model, Go to and Back navigate the editor");
    // The DetailView drop-down lists the Order detail views ([DataSourceProperty] + [DataSourceCriteria]); after Save the list
    // opens the compact form the builder declared (SampleViews, group "Compact order").
    await OpenListView(page, "Order_ListView", "ORD-001");
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    await PickComboItem(page, modelEditor.Locator("tr[data-value='DetailView']"), "Views/Order_Compact_DetailView");
    await modelEditor.Locator("tr[data-value='DetailView'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    // A lookup edit is the only pending edit until Save, so another edit is refused; the refused text must not stay in the
    // input, or it would look saved (Codex review 5).
    var refusedCaptionInput = modelEditor.Locator("tr[data-value='Caption'] input");
    var captionBefore = await refusedCaptionInput.InputValueAsync();
    await refusedCaptionInput.FillAsync("Refused caption");
    await refusedCaptionInput.PressAsync("Tab");
    await modelEditor.Locator(".xlb-model-editor-message", new() { HasText = "first" }).WaitForAsync(new() { Timeout = 10_000 });
    var captionAfter = await modelEditor.Locator("tr[data-value='Caption'] input").InputValueAsync();
    Assert(captionAfter == captionBefore,
        $"an edit refused while a lookup edit is pending leaves the input showing the model's value (got '{captionAfter}', expected '{captionBefore}')");
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    // An order already open in a tab keeps the form it was opened with, and its tab header carries the number too, so the
    // check opens an order no earlier step opened.
    var lookupForm = await OpenOrderFromOrderList(page, "ORD-002");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-25-model-editor-lookup.png") });
    Assert(lookupForm.Contains("Compact order"),
        $"the DetailView chosen through the Model Editor's lookup opens from Order_ListView after Save (got {lookupForm.Replace('\n', ' ')})");
    // View in Model in the open form selects its view's node.
    await page.GetByText("Tools", new() { Exact = true }).First.ClickAsync();
    await page.GetByText("View in Model", new() { Exact = true }).First.ClickAsync();
    modelEditor = page.Locator(".xlb-model-editor");
    await modelEditor.Locator("[data-selected='Views/Order_Compact_DetailView']").WaitForAsync(new() { Timeout = 15_000 });
    await ClosePopup(page);
    await modelEditor.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    // Go to follows the DetailView reference, Back returns; then the DetailView is reset and saved for the steps after this one.
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    await modelEditor.Locator("tr[data-value='DetailView'] .xlb-goto").ClickAsync();
    await modelEditor.Locator("[data-selected='Views/Order_Compact_DetailView']").WaitForAsync(new() { Timeout = 10_000 });
    await modelEditor.Locator(".xlb-back").ClickAsync();
    await modelEditor.Locator("[data-selected='Views/Order_ListView']").WaitForAsync(new() { Timeout = 10_000 });
    await modelEditor.Locator("tr[data-value='DetailView'] .xlb-reset").ClickAsync();
    await modelEditor.Locator("tr[data-value='DetailView'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    var resetForm = await OpenOrderFromOrderList(page, "ORD-003");
    Assert(!resetForm.Contains("Compact order") && resetForm.Contains("Notes"),
        $"after resetting the DetailView in the Model Editor, Order_ListView opens its own form again (got {resetForm.Replace('\n', ' ')})");

    Step("MODELEDITOR-006: the filter builder round-trips Order_ListView's Criteria; image and multiline values get their editors");
    // Typed criteria open in the filter builder over Order's fields; Apply writes back what the builder holds.
    await OpenListView(page, "Order_ListView", "ORD-001");
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    var filterBuilder = modelEditor.Locator(".xlb-filter-builder");
    var criteriaText = filterBuilder.GetByLabel("Criteria expression");
    // Switching the builder to another criteria value starts from that value, not from the draft left in the first; with
    // Criteria and Filter both empty the builder's criteria do not change between them (Codex review 2).
    await modelEditor.Locator("tr[data-value='Criteria'] .xlb-criteria-builder").ClickAsync();
    await criteriaText.WaitForAsync(new() { Timeout = 10_000 });
    await criteriaText.FillAsync("[Number] = ");
    await criteriaText.PressAsync("Tab");
    await modelEditor.Locator("tr[data-value='Filter'] .xlb-criteria-builder").ClickAsync();
    await filterBuilder.GetByText("Filter: Order").WaitForAsync(new() { Timeout = 10_000 });
    var switchedText = await criteriaText.InputValueAsync();
    Assert(switchedText == "", $"opening the filter builder for Filter does not carry over Criteria's draft (got '{switchedText}')");
    await filterBuilder.Locator(".xlb-criteria-cancel").ClickAsync();
    await filterBuilder.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    var criteriaInput = modelEditor.Locator("tr[data-value='Criteria'] input");
    await criteriaInput.FillAsync("[Number] = 'ORD-001'");
    await criteriaInput.PressAsync("Tab");
    await modelEditor.Locator("tr[data-value='Criteria'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await modelEditor.Locator("tr[data-value='Criteria'] .xlb-criteria-builder").ClickAsync();
    await filterBuilder.WaitForAsync(new() { Timeout = 10_000 });
    var builderText = await filterBuilder.InnerTextAsync();
    Assert(builderText.Contains("Number") && builderText.Contains("ORD-001"),
        $"the filter builder shows the typed Criteria over Order's fields (got {builderText.Replace('\n', ' ')})");
    // Text the builder cannot parse leaves it holding its last valid criteria, so Apply must not write those: the builder stays
    // open with the text to correct (Codex review).
    await criteriaText.FillAsync("[Number] = ");
    await criteriaText.PressAsync("Tab");
    await filterBuilder.Locator(".xlb-criteria-apply").ClickAsync();
    await modelEditor.Locator(".xlb-model-editor-message", new() { HasText = "Correct the criteria first" }).WaitForAsync(new() { Timeout = 10_000 });
    Assert(await filterBuilder.IsVisibleAsync() && await criteriaText.InputValueAsync() == "[Number] = ",
        "Apply with invalid criteria text keeps the filter builder open with that text");
    var criteriaAfterInvalid = await modelEditor.Locator("tr[data-value='Criteria'] input").InputValueAsync();
    Assert(criteriaAfterInvalid == "[Number] = 'ORD-001'",
        $"Apply with invalid criteria text leaves the Criteria value as it was (got '{criteriaAfterInvalid}')");
    await criteriaText.FillAsync("[Number] = 'ORD-001'");
    await criteriaText.PressAsync("Tab");
    await filterBuilder.Locator(".xlb-criteria-apply").ClickAsync();
    await filterBuilder.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    var appliedCriteria = await modelEditor.Locator("tr[data-value='Criteria'] input").InputValueAsync();
    Assert(appliedCriteria == "[Number] = 'ORD-001'", $"Apply writes the filter builder's criteria back as text (got '{appliedCriteria}')");
    // ImageName offers the application's image names ([Editor] ImageGalleryModelEditorControl, IModelView.cs 59).
    // MODELEDITOR-003 review: a DevExpress combo box renders its items only while its list is open.
    var imageNameRow = modelEditor.Locator("tr[data-value='ImageName']");
    await OpenComboList(imageNameRow);
    var imageNameOptions = await page.GetByRole(AriaRole.Option).CountAsync();
    await imageNameRow.Locator(".dxbl-edit-btn-dropdown").First.ClickAsync(); // closes the list again; Escape could close the popup
    Assert(imageNameOptions > 0, $"Order_ListView's ImageName offers the application's image names (got {imageNameOptions})");
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-26-model-editor-criteria.png") });
    var filteredList = await page.InnerTextAsync("body");
    Assert(filteredList.Contains("Data grid with 1 rows"),
        $"after Save Order_ListView applies the Criteria set through the filter builder: one row left (got {filteredList.Replace('\n', ' ')[..Math.Min(400, filteredList.Length)]})");
    // A column's ToolTip takes the multiline string editor (IModelToolTip, CommonInterfaces.cs 562).
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView/Columns/Number");
    Assert(await modelEditor.Locator("tr[data-value='ToolTip'] textarea").CountAsync() == 1, "a column's ToolTip is edited in a text area");
    await ClosePopup(page);
    await modelEditor.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    // Reset and save, for the steps after this one.
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView");
    await modelEditor.Locator("tr[data-value='Criteria'] .xlb-reset").ClickAsync();
    await modelEditor.Locator("tr[data-value='Criteria'] .xlb-pending").WaitForAsync(new() { Timeout = 10_000 });
    await SaveModelEditorAndWaitForReload(page, modelEditor, "ORD-001");
    await page.GetByText("ORD-002", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 15_000 });
    var unfilteredList = await page.InnerTextAsync("body");
    Assert(unfilteredList.Contains("Data grid with 4 rows"), "after resetting the Criteria Order_ListView lists every order again");

    Step("MODELEDITOR-007: a required value left empty is marked and blocks Save, which names the node and the value");
    modelEditor = await OpenModelEditorAt(page, "Views/Order_ListView/Columns/Number");
    var propertyNameInput = modelEditor.Locator("tr[data-value='PropertyName'] input");
    await propertyNameInput.FillAsync("");
    await propertyNameInput.PressAsync("Tab");
    await modelEditor.Locator("tr[data-value='PropertyName'] .xlb-required-missing").WaitForAsync(new() { Timeout = 10_000 });
    await modelEditor.Locator(".xlb-save").ClickAsync();
    var requiredMessage = modelEditor.Locator(".xlb-model-editor-message", new() { HasText = "PropertyName required" });
    await requiredMessage.WaitForAsync(new() { Timeout = 10_000 });
    var requiredText = await requiredMessage.InnerTextAsync();
    Assert(requiredText.Contains("Views/Order_ListView/Columns/Number"),
        $"Save with a required value cleared is refused, naming the node and the value (got '{requiredText}')");
    Assert(await modelEditor.IsVisibleAsync(), "the refused Save leaves the Model Editor open with the edit to correct");
    await ClosePopup(page);
    await modelEditor.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    var headersAfterRefusedSave = await GridHeaders(page);
    Assert(headersAfterRefusedSave.Contains("Number"),
        $"nothing was saved: Order_ListView still shows its Number column (got {string.Join(",", headersAfterRefusedSave)})");

    Step("FREEZE-001: an administrator's frozen column set keeps a column added to the spec later hidden");
    // The case the freeze exists for is a column that did not exist when the column set was frozen, such as a property
    // added to the class later. --extra-column lists Notes, which Order.Layout.cs leaves out; --freeze-order-columns
    // adds the sample host's Fixtures/FrozenOrderColumns.xafml, whose freezing layer stores explicit indexes only for the
    // three columns shown when it was frozen. XAF resolves every other column's index to -1 while the view is frozen
    // (ModelViewLogic.Get_Index); that reaches Notes only because the updater orders columns through GeneratedIndex
    // instead of writing Index. The freeze has to come from an application-level layer: stored in a user's own
    // differences, XAF Blazor ignores it (all columns show), so this cannot be written into Admin's user model.
    KillApp(ref app);
    ResetUserModel(adminId);
    lock (appOutput) appOutput.Clear();
    app = StartApp(blazorProj, appOutput, "--extra-column");
    await WaitForHttpOk(app, appOutput);
    await OpenListView(page, "Order_ListView", "ORD-001");
    await WaitForNoLoading(page);
    var unfrozenHeaders = await GridHeaders(page);
    Console.WriteLine("    headers, not frozen: " + string.Join(" | ", unfrozenHeaders));
    Assert(string.Join(",", unfrozenHeaders) == "Number,Customer,Order Date,Notes",
        $"control: with --extra-column and no freeze, Notes is the fourth column (got {string.Join(",", unfrozenHeaders)})");

    KillApp(ref app);
    lock (appOutput) appOutput.Clear();
    app = StartApp(blazorProj, appOutput, "--extra-column --freeze-order-columns");
    await WaitForHttpOk(app, appOutput);
    await OpenListView(page, "Order_ListView", "ORD-001");
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-17-frozen-column-set.png") });
    var frozenHeaders = await GridHeaders(page);
    Console.WriteLine("    headers, frozen: " + string.Join(" | ", frozenHeaders));
    Assert(string.Join(",", frozenHeaders) == "Number,Customer,Order Date",
        $"the frozen column set is unchanged: Notes, added to the spec after the freeze, is not shown (got {string.Join(",", frozenHeaders)})");
    var frozenGrid = page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First;
    await frozenGrid.Locator("th.dxbl-grid-header").Filter(new() { HasText = "Number" }).First.ClickAsync(new() { Button = MouseButton.Right });
    await page.WaitForTimeoutAsync(800);
    await page.Locator("[role=menuitem], .dxbl-context-menu-item, .dxbl-menu-item").Filter(new() { HasText = "Column Chooser" }).First.ClickAsync();
    var frozenChooser = page.Locator(".dxbl-popup, .dxbl-grid-column-chooser, .dxbl-column-chooser").Filter(new() { HasText = "Order Date" }).First;
    await frozenChooser.WaitForAsync(new() { Timeout = 10_000 });
    Assert((await frozenChooser.InnerTextAsync()).Contains("Notes"), "the column chooser still offers Notes");
    await page.Keyboard.PressAsync("Escape");
    KillApp(ref app);
    ResetUserModel(adminId);

    Step("NEST-001: a column over a reference's member (Customer.City) shows the customer's data and exports as a chained lambda");
    // --nested-column registers Order's columns plus Customer.City, the dotted path XAF's own generator gives a column over
    // a reference's member. The updater adds that column the way the generator does, and the export has to print it.
    lock (appOutput) appOutput.Clear();
    app = StartApp(blazorProj, appOutput, "--nested-column");
    await WaitForHttpOk(app, appOutput);
    await OpenListView(page, "Order_ListView", "ORD-001");
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-20-nested-column.png") });
    var nestedHeaders = (await GridHeaders(page)).ToList();
    Console.WriteLine("    headers: " + string.Join(" | ", nestedHeaders));
    Assert(nestedHeaders.Count == 4 && nestedHeaders[3].Contains("City"),
        $"the Customer.City column is the fourth column (got {string.Join(",", nestedHeaders)})");
    var nestedRows = await page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First.EvaluateAsync<string[]>(
        "g => [...g.querySelectorAll('tr[role=row]')].map(r => [...r.querySelectorAll('td')].map(c => c.innerText.trim()).filter(t => t).join('|')).filter(t => t)");
    Console.WriteLine("    rows: " + string.Join(" / ", nestedRows));
    Assert(nestedRows.Any(r => r.StartsWith("ORD-001|") && r.EndsWith("|Leeuwarden")), "ORD-001's row shows Acme Corp's city, Leeuwarden");
    Assert(nestedRows.Any(r => r.StartsWith("ORD-003|") && r.EndsWith("|Groningen")), "ORD-003's row shows Globex's city, Groningen");
    var nestedExport = await ExportLayoutCode(page, Path.Combine(screenshotDir, "e2e-20-nested-column-export.png"));
    await ClosePopup(page);
    Console.WriteLine("    exported nested column: " + (nestedExport.Split('\n').FirstOrDefault(l => l.Contains("Customer.City"))?.Trim() ?? "(none)"));
    Assert(nestedExport.Contains(".Column(x => x.Customer.City"), "the export prints the nested column as .Column(x => x.Customer.City...)");
    Assert(!nestedExport.Contains("nested path"), "the export skips nothing for the nested column");
    KillApp(ref app);

    Step("Session 5: a broken layout is reported at startup (host started with --break-layout)");
    KillApp(ref app);
    lock (appOutput) appOutput.Clear();
    // The XAF Blazor host builds the application (and so the model) while the host starts, so the diagnostic
    // kills the process before it ever listens. The fixture breaks two views independently, and the check must report
    // both in one run rather than stop at the first. Expect: no HTTP, non-zero exit, XLB001 and XLB003 in the output.
    app = StartApp(blazorProj, appOutput, "--break-layout");
    var exited = app.WaitForExit(90_000);
    if (exited) app.WaitForExit(); // flushes the async stdout/stderr readers
    string log; lock (appOutput) log = appOutput.ToString();
    var line = log.Split('\n').FirstOrDefault(l => l.Contains("XLB001"))?.Trim() ?? "(not in app output)";
    var columnLine = log.Split('\n').FirstOrDefault(l => l.Contains("XLB003"))?.Trim() ?? "(not in app output)";
    Console.WriteLine("    app output: " + line);
    Console.WriteLine("    app output: " + columnLine);
    Assert(exited, "host process exits instead of serving");
    Assert(app.ExitCode != 0, $"host exit code is non-zero (got {app.ExitCode})");
    Assert(!await IsServing(), "nothing is serving on :5100 after the failed start");
    Assert(line.Contains("XLB001"), "XLB001 is reported in the host output");
    Assert(line.Contains("Customer_DetailView") && line.Contains("InternalCode"), "the diagnostic names the view id and the member");
    Assert(columnLine.Contains("Order_ListView") && columnLine.Contains("Lines"), "XLB003 for Order_ListView is reported in the same startup");
    var twiceLine = log.Split('\n').FirstOrDefault(l => l.Contains("placed twice"))?.Trim() ?? "(not in app output)";
    Console.WriteLine("    app output: " + twiceLine);
    Assert(twiceLine.Contains("Order: member 'Number' is placed twice"),
        "Order's registered detail factory fails in the same startup as Order's XLB003: checked at startup, not at Register, and per view");

    Step("TEST-001: a registered factory throwing a non-layout exception stops a fail-fast startup");
    KillApp(ref app);
    lock (appOutput) appOutput.Clear();
    app = StartApp(blazorProj, appOutput, "--break-layout --break-factory");
    var factoryExited = app.WaitForExit(90_000);
    if (factoryExited) app.WaitForExit(); // flushes the async stdout/stderr readers
    string factoryLog; lock (appOutput) factoryLog = appOutput.ToString();
    var factoryLine = factoryLog.Split('\n').FirstOrDefault(l => l.Contains("Customer columns factory broke"))?.Trim() ?? "(not in app output)";
    Console.WriteLine("    app output: " + factoryLine);
    Assert(factoryExited, "host process exits instead of serving");
    Assert(app.ExitCode != 0, $"host exit code is non-zero (got {app.ExitCode})");
    Assert(!await IsServing(), "nothing is serving on :5100 after the failed start");
    Assert(factoryLine.Contains("InvalidOperationException"),
        "the factory's own exception ends a fail-fast startup (only layout errors are collected with fail-fast on)");

    Step("CHECK-002: with FailFastOnLayoutErrors off, the broken layouts are logged and the host serves XAF's own layout");
    // The switch defaults to off; the sample turns it on in appsettings.Development.json and the command line turns it
    // off again here. XAF traces to eXpressAppFramework.log next to the executable, a file that grows across runs, so
    // only what this start appends is searched.
    KillApp(ref app);
    lock (appOutput) appOutput.Clear();
    var xafLog = Path.Combine(blazorProj, "bin", "Debug", "net10.0", "eXpressAppFramework.log");
    var logStart = File.Exists(xafLog) ? new FileInfo(xafLog).Length : 0;
    // The override has to come first: .NET's command-line configuration reads "--key value", so a bare --break-layout
    // would swallow the next argument as its own value and leave the appsettings value in charge.
    app = StartApp(blazorProj, appOutput, "--XafLayoutBuilder:FailFastOnLayoutErrors=false --break-layout --break-factory");
    await WaitForHttpOk(app, appOutput);
    Assert(await IsServing(), "the host serves despite the broken layouts");
    for (var attempt = 0; attempt < 3; attempt++) {
        try { await page.GotoAsync($"{BaseUrl}/Customer_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle }); }
        catch (PlaywrightException ex) when (ex.Message.Contains("interrupted")) { await page.WaitForLoadStateAsync(LoadState.NetworkIdle); continue; }
        if (page.Url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase)) { await Login(page); continue; }
        if (page.Url.Contains("Customer_ListView", StringComparison.OrdinalIgnoreCase)) break;
    }
    await page.GetByText("Acme Corp", new() { Exact = true }).First.ClickAsync();
    await page.WaitForFunctionAsync("() => [...document.querySelectorAll('input')].some(i => i.value === 'Acme Corp')", null, new() { Timeout = 30_000 });
    await WaitForNoLoading(page);
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-16-degraded-customer.png") });
    var degradedForm = page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First;
    var degradedGroups = await degradedForm.EvaluateAsync<string[]>(
        @"f => [...f.querySelectorAll('[role=group].dxbl-fl-group')].map(g => g.querySelector(':scope > .dxbl-group > .dxbl-group-header')?.innerText.trim() ?? '(no header)')");
    Console.WriteLine("    Customer groups: " + string.Join(" | ", degradedGroups));
    Assert(await degradedForm.Locator("label.xaf-item-city").CountAsync() > 0, "Customer's DetailView still shows City");
    Assert(!degradedGroups.Contains("Identification") && !degradedGroups.Contains("Other"),
        $"no builder layout was applied to Customer: XAF's own layout renders (got {string.Join(",", degradedGroups)})");
    // The broken spec's own caption: present only if the rejected spec was partly applied, which is exactly what
    // checking before mutating (APPLY-001) prevents. City alone cannot tell, because a partial layout shows it too.
    Assert(!degradedGroups.Contains("Broken layout"),
        $"the rejected Customer spec left nothing behind: no \"Broken layout\" group (got {string.Join(",", degradedGroups)})");
    await page.GotoAsync($"{BaseUrl}/Order_ListView", new() { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByText("ORD-001", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 30_000 });
    Assert(await page.GetByText("ORD-001", new() { Exact = true }).CountAsync() > 0, "Order_ListView, whose columns spec was rejected, still lists its orders");
    var degradedHeaders = await page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First.EvaluateAsync<string[]>(
        @"g => [...g.querySelectorAll('th.dxbl-grid-header')].map(h => h.textContent.replace(/No filter applied/g,'').trim().replace(/\s+/g,' ')).filter(t => t && t !== 'Selection')");
    Console.WriteLine("    Order headers: " + string.Join(" | ", degradedHeaders));
    Assert(degradedHeaders.Contains("Number") && !degradedHeaders.Contains("Broken number"),
        $"the rejected Order columns spec left nothing behind: stock \"Number\" header, no \"Broken number\" (got {string.Join(",", degradedHeaders)})");
    string appendedLog;
    using (var stream = new FileStream(xafLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
        stream.Seek(logStart, SeekOrigin.Begin);
        appendedLog = new StreamReader(stream).ReadToEnd();
    }
    Assert(appendedLog.Contains("XLB001 Customer_DetailView"), "XLB001 is written to eXpressAppFramework.log");
    Assert(appendedLog.Contains("XLB003 Order_ListView"), "XLB003 is written to eXpressAppFramework.log");
    Assert(appendedLog.Contains("Customer columns factory broke"), "Customer's columns factory exception is logged instead of stopping the host (TEST-001)");
    Assert(appendedLog.Contains("Order: member 'Number' is placed twice"), "Order's throwing detail factory is logged instead of stopping the host");
    // The updaters log their own failures whenever a view is generated (opening Order_ListView also generates
    // Order_DetailView's layout), so a logged message alone does not show that the startup check went on. The check's
    // own report is one "N layout problems" entry: it must hold Customer's factory exception and Order's failure, which
    // the check only reaches after Customer. Narrowing the check's catch to layout errors makes this assertion fail.
    var checkReportAt = appendedLog.IndexOf("layout problems:", StringComparison.Ordinal);
    var checkReport = checkReportAt < 0 ? "" : appendedLog[checkReportAt..];
    var nextEntry = System.Text.RegularExpressions.Regex.Match(checkReport, @"\n\d\d\.\d\d\.\d\d \d\d:\d\d:\d\d\.\d{3}\t");
    if (nextEntry.Success) checkReport = checkReport[..nextEntry.Index];
    Assert(checkReport.Contains("Customer columns factory broke") && checkReport.Contains("Order: member 'Number' is placed twice"),
        "the startup check's one report holds Customer's factory exception and Order's later failure: a non-layout exception does not end the degraded check (TEST-001)");

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
    // LOGIN-001: the DOM value is not the server's. XAF's text editor sends its value only when it loses focus
    // (DxTextBoxAdapter sets BindValueMode.OnLostFocus unless ImmediatePostData; dxdocs, DxTextBox.BindValueMode), so a
    // click straight after the fill could reach the server first and log on with an empty user name. Tab commits the value
    // before the click, and a login that still stays on the login page is tried once more, logged, instead of ending the gate.
    for (var attempt = 1; ; attempt++)
    {
        await page.GotoAsync($"{BaseUrl}/LoginPage", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var userField = page.Locator("input[type='text'], input[name*='sername']").First;
        await userField.WaitForAsync(new() { Timeout = 20_000 });
        for (var i = 0; i < 10; i++)
        {
            await userField.FillAsync("Admin");
            if (await userField.InputValueAsync() == "Admin") break;
            await Task.Delay(300);
        }
        await userField.PressAsync("Tab");
        await page.WaitForTimeoutAsync(500);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log In" }).ClickAsync();
        try
        {
            await page.WaitForURLAsync(url => !url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase),
                new() { Timeout = attempt == 1 ? 10_000 : 20_000 });
            break;
        }
        catch (TimeoutException) when (attempt == 1)
        {
            Console.WriteLine("    login stayed on the login page; trying once more (LOGIN-001)");
        }
    }
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });
}

// A fresh page load starts a new Blazor circuit, which reloads the user model differences.
static async Task OpenListView(IPage page, string viewId, string seededText)
{
    // A fresh circuit may restore the last open view or land on the login page; retry the navigation like OpenOrd001Detail.
    for (var attempt = 0; attempt < 3; attempt++) {
        try { await page.GotoAsync($"{BaseUrl}/{viewId}", new() { WaitUntil = WaitUntilState.NetworkIdle }); }
        catch (PlaywrightException ex) when (ex.Message.Contains("interrupted")) { await page.WaitForLoadStateAsync(LoadState.NetworkIdle); continue; }
        if (page.Url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase)) { await Login(page); continue; }
        if (page.Url.Contains(viewId, StringComparison.OrdinalIgnoreCase)) break;
    }
    await page.GetByText(seededText, new() { Exact = true }).First.WaitForAsync(new() { Timeout = 30_000 });
}

// MODELEDITOR-003 review: the Model Editor's drop-downs are DevExpress combo boxes, not native selects. The list opens from the
// combo's drop-down button (with free text allowed, clicking the input does not open it) and renders in a popup attached to the
// page body, so the option is picked from the page by its role (DxComboBox research, docs/api-notes.md).
static async Task OpenComboList(ILocator scope) {
    await scope.Locator(".dxbl-edit-btn-dropdown").First.ClickAsync();
    await scope.Locator("input[role=combobox]").First.EvaluateAsync(@"i => new Promise((resolve, reject) => {
        const start = Date.now();
        (function poll() {
            if (i.getAttribute('aria-expanded') === 'true') resolve();
            else if (Date.now() - start > 10000) reject(new Error('the combo box list did not open'));
            else setTimeout(poll, 100);
        })();
    })");
}

static async Task PickComboItem(IPage page, ILocator scope, string text) {
    await OpenComboList(scope);
    await page.GetByRole(AriaRole.Option, new() { Name = text, Exact = true }).First.ClickAsync();
}

// MODELEDITOR-008: the browser's culture, through the request-culture cookie XAF Blazor's language switcher writes
// (XafCultureInfoService.SetNewCultureAsync: CookieRequestCultureProvider.MakeCookieValue, XafLanguageService.cs 108-113).
// The next navigation starts a circuit in that culture, which is the aspect its model reads.
static Task SetCulture(IPage page, string culture) =>
    page.Context.AddCookiesAsync([new Cookie { Name = ".AspNetCore.Culture", Value = $"c={culture}|uic={culture}", Url = BaseUrl }]);

// Header captions of the grid on the active tab, without the filter button's accessibility text or the selection column.
static Task<string[]> GridHeaders(IPage page) =>
    page.Locator("[role=tabpanel].dxbl-active .dxbl-grid").First.EvaluateAsync<string[]>(
        @"g => [...g.querySelectorAll('th.dxbl-grid-header')].map(h => h.textContent.replace(/No filter applied/g,'').trim().replace(/\s+/g,' ')).filter(t => t && t !== 'Selection')");

// Opens an order from Order_ListView, as a user clicks it, and returns the text of the form in the active tab (inactive
// tabs stay in the DOM).
static async Task<string> OpenOrderFromOrderList(IPage page, string number)
{
    await OpenListView(page, "Order_ListView", number);
    await page.GetByText(number, new() { Exact = true }).First.ClickAsync();
    await page.WaitForFunctionAsync($"() => [...document.querySelectorAll('input')].some(i => i.value === '{number}')", null, new() { Timeout = 30_000 });
    await WaitForNoLoading(page);
    return await page.Locator("[role=tabpanel].dxbl-active .detail-view-content").First.InnerTextAsync();
}

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
// A layout item's box in the form: the form layout item element holding the member's xaf-item label (E2E4-001's drag).
static async Task<LocatorBoundingBoxResult> LayoutItemBox(ILocator form, string member) =>
    await form.Locator("dxbl-form-layout-item", new() { Has = form.Page.Locator($".xaf-item-{member}") }).First.BoundingBoxAsync()
        ?? throw new Exception($"the form has no layout item for {member}");

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

static int Sql(string sql, params (string Name, string Value)[] parameters) => Retry(() =>
{
    using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
    foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
    return cmd.ExecuteNonQuery();
});

static string? SqlScalar(string sql) => Retry<string?>(() =>
{
    using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
    return cmd.ExecuteScalar()?.ToString();
});

// LocalDB drops the odd connection around the host restarts this gate does; one retry covers it.
static T Retry<T>(Func<T> action)
{
    try { return action(); }
    catch (Microsoft.Data.SqlClient.SqlException ex)
    {
        Console.WriteLine("    SQL retry after: " + ex.Message.Split('\n')[0].Trim());
        Thread.Sleep(2000);
        return action();
    }
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
    // Show the top of the file (namespace, export comment) instead of wherever the memo happens to be scrolled.
    await page.EvaluateAsync("() => document.querySelectorAll('textarea').forEach(t => { t.scrollTop = 0; })");
    await page.ScreenshotAsync(new() { Path = screenshotPath });
    return code;
}

static async Task ClosePopup(IPage page)
{
    var cancel = page.Locator(".dxbl-popup, .dxbl-modal").GetByRole(AriaRole.Button, new() { Name = "Cancel" });
    if (await cancel.CountAsync() > 0) await cancel.First.ClickAsync();
    else await page.Keyboard.PressAsync("Escape");
}

// Tools tab -> Edit Model (ModelEditor add-on); expands the tree down to the node and selects it.
static async Task<ILocator> OpenModelEditorAt(IPage page, string nodePath)
{
    // Running an action or closing a popup can drop the toolbar back to the Home tab, so select Tools every time.
    await page.GetByText("Tools", new() { Exact = true }).First.ClickAsync();
    var editModel = page.GetByText("Edit Model", new() { Exact = true }).First;
    await editModel.WaitForAsync(new() { Timeout = 10_000 });
    await editModel.ClickAsync();
    var editor = page.Locator(".xlb-model-editor");
    await editor.WaitForAsync(new() { Timeout = 15_000 });
    var ids = nodePath.Split('/');
    for (var i = 1; i < ids.Length; i++)
        await editor.Locator($"[data-expand='{string.Join("/", ids[..i])}']").ClickAsync();
    await editor.Locator($"[data-node='{nodePath}']").ClickAsync();
    await editor.Locator($"[data-selected='{nodePath}']").WaitForAsync(new() { Timeout = 10_000 });
    return editor;
}

// The Model Editor's Save reloads the page (MODELEDITOR-002). Waits for that navigation itself, so a Save that did not
// reload fails here instead of being covered by a page load of the gate's own.
static async Task SaveModelEditorAndWaitForReload(IPage page, ILocator editor, string seededText)
{
    await page.RunAndWaitForNavigationAsync(() => editor.Locator(".xlb-save").ClickAsync(), new() { Timeout = 30_000 });
    await page.GetByText(seededText, new() { Exact = true }).First.WaitForAsync(new() { Timeout = 30_000 });
    await WaitForNoLoading(page);
}

// Round-trip helpers: cut one builder expression out of C# text and compare modulo whitespace.
static string BuilderExpression(string code, string start)
{
    var from = code.IndexOf(start, StringComparison.Ordinal);
    if (from < 0) throw new Exception($"'{start}' not found in the code");
    var to = code.IndexOf(".Build()", from, StringComparison.Ordinal);
    return code[from..(to + ".Build()".Length)];
}

static string NormalizeCode(string s) =>
    string.Join("\n", s.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

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
