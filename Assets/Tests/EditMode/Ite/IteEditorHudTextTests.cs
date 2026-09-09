using System;
using NUnit.Framework;
using Uality.IteTour.Core;
using MRBase.Ite.Host;

namespace MRBase.Ite.Host.Tests
{
    public class IteEditorHudTextTests
    {
        [Test]
        public void Format_Unscanned_ShowsVisiblePromptWithEmptyTourIds()
        {
            var text = IteEditorHudText.Format(
                loadProgress: 1f,
                spaceSceneName: "thirdDemo",
                assembledTourIds: new[] { "wm0l5qcn_ibd", "4kvhqwvp_12f" },
                activeTourId: null,
                prompt: ScanPrompt.Visible(Array.Empty<string>()),
                pendingVolumeTourIds: Array.Empty<string>(),
                lastObserved: null,
                lastLost: null);

            StringAssert.Contains("ScanPrompt: Visible", text);
            StringAssert.Contains("PromptTourIds: (empty)", text);
            StringAssert.DoesNotContain("Active: wm0l5qcn_ibd", text);
        }

        [Test]
        public void Format_Activated_ShowsHiddenPromptAndActiveId()
        {
            var text = IteEditorHudText.Format(
                loadProgress: 1f,
                spaceSceneName: "thirdDemo",
                assembledTourIds: new[] { "wm0l5qcn_ibd" },
                activeTourId: "wm0l5qcn_ibd",
                prompt: ScanPrompt.Hidden,
                pendingVolumeTourIds: new[] { "wm0l5qcn_ibd" },
                lastObserved: "******wm0l5qcn_ibd******",
                lastLost: null);

            StringAssert.Contains("ScanPrompt: Hidden", text);
            StringAssert.Contains("Active: wm0l5qcn_ibd", text);
            StringAssert.Contains("InVolume: wm0l5qcn_ibd", text);
        }
    }
}
