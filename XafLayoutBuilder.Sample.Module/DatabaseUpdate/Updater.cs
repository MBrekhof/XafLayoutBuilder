using DevExpress.ExpressApp;
using DevExpress.Data.Filtering;
using DevExpress.Persistent.Base;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.ExpressApp.EF;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using XafLayoutBuilder.Sample.Module.BusinessObjects;
using Microsoft.Extensions.DependencyInjection;

namespace XafLayoutBuilder.Sample.Module.DatabaseUpdate;

// For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Updating.ModuleUpdater
public class Updater : ModuleUpdater {
    public Updater(IObjectSpace objectSpace, Version currentDBVersion) :
        base(objectSpace, currentDBVersion) {
    }
    public override void UpdateDatabaseAfterUpdateSchema() {
        base.UpdateDatabaseAfterUpdateSchema();
        //string name = "MyName";
        //EntityObject1 theObject = ObjectSpace.FirstOrDefault<EntityObject1>(u => u.Name == name);
        //if(theObject == null) {
        //    theObject = ObjectSpace.CreateObject<EntityObject1>();
        //    theObject.Name = name;
        //}

        // The code below creates users and roles for testing purposes only.
        // In production code, you can create users and assign roles to them automatically, as described in the following help topic:
        // https://docs.devexpress.com/eXpressAppFramework/119064/data-security-and-safety/security-system/authentication
#if !RELEASE
        // If a role doesn't exist in the database, create this role
        var defaultRole = CreateDefaultRole();
        var adminRole = CreateAdminRole();

        ObjectSpace.CommitChanges(); //This line persists created object(s).

        UserManager userManager = ObjectSpace.ServiceProvider.GetRequiredService<UserManager>();

        // If a user named 'User' doesn't exist in the database, create this user
        if(userManager.FindUserByName<ApplicationUser>(ObjectSpace, "User") == null) {
            // Set a password if the standard authentication type is used
            string EmptyPassword = "";
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "User", EmptyPassword, (user) => {
                // Add the Users role to the user
                user.Roles.Add(defaultRole);
            });
        }

        // If a user named 'Admin' doesn't exist in the database, create this user
        if(userManager.FindUserByName<ApplicationUser>(ObjectSpace, "Admin") == null) {
            // Set a password if the standard authentication type is used
            string EmptyPassword = "";
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "Admin", EmptyPassword, (user) => {
                // Add the Administrators role to the user
                user.Roles.Add(adminRole);
            });
        }

        ObjectSpace.CommitChanges(); //This line persists created object(s).
#endif

        // Sample data so the ListView E2E assertions have rows to look at.
        if(!ObjectSpace.GetObjects<Customer>().Any()) {
            var acme = ObjectSpace.CreateObject<Customer>();   acme.Name = "Acme Corp"; acme.City = "Leeuwarden";
            var globex = ObjectSpace.CreateObject<Customer>(); globex.Name = "Globex";  globex.City = "Groningen";
            var today = DateTime.Today;
            var ord1 = ObjectSpace.CreateObject<Order>();
            Seed(ord1, "ORD-001", today.AddDays(-2), acme);
            Seed(ObjectSpace.CreateObject<Order>(), "ORD-002", today.AddDays(-30), acme);
            Seed(ObjectSpace.CreateObject<Order>(), "ORD-003", today.AddDays(-1), globex);
            var svc = ObjectSpace.CreateObject<ServiceOrder>();
            Seed(svc, "SRV-001", today.AddDays(-5), globex);
            svc.ServiceDate = today.AddDays(3); svc.Technician = "J. de Vries"; svc.OriginalOrder = ord1;
            ObjectSpace.CommitChanges();

            void Seed(Order o, string number, DateTime date, Customer c) {
                o.Number = number; o.OrderDate = date; o.Customer = c;
                o.Notes = $"Seeded {number}"; o.SyncToken = Guid.NewGuid().ToString("N");
                var line = ObjectSpace.CreateObject<OrderLine>();
                line.Product = "Widget"; line.Quantity = 2; line.UnitPrice = 12.50m;
                o.Lines.Add(line);
            }
        }
    }
    public override void UpdateDatabaseBeforeUpdateSchema() {
        base.UpdateDatabaseBeforeUpdateSchema();
    }
    PermissionPolicyRole CreateAdminRole() {
        PermissionPolicyRole adminRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "Administrators");
        if(adminRole == null) {
            adminRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            adminRole.Name = "Administrators";
            adminRole.IsAdministrative = true;
        }
        // MODELEDITOR-001: Edit Model asks ModelOperationPermissionRequest, which only CanEditModel grants
        // (PermissionsExtractor.cs 54-55); IsAdministrative alone does not. Set on an existing role too.
        adminRole.CanEditModel = true;
        return adminRole;
    }
    PermissionPolicyRole CreateDefaultRole() {
        PermissionPolicyRole defaultRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(role => role.Name == "Default");
        if(defaultRole == null) {
            defaultRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            defaultRole.Name = "Default";

            defaultRole.AddObjectPermissionFromLambda<ApplicationUser>(SecurityOperations.Read, cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/MyDetails", SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "ChangePasswordOnFirstLogon", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "StoredPassword", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<PermissionPolicyRole>(SecurityOperations.Read, SecurityPermissionState.Deny);
            defaultRole.AddObjectPermission<ModelDifference>(SecurityOperations.ReadWriteAccess, "UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddObjectPermission<ModelDifferenceAspect>(SecurityOperations.ReadWriteAccess, "Owner.UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifference>(SecurityOperations.Create, SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifferenceAspect>(SecurityOperations.Create, SecurityPermissionState.Allow);
        }
        return defaultRole;
    }
}
