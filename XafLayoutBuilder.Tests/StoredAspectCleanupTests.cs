using DevExpress.ExpressApp;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// Codex review (MODELEDITOR-002): the cleanup writes the stored differences directly, so it must keep the version guard
// ModelDifferenceDbStore.SaveDifference applies (ModelDifferenceDbStore.cs 181): a stored difference newer than the model
// being saved (an administrator copied differences to the user meanwhile) is left alone.
public class StoredAspectCleanupTests {
    [Fact]
    public void AStoredDifferenceNewerThanTheSavedLayer_IsLeftAlone() {
        Assert.False(StoredAspectCleanup.StoreAcceptsSaveFrom(new Difference { Version = 3 }, layerVersion: 2));
        Assert.True(StoredAspectCleanup.StoreAcceptsSaveFrom(new Difference { Version = 2 }, layerVersion: 2));
        Assert.True(StoredAspectCleanup.StoreAcceptsSaveFrom(new Difference { Version = 1 }, layerVersion: 2));
    }

    sealed class Difference : IModelDifference {
        public string UserId { get; set; } = "";
        public string UserName => "";
        public string ContextId { get; set; } = "";
        public int Version { get; set; }
        public IList<IModelDifferenceAspect> Aspects { get; } = [];
    }
}
