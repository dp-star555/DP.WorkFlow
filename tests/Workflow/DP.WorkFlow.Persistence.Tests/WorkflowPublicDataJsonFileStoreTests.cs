using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPublicDataJsonFileStoreTests
{
    [Fact]
    public void Save_ReplacesByKeyAndSurvivesReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), "dp-workflow-tests", Guid.NewGuid().ToString("N"), "public-data.json");
        try
        {
            var store = new WorkflowPublicDataJsonFileStore(path);
            Assert.Empty(store.Load());
            store.Save(new WorkflowPersistedPublicData("Jobs", WorkflowPublicDataKind.Queue, E_SignalValueType.Int32, new[] { "1", "2" }));
            store.Save(new WorkflowPersistedPublicData("Ready", WorkflowPublicDataKind.Signal, E_SignalValueType.Boolean, new[] { "True" }));
            store.Save(new WorkflowPersistedPublicData("Jobs", WorkflowPublicDataKind.Queue, E_SignalValueType.Int32, new[] { "2" }));

            var reopened = new WorkflowPublicDataJsonFileStore(path).Load().OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();

            Assert.Equal(2, reopened.Length);
            Assert.Equal(WorkflowPublicDataKind.Queue, reopened[0].Kind);
            Assert.Equal(new[] { "2" }, reopened[0].Values);
            Assert.Equal(new[] { "True" }, reopened[1].Values);
            Assert.Contains("\"Kind\": \"Queue\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Load_RejectsCorruptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "dp-workflow-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.ThrowsAny<Exception>(() => new WorkflowPublicDataJsonFileStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
