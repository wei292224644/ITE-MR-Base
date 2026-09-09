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

        /// <summary>
        /// 装配一个最小可用的运行时。Tour 预制体是个空 GameObject——
        /// 这些用例都走不到实例化，只验证公开面本身不炸。
        /// </summary>
        private class RuntimeFixture
        {
            private readonly IteRuntimeConfig _config;
            private readonly GameObject _host;
            private readonly GameObject _prefab;

            internal readonly IteRuntime Runtime;

            internal RuntimeFixture()
            {
                _prefab = new GameObject("tour-prefab");
                _config = ScriptableObject.CreateInstance<IteRuntimeConfig>();

                var serialized = new UnityEditor.SerializedObject(_config);
                serialized.FindProperty("tourObjectPrefab").objectReferenceValue = _prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                _host = new GameObject("host");

                Runtime = IteRuntime.Create(new IteBootstrap
                {
                    Config = _config,
                    AnchorRoot = _host.transform,
                    TourRoot = _host.transform,
                    Camera = _host.transform,
                });

                Assert.IsNotNull(Runtime, "装配失败，用例前提不成立");
            }

            internal void Dispose()
            {
                Runtime?.Shutdown();
                Object.DestroyImmediate(_host);
                Object.DestroyImmediate(_prefab);
                Object.DestroyImmediate(_config);
            }
        }
    }
}
