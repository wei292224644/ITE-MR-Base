using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using Uality.IteTour.Config;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 相机三段解析（design D2）：序列化覆盖 → 运行时解析 MRContext → 报错停用。
    ///
    /// 中间那段是真机上唯一走得通的路——内容场景加性加载在 MRCore 之上，
    /// Unity 不支持跨场景序列化引用，XR 相机在设备场景里根本指不上。
    /// </summary>
    public class IteHostCameraResolutionTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            MRContext.BindInstanceForTesting(null);
            foreach (var o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private T Spawn<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private IteHostBootstrap BuildHost(Transform cameraOverride)
        {
            var prefab = new GameObject("tour-prefab");
            _spawned.Add(prefab);

            var config = ScriptableObject.CreateInstance<IteRuntimeConfig>();
            _spawned.Add(config);
            var configSo = new UnityEditor.SerializedObject(config);
            configSo.FindProperty("tourObjectPrefab").objectReferenceValue = prefab;
            configSo.ApplyModifiedPropertiesWithoutUndo();

            var anchor = new GameObject("AnchorRoot");
            _spawned.Add(anchor);
            var tourRoot = new GameObject("TourRoot");
            tourRoot.transform.SetParent(anchor.transform);

            var hostGo = new GameObject("ITE Host");
            _spawned.Add(hostGo);
            var host = hostGo.AddComponent<IteHostBootstrap>();

            var so = new UnityEditor.SerializedObject(host);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("anchorRoot").objectReferenceValue = anchor.transform;
            so.FindProperty("tourRoot").objectReferenceValue = tourRoot.transform;
            so.FindProperty("xrCamera").objectReferenceValue = cameraOverride;
            so.FindProperty("startOnAwake").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            return host;
        }

        /// <summary>把一台相机挂到 MRContext 上，模拟 MRCore 已就位。</summary>
        private Camera BindContextCamera()
        {
            var origin = Spawn<XROrigin>("XR Origin");
            var camera = Spawn<Camera>("XR Camera");
            origin.Camera = camera;

            var context = Spawn<MRContext>("MRContext");
            var so = new UnityEditor.SerializedObject(context);
            so.FindProperty("origin").objectReferenceValue = origin;
            so.ApplyModifiedPropertiesWithoutUndo();
            MRContext.BindInstanceForTesting(context);

            return camera;
        }

        [Test]
        public void SerializedOverride_WinsWithoutResolving()
        {
            var desktop = Spawn<Camera>("Desktop Camera");
            var contextCamera = BindContextCamera();
            var host = BuildHost(desktop.transform);

            LogAssert.Expect(LogType.Log, new Regex("未接入标记源"));
            Assert.IsTrue(host.TryCreateRuntime());
            Assert.AreNotSame(contextCamera.transform, desktop.transform,
                "前提：两台相机不是同一个，否则本用例什么都没验");
        }

        [Test]
        public void NoOverride_ResolvesXrCameraFromContext()
        {
            BindContextCamera();
            var host = BuildHost(null);

            LogAssert.Expect(LogType.Log, new Regex("未接入标记源"));
            Assert.IsTrue(host.TryCreateRuntime(), "MRContext 已就位时应当解析得到相机");
        }

        [Test]
        public void NoOverrideAndNoContext_FailsVisiblyInsteadOfWaiting()
        {
            var host = BuildHost(null);

            LogAssert.Expect(LogType.Error, new Regex("取不到相机"));
            Assert.IsFalse(host.TryCreateRuntime(), "拿不到相机就不该进入 IteRuntime.Create");
        }
    }
}
