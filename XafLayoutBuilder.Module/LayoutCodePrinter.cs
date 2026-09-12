using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>
/// Turns the view a user is looking at into the printed `{Type}.Layout.cs`, or into `{Type}.layout.json`. Shared by the
/// export popups and by the Blazor add-on's clipboard and download actions, so every one of them produces the same text,
/// and the C# and JSON forms come from the same export.
/// </summary>
public static class LayoutCodePrinter {
    public static (string FileName, string Code) ForView(XafApplication application, View view) {
        var export = Export(application, view);
        var fileName = $"{export.Type.Name}.Layout.cs";
        export.Notes.Insert(0, $"Exported from the running model ({DateTime.Now:yyyy-MM-dd HH:mm}); every layer applied. Save as {fileName}.");
        export.Notes.Insert(1, $"Views: {export.Views}.");
        return (fileName, CSharpLayoutPrinter.PrintClass(export.Type.Namespace, export.Type.Name, export.Detail, export.Columns, export.Notes));
    }

    /// <summary>
    /// The same export as a <see cref="LayoutSpecs"/> document, for <c>LayoutRegistry.RegisterJson</c> or a generator.
    /// JSON has no comments, so what the C# export prints as its leading comment (the view ids, skipped items) is not in it.
    /// </summary>
    public static (string FileName, string Json) JsonForView(XafApplication application, View view) {
        var export = Export(application, view);
        return ($"{export.Type.Name}.layout.json", LayoutSpecJson.Serialize(new LayoutSpecs(export.Detail, export.Columns)));
    }

    static (Type Type, DetailLayoutSpec? Detail, ListColumnsSpec? Columns, List<string> Notes, string Views) Export(XafApplication application, View view) {
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
        var views = $"{detailView?.Id ?? "(no DetailView)"}, {listView?.Id ?? "(no ListView)"}, {lookupView?.Id ?? "(no lookup)"}";
        return (type, detail, columns, notes, views);
    }
}
