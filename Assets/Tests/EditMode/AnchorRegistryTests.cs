using System.Collections.Generic;
using NUnit.Framework;

public class AnchorRegistryTests
{
    private AnchorRegistry registry;

    [SetUp]
    public void SetUp()
    {
        var data = new AnchorEntityData
        {
            AnchorId = "anchor_a",
            QuestPayload = "QR_A",
            PicoMarkerId = 3
        };

        registry = new AnchorRegistry();
        registry.SetEntitiesForTesting(new List<AnchorEntityData> { data });
    }

    [Test]
    public void TryResolve_MatchesByQuestPayload()
    {
        Assert.IsTrue(registry.TryResolve("QR_A", out var result));
        Assert.AreEqual("anchor_a", result.AnchorId);
    }

    [Test]
    public void TryResolve_MatchesByPicoMarkerId()
    {
        Assert.IsTrue(registry.TryResolve("3", out var result));
        Assert.AreEqual("anchor_a", result.AnchorId);
    }

    [Test]
    public void TryResolve_ReturnsFalse_WhenNoMatch()
    {
        Assert.IsFalse(registry.TryResolve("unknown", out var result));
        Assert.IsNull(result);
    }
}
