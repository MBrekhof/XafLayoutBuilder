using System.Diagnostics;
using Microsoft.Playwright;

// E2E phase gate for XafLayoutBuilder. Same shape as XafReportScheduler's harness.
// Run with:  dotnet run --project XafLayoutBuilder.E2ETests
// Exit codes: 0 pass, 1 fail, 2 Playwright browser missing.
//
// Session 1 level: the sample app builds, starts on :5100, Admin logs in, the Orders
// ListView renders the seeded rows with XAF's default layout. Later sessions add the
// builder assertions listed in XafLayoutBuilder-START.md section 8.

const string BaseUrl = "http://localhost:5100";

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

    Step("Open ORD-001 DetailView (default XAF layout)");
    await page.GetByText("ORD-001", new() { Exact = true }).First.ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("Order_DetailView", StringComparison.OrdinalIgnoreCase), new() { Timeout = 20_000 });
    // NetworkIdle fires while the ListView is still on screen; wait for the form to bind ORD-001
    // into an editor before reading the DOM, otherwise the grid's column headers satisfy the assert.
    await page.WaitForFunctionAsync("() => [...document.querySelectorAll('input')].some(i => i.value === 'ORD-001')",
        null, new() { Timeout = 30_000 });
    var detailText = await page.InnerTextAsync("body");
    Assert(detailText.Contains("Sync Token"), "default layout still shows SyncToken (nothing custom yet)");
    await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-03-order-detailview.png") });

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

static async Task<IPage> NewPage(IBrowser browser)
{
    var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
    page.SetDefaultTimeout(30_000);
    page.SetDefaultNavigationTimeout(30_000);
    return page;
}

static Process StartApp(string blazorProj, System.Text.StringBuilder appOutput)
{
    // launchSettings.json's applicationUrl overrides ASPNETCORE_URLS unless --no-launch-profile
    // is passed; --urls on the command line wins over both. Belt and braces.
    var psi = new ProcessStartInfo("dotnet",
        $"run --no-build --no-launch-profile --project \"{blazorProj}\" --urls {BaseUrl}")
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
