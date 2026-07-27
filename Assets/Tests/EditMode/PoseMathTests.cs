using NUnit.Framework;
using UnityEngine;

public class PoseMathTests
{
    [Test]
    public void Compose_WithIdentityOffset_ReturnsParentPose()
    {
        var parent = new Pose(new Vector3(1, 2, 3), Quaternion.Euler(0, 45, 0));

        var result = PoseMath.Compose(parent, Pose.identity);

        Assert.AreEqual(parent.position, result.position);
        Assert.AreEqual(parent.rotation, result.rotation);
    }

    [Test]
    public void Compose_WithTranslationOffset_RotatesOffsetIntoParentFrame()
    {
        var parent = new Pose(Vector3.zero, Quaternion.Euler(0, 90, 0));
        var offset = new Pose(new Vector3(1, 0, 0), Quaternion.identity);

        var result = PoseMath.Compose(parent, offset);

        Assert.AreEqual(0f, result.position.x, 0.0001f);
        Assert.AreEqual(0f, result.position.y, 0.0001f);
        Assert.AreEqual(-1f, result.position.z, 0.0001f);
    }
}
