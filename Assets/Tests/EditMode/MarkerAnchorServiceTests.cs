using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MarkerAnchorServiceTests
{
    private GameObject hostObject;
    private MarkerAnchorService service;
    private AnchorRegistry registry;
    private PlatformOffsetConfig offsetConfig;
    private MockMarkerProvider provider;
    private FakeContentLoader contentLoader;

    [SetUp]
    public void SetUp()
    {
        var data = new AnchorEntityData { AnchorId = "anchor_a", QuestPayload = "QR_A", PicoMarkerId = 3 };

        registry = new AnchorRegistry();
        registry.SetEntitiesForTesting(new List<AnchorEntityData> { data });

        offsetConfig = ScriptableObject.CreateInstance<PlatformOffsetConfig>();
        contentLoader = new FakeContentLoader { SpriteToReturn = null };

        hostObject = new GameObject("MarkerAnchorServiceHost");
        service = hostObject.AddComponent<MarkerAnchorService>();
        service.SetDependenciesForTesting(registry, offsetConfig, contentLoader, new MarkerStabilizer(stableFrameThreshold: 1));

        provider = new MockMarkerProvider();
        service.BindProviderForTesting(provider);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var t in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.gameObject.name.StartsWith("AnchorEntity_"))
            {
                Object.DestroyImmediate(t.gameObject);
            }
        }
        Object.DestroyImmediate(hostObject);
    }

    [Test]
    public void MarkerResolved_WithMatchingId_CreatesAnchorEntityAtMarkerPose()
    {
        var pose = new Pose(new Vector3(1, 0, 2), Quaternion.identity);

        provider.SimulateMarkerResolved("QR_A", pose);

        var spawned = GameObject.Find("AnchorEntity_anchor_a");
        Assert.IsNotNull(spawned);
        Assert.AreEqual(pose.position, spawned.transform.position);
    }

    [Test]
    public void MarkerResolved_WithUnknownId_DoesNotCreateEntityAndLogsWarning()
    {
        LogAssert.Expect(LogType.Warning, "[MarkerAnchorService] 识别到未匹配的标记ID: UNKNOWN");

        provider.SimulateMarkerResolved("UNKNOWN", Pose.identity);

        Assert.IsNull(GameObject.Find("AnchorEntity_anchor_a"));
    }

    [Test]
    public void MarkerResolved_SameIdTwice_OnlyCreatesOnce()
    {
        provider.SimulateMarkerResolved("QR_A", Pose.identity);
        provider.SimulateMarkerResolved("QR_A", new Pose(Vector3.one, Quaternion.identity));

        int count = 0;
        foreach (var t in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.gameObject.name == "AnchorEntity_anchor_a") count++;
        }

        Assert.AreEqual(1, count);
    }
}
