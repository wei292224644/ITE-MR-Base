using NUnit.Framework;
using UnityEngine;
using MRBase.Ite.Host;

namespace MRBase.Ite.Host.Tests
{
    public class EditorFakeScanTests
    {
        [Test]
        public void PoseInFront_IsOneAndHalfMetersAlongCameraForward()
        {
            var cameraPos = new Vector3(1f, 2f, 3f);
            var cameraRot = Quaternion.LookRotation(Vector3.forward);

            var pose = EditorFakeScan.PoseInFront(cameraPos, cameraRot, 1.5f);

            Assert.AreEqual(cameraPos + Vector3.forward * 1.5f, pose.position);
        }

        [Test]
        public void PoseInFront_FacesTheCamera()
        {
            var cameraPos = Vector3.zero;
            var cameraRot = Quaternion.LookRotation(Vector3.forward);
            var pose = EditorFakeScan.PoseInFront(cameraPos, cameraRot, 1.5f);
            var toCamera = (cameraPos - pose.position).normalized;
            Assert.Greater(Vector3.Dot(pose.rotation * Vector3.forward, toCamera), 0.99f);
        }

        [Test]
        public void WrapPayload_UsesSixAsterisks()
        {
            Assert.AreEqual("******wm0l5qcn_ibd******", EditorFakeScan.WrapPayload("wm0l5qcn_ibd"));
        }

        [Test]
        public void Driver_Trigger_DispatchesObservedThenLostOnce()
        {
            var go = new GameObject("driver");
            try
            {
                var driver = go.AddComponent<IteEditorFakeScan>();
                int observed = 0;
                int lost = 0;
                string lastPayload = null;
                driver.Session.MarkerObserved += obs =>
                {
                    observed++;
                    lastPayload = obs.RawPayload;
                };
                driver.Session.MarkerLost += (_, __) => lost++;

                driver.Trigger("wm0l5qcn_ibd");
                driver.Tick(0.05f);

                Assert.AreEqual(1, observed);
                Assert.AreEqual("******wm0l5qcn_ibd******", lastPayload);
                Assert.AreEqual(0, lost);

                driver.Tick(0.4f);
                Assert.AreEqual(0, lost, "投喂尚未超过滞回");

                driver.Tick(1.1f);
                Assert.AreEqual(1, lost);
                driver.Tick(1f);
                Assert.AreEqual(1, lost, "持续缺席只派发一次 Lost");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
