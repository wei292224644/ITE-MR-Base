using NUnit.Framework;
using UnityEngine;

public class PlatformOffsetConfigTests
{
    [Test]
    public void DefaultOffsets_AreIdentity()
    {
        var config = ScriptableObject.CreateInstance<PlatformOffsetConfig>();

        Assert.AreEqual(Pose.identity.position, config.questMarkerToTargetOffset.position);
        Assert.AreEqual(Pose.identity.rotation, config.questMarkerToTargetOffset.rotation);
        Assert.AreEqual(Pose.identity.position, config.picoMarkerToTargetOffset.position);
        Assert.AreEqual(Pose.identity.rotation, config.picoMarkerToTargetOffset.rotation);
    }
}
