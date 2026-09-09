using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Uality.IteTour.Config;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 对外 API 面的装配与校验。运行时的加载流程需要网络与磁盘，不在这里测。
    /// </summary>
    public class IteRuntimeTests
    {
        [Test]
        public void MissingRequired_EmptyBootstrap_NamesEveryRequiredField()
        {
            var missing = new IteBootstrap().MissingRequired();

            CollectionAssert.AreEquivalent(
                new[] { "Config", "AnchorRoot", "TourRoot", "Camera" },
                missing.ToArray());
        }

        [Test]
        public void Create_MissingRequired_RefusesAndNamesEachMissingItem()
        {
            LogAssert.Expect(LogType.Error, new Regex("Config.*AnchorRoot.*TourRoot.*Camera"));

            Assert.IsNull(IteRuntime.Create(new IteBootstrap()));
        }

        /// <summary>
        /// Tour 预制体是装配的一部分，只是它挂在 Config 上。少了它同样要拒绝启动，
        /// 而不是等 <c>IteTourAssembler</c> 的构造函数抛 <c>ArgumentNullException</c>。
        /// </summary>
        [Test]
        public void Create_ConfigWithoutTourObjectPrefab_RefusesInsteadOfThrowing()
        {
            var config = ScriptableObject.CreateInstance<IteRuntimeConfig>();
            var host = new GameObject("host");

            try
            {
                LogAssert.Expect(LogType.Error, new Regex("Config\\.TourObjectPrefab"));

                Assert.IsNull(IteRuntime.Create(new IteBootstrap
                {
                    Config = config,
                    AnchorRoot = host.transform,
                    TourRoot = host.transform,
                    Camera = host.transform,
                }));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(config);
            }
        }

        /// <summary>
        /// 7.6：宿主一个事件都不订，推入方法与激活入口也不能炸。
        /// 广播全部以 <c>?.Invoke</c> 触发。
        /// </summary>
        [Test]
        public void PushMethods_WithNoSubscribers_DoNotThrow()
        {
            var fixture = new RuntimeFixture();

            try
            {
                LogAssert.Expect(LogType.Error, new Regex("找不到 Tour"));

                Assert.DoesNotThrow(() =>
                {
                    fixture.Runtime.SubmitMarkerScan("marker-1", Pose.identity);
                    fixture.Runtime.SetHeadsetMounted(false);
                    fixture.Runtime.SetHeadsetMounted(true);
                    Assert.IsFalse(fixture.Runtime.ActivateTour("no-such-tour"));
                    fixture.Runtime.SetTriggerVolumesActive(false);
                    fixture.Runtime.SetTriggerVolumesActive(true);
                });
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void Create_TourRootNotDirectChildOfAnchorRoot_RefusesAndExplainsWhy()
        {
            var fixture = new HierarchyFixture();
            try
            {
                fixture.TourRoot.SetParent(null);

                LogAssert.Expect(LogType.Error, new Regex("TourRoot.*AnchorRoot.*直接子"));

                Assert.IsNull(IteRuntime.Create(fixture.Bootstrap()));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void Create_AnchorRootParentHasNonIdentityTransform_RefusesAndExplainsWhy()
        {
            var fixture = new HierarchyFixture();
            GameObject parent = null;
            try
            {
                parent = new GameObject("OffsetParent");
                parent.transform.position = new Vector3(1f, 0f, 0f);
                fixture.AnchorRoot.SetParent(parent.transform);

                LogAssert.Expect(LogType.Error, new Regex("AnchorRoot.*父级.*世界原点"));

                Assert.IsNull(IteRuntime.Create(fixture.Bootstrap()));
            }
            finally
            {
                fixture.Dispose();
                if (parent != null) Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Create_ValidHierarchy_ReturnsRuntime()
        {
            var fixture = new HierarchyFixture();
            try
            {
                var runtime = IteRuntime.Create(fixture.Bootstrap());
                Assert.IsNotNull(runtime);
                Assert.IsNull(runtime.ActiveTourId);
                Assert.AreEqual(0, runtime.AssembledTourIds.Count);
                runtime.Shutdown();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void Create_AnchorRootParentIsIdentity_ReturnsRuntime()
        {
            var fixture = new HierarchyFixture();
            GameObject parent = null;
            try
            {
                parent = new GameObject("WorldOriginParent");
                fixture.AnchorRoot.SetParent(parent.transform);

                var runtime = IteRuntime.Create(fixture.Bootstrap());
                Assert.IsNotNull(runtime);
                runtime.Shutdown();
            }
            finally
            {
                fixture.Dispose();
                if (parent != null) Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// 装配一个最小可用的运行时。Tour 预制体是个空 GameObject——
        /// 这些用例都走不到实例化，只验证公开面本身不炸。
        /// </summary>
        private class RuntimeFixture
        {
            private readonly IteRuntimeConfig _config;
            private readonly GameObject _host;
            private readonly GameObject _camera;
            private readonly GameObject _prefab;

            internal readonly IteRuntime Runtime;

            internal RuntimeFixture()
            {
                _prefab = new GameObject("tour-prefab");
                _config = ScriptableObject.CreateInstance<IteRuntimeConfig>();

                var serialized = new UnityEditor.SerializedObject(_config);
                serialized.FindProperty("tourObjectPrefab").objectReferenceValue = _prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                _host = new GameObject("AnchorRoot");
                var tourRoot = new GameObject("TourRoot");
                tourRoot.transform.SetParent(_host.transform);
                _camera = new GameObject("Camera");

                Runtime = IteRuntime.Create(new IteBootstrap
                {
                    Config = _config,
                    AnchorRoot = _host.transform,
                    TourRoot = tourRoot.transform,
                    Camera = _camera.transform,
                });

                Assert.IsNotNull(Runtime, "装配失败，用例前提不成立");
            }

            internal void Dispose()
            {
                Runtime?.Shutdown();
                Object.DestroyImmediate(_host);
                Object.DestroyImmediate(_camera);
                Object.DestroyImmediate(_prefab);
                Object.DestroyImmediate(_config);
            }
        }

        /// <summary>
        /// 层级合法的最小装配：TourRoot 是 AnchorRoot 的直接子物体，AnchorRoot 为根级。
        /// </summary>
        private class HierarchyFixture
        {
            private readonly IteRuntimeConfig _config;
            private readonly GameObject _prefab;
            internal readonly Transform AnchorRoot;
            internal readonly Transform TourRoot;
            internal readonly Transform Camera;

            internal HierarchyFixture()
            {
                _prefab = new GameObject("tour-prefab");
                _config = ScriptableObject.CreateInstance<IteRuntimeConfig>();
                var serialized = new UnityEditor.SerializedObject(_config);
                serialized.FindProperty("tourObjectPrefab").objectReferenceValue = _prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                AnchorRoot = new GameObject("AnchorRoot").transform;
                TourRoot = new GameObject("TourRoot").transform;
                TourRoot.SetParent(AnchorRoot);
                Camera = new GameObject("Camera").transform;
            }

            internal IteBootstrap Bootstrap() => new IteBootstrap
            {
                Config = _config,
                AnchorRoot = AnchorRoot,
                TourRoot = TourRoot,
                Camera = Camera,
            };

            internal void Dispose()
            {
                if (TourRoot != null) Object.DestroyImmediate(TourRoot.gameObject);
                if (AnchorRoot != null) Object.DestroyImmediate(AnchorRoot.gameObject);
                if (Camera != null) Object.DestroyImmediate(Camera.gameObject);
                Object.DestroyImmediate(_prefab);
                Object.DestroyImmediate(_config);
            }
        }
    }
}
