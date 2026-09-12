using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Turns the view a user is looking at into the printed `{Type}.Layout.cs`. Shared by the export popup and by the
/// Blazor add-on's clipboard action, so both produce exactly the same text.
/// </summary>
public static class LayoutCodePrinter {
    public static (string FileName, string Code) ForView(XafApplication application, View view) {
        var type = view.ObjectTypeInfo.Type;
        var modelClass = application.Model.BOModel.GetClass(type);
        var notes = new List<string>();

        // Start document section 6: export what the user is looking at, not just the default views.
        var detailView = view is DetailView dv ? dv.Model : modelClass.DefaultDetailView;
        var listView = modelClass.DefaultListView;
        var lookupView = modelClass.DefaultLookupListView;
        if (view is ListView lv) {
            if (lv.Model.GetValue<bool>(ModelViewsNodesGenerator.IsLookupListView)) lookupView = lv.Model;
            else listView = lv.Model;
        }

        DetailLayoutSpec? detail = null;
        if (detailView is not null) {
            var (spec, skipped) = LayoutExporter.ExportDetail(detailView);
            detail = spec;
            notes.AddRange(skipped);
        }
        ListColumnsSpec? columns = null;
        if (listView is not null) {
            var (spec, skipped) = LayoutExporter.ExportColumns(listView, lookupView);
            columns = spec;
            notes.AddRange(skipped);
        }

        var fileName = $"{type.Name}.Layout.cs";
        notes.Insert(0, $"Exported from the running model ({DateTime.Now:yyyy-MM-dd HH:mm}); every layer applied. Save as {fileName}.");
        notes.Insert(1, $"Views: {detailView?.Id ?? "(no DetailView)"}, {listView?.Id ?? "(no ListView)"}, {lookupView?.Id ?? "(no lookup)"}.");
        return (fileName, CSharpLayoutPrinter.PrintClass(type.Namespace, type.Name, detail, columns, notes));
    }
}
