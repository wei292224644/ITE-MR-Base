using System.IO;
using UnityEngine;

public class DemoMarkerTrigger : MonoBehaviour
{
    [SerializeField] private MarkerAnchorService anchorService;

    private MockMarkerProvider mockProvider;

    private async void Start()
    {
        string demoJson = @"{ ""Anchors"": [ { ""AnchorId"": ""demo_anchor"", ""QuestPayload"": ""DEMO_QR"", ""PicoMarkerId"": 0 } ] }";
        string path = Path.Combine(Application.persistentDataPath, "demo_anchor_registry.json");
        File.WriteAllText(path, demoJson);

        mockProvider = new MockMarkerProvider();
        var dataSource = new LocalJsonAnchorDataSource("demo_anchor_registry.json");
        await anchorService.Initialize(mockProvider, dataSource);
    }

    public void TriggerDemoMarker()
    {
        var pose = new Pose(new Vector3(0, 1.5f, 2), Quaternion.identity);
        // Default MarkerStabilizer needs 30 stable frames; feed once per "frame" in one click.
        for (int i = 0; i < 30; i++)
        {
            mockProvider.SimulateMarkerResolved("DEMO_QR", pose);
        }
    }
}
