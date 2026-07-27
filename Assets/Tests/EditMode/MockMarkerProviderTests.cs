using NUnit.Framework;
using UnityEngine;

public class MockMarkerProviderTests
{
    [Test]
    public void SimulateMarkerResolved_RaisesMarkerResolvedEvent()
    {
        var provider = new MockMarkerProvider();
        string capturedId = null;
        Pose capturedPose = default;
        provider.MarkerResolved += (id, pose) => { capturedId = id; capturedPose = pose; };

        var testPose = new Pose(new Vector3(1, 2, 3), Quaternion.identity);
        provider.SimulateMarkerResolved("QR_A", testPose);

        Assert.AreEqual("QR_A", capturedId);
        Assert.AreEqual(testPose.position, capturedPose.position);
    }

    [Test]
    public void SimulateMarkerLost_RaisesMarkerLostEvent()
    {
        var provider = new MockMarkerProvider();
        string capturedId = null;
        provider.MarkerLost += id => capturedId = id;

        provider.SimulateMarkerLost("QR_A");

        Assert.AreEqual("QR_A", capturedId);
    }
}
