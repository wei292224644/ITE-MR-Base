using NUnit.Framework;

public class FiducialConfidencePolicyTests
{
    private const float Threshold = 20f;

    /// <summary>真机实测的假标记：tag 64,margin 3.8,位姿解在相机前 22 cm。</summary>
    [Test]
    public void ObservedFalsePositive_IsRejected()
    {
        Assert.IsFalse(FiducialConfidencePolicy.ShouldAccept(3.8f, Threshold));
    }

    /// <summary>真检测实测分布 76.5-99.6(n=444),下限取 20 不该误伤任何一个。</summary>
    [Test]
    public void ObservedTruePositiveRange_IsAccepted()
    {
        Assert.IsTrue(FiducialConfidencePolicy.ShouldAccept(76.5f, Threshold));
        Assert.IsTrue(FiducialConfidencePolicy.ShouldAccept(91.1f, Threshold));
        Assert.IsTrue(FiducialConfidencePolicy.ShouldAccept(99.6f, Threshold));
    }

    [Test]
    public void ExactlyAtThreshold_IsAccepted()
    {
        Assert.IsTrue(FiducialConfidencePolicy.ShouldAccept(Threshold, Threshold),
            "阈值表达的是「不低于」,取到下限算通过");
    }

    [Test]
    public void JustBelowThreshold_IsRejected()
    {
        Assert.IsFalse(FiducialConfidencePolicy.ShouldAccept(Threshold - 0.01f, Threshold));
    }

    /// <summary>阈值设 0 等于不过滤——留给「先量后定」的现场。</summary>
    [Test]
    public void ZeroThreshold_AcceptsEverything()
    {
        Assert.IsTrue(FiducialConfidencePolicy.ShouldAccept(0f, 0f));
        Assert.IsTrue(FiducialConfidencePolicy.ShouldAccept(3.8f, 0f));
    }
}
