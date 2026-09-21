using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Uality.IteTour.Config;
using MRBase.Ite.Host;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 会话只能从外部注入：装配点不构造会话、不持有观测源字段（design D3）。
    /// </summary>
    public class IteHostBootstrapTests
    {
        [Test]
        public void AttachMarkerSession_BeforeCreate_StashesAndBindsOnTryCreateRuntime()
        {
            var fixture = new HostFixture();
            try
            {
                var source = new MockObservationSource();
                var session = new MarkerTrackingSession(source);

                fixture.Host.AttachMarkerSession(session);

                Assert.IsTrue(fixture.Host.TryCreateRuntime());
                Assert.IsNotNull(fixture.Host.Runtime);
                Assert.IsNotNull(fixture.Host.MarkerBridge);

                source.SetNextPoll(new[]
                {
                    new MarkerObservation(
                        MarkerPlatform.Quest,
                        "******wm0l5qcn_ibd******",
                        Pose.identity)
                });
                Assert.DoesNotThrow(() => session.Tick(0.1f));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryCreateRuntime_WithoutSession_LogsAndStillCreates()
        {
            var fixture = new HostFixture();
            try
            {
                LogAssert.Expect(LogType.Log, new Regex("未接入标记源"));

                Assert.IsTrue(fixture.Host.TryCreateRuntime());
                Assert.IsNotNull(fixture.Host.Runtime);
                Assert.IsNull(fixture.Host.MarkerBridge);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AttachMarkerSession_AfterCreate_BindsImmediately()
        {
            var fixture = new HostFixture();
            try
            {
                LogAssert.Expect(LogType.Log, new Regex("未接入标记源"));
                Assert.IsTrue(fixture.Host.TryCreateRuntime());
                Assert.IsNull(fixture.Host.MarkerBridge);

                var session = new MarkerTrackingSession(new MockObservationSource());
                fixture.Host.AttachMarkerSession(session);

                Assert.IsNotNull(fixture.Host.MarkerBridge);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private sealed class HostFixture
        {
            private readonly IteRuntimeConfig _config;
            private readonly GameObject _prefab;
            private readonly GameObject _hostGo;
            private readonly GameObject _anchor;
            private readonly GameObject _camera;

            internal readonly IteHostBootstrap Host;

            internal HostFixture()
            {
                _prefab = new GameObject("tour-prefab");
                _config = ScriptableObject.CreateInstance<IteRuntimeConfig>();
                var configSo = new UnityEditor.SerializedObject(_config);
                configSo.FindProperty("tourObjectPrefab").objectReferenceValue = _prefab;
                configSo.ApplyModifiedPropertiesWithoutUndo();

                _anchor = new GameObject("AnchorRoot");
                var tourRoot = new GameObject("TourRoot");
                tourRoot.transform.SetParent(_anchor.transform);
                _camera = new GameObject("Camera");

                _hostGo = new GameObject("ITE Host");
                Host = _hostGo.AddComponent<IteHostBootstrap>();

                var so = new UnityEditor.SerializedObject(Host);
                so.FindProperty("config").objectReferenceValue = _config;
                so.FindProperty("anchorRoot").objectReferenceValue = _anchor.transform;
                so.FindProperty("tourRoot").objectReferenceValue = tourRoot.transform;
                so.FindProperty("xrCamera").objectReferenceValue = _camera.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            internal void Dispose()
            {
                Object.DestroyImmediate(_hostGo);
                Object.DestroyImmediate(_anchor);
                Object.DestroyImmediate(_camera);
                Object.DestroyImmediate(_prefab);
                Object.DestroyImmediate(_config);
            }
        }
    }
}
