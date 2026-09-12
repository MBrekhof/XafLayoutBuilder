using System.Reflection;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.DesignTime;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.Utils;

namespace XafLayoutBuilder.Sample.Blazor.Server;

public class Program : IDesignTimeApplicationFactory {
    static bool ContainsArgument(string[] args, string argument) {
        return args.Any(arg => arg.TrimStart('/').TrimStart('-').ToLower() == argument.ToLower());
    }
    public static int Main(string[] args) {
        if(ContainsArgument(args, "help") || ContainsArgument(args, "h")) {
            Console.WriteLine("Updates the database when its version does not match the application's version.");
            Console.WriteLine();
            Console.WriteLine($"    {Assembly.GetExecutingAssembly().GetName().Name}.exe --updateDatabase [--forceUpdate --silent]");
            Console.WriteLine();
            Console.WriteLine("--forceUpdate - Marks that the database must be updated whether its version matches the application's version or not.");
            Console.WriteLine("--silent - Marks that database update proceeds automatically and does not require any interaction with the user.");
            Console.WriteLine();
            Console.WriteLine($"Exit codes: 0 - {DBUpdaterStatus.UpdateCompleted}");
            Console.WriteLine($"            1 - {DBUpdaterStatus.UpdateError}");
            Console.WriteLine($"            2 - {DBUpdaterStatus.UpdateNotNeeded}");
        }
        else {
            DevExpress.ExpressApp.Blazor.Editors.LookupPropertyEditor.DefaultUseViewMode = true;
            DevExpress.ExpressApp.FrameworkSettings.DefaultSettingsCompatibilityMode = DevExpress.ExpressApp.FrameworkSettingsCompatibilityMode.Latest;
            DevExpress.ExpressApp.Security.SecurityStrategy.AutoAssociationReferencePropertyMode = DevExpress.ExpressApp.Security.ReferenceWithoutAssociationPermissionsMode.AllMembers;
            // E2E fixture: register a layout that fails XLB001, to prove the diagnostics fire at startup.
            if(ContainsArgument(args, "break-layout")) {
                XafLayoutBuilder.Sample.Module.BrokenLayouts.Register();
            }
            // E2E fixture: a registered columns factory that throws a non-layout exception (TEST-001).
            if(ContainsArgument(args, "break-factory")) {
                XafLayoutBuilder.Sample.Module.BrokenLayouts.RegisterThrowingFactory();
            }
            // E2E fixture: Order's columns plus Notes, for the frozen-column-set check (FREEZE-001).
            if(ContainsArgument(args, "extra-column")) {
                XafLayoutBuilder.Sample.Module.GateFixtures.RegisterExtraOrderColumn();
            }
            // E2E fixture: an administrator's frozen Order_ListView column set, as an application-level difference (FREEZE-001).
            if(ContainsArgument(args, "freeze-order-columns")) {
                SampleBlazorModule.FreezeOrderColumnsFixture = true;
            }
            IHost host = CreateHostBuilder(args).Build();
            if(ContainsArgument(args, "updateDatabase")) {
                using(var serviceScope = host.Services.CreateScope()) {
                    return serviceScope.ServiceProvider.GetRequiredService<DevExpress.ExpressApp.Utils.IDBUpdater>().Update(ContainsArgument(args, "forceUpdate"), ContainsArgument(args, "silent"));
                }
            }
            else {
                host.Run();
            }
        }
        return 0;
    }
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => {
                webBuilder.UseStartup<Startup>();
            });
    XafApplication IDesignTimeApplicationFactory.Create() {
        IHostBuilder hostBuilder = CreateHostBuilder(Array.Empty<string>());
        return DesignTimeApplicationFactoryHelper.Create(hostBuilder);
    }
}
