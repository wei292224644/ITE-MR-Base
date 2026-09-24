using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// alwaysDisplayed 可见 ⇔ 内容树已建好（ite-current-tour D13）。内容组件把 OnDisable 当拆除用
    /// （VideoPlaneElement 会销毁 VideoPlayer），所以隐藏只能拆树、重新显示只能重建。
    ///
    /// 走真实的 IteTourAssembler / IteTourObject：Tour 预制体在内存里搭（体积 + 内容根），内容是一个
    /// 空场景，装配、建树、拆树都同步完成，不需要 async 测试。
    /// </summary>
    public class AlwaysDisplayedLifecycleTests
    {
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;

        private GameObject _prefab;
        private GameObject _anchorRoot;
        private GameObject _camera;
        private IteTourAssembler _assembler;
        private readonly Dictionary<string, int> _loaded = new Dictionary<string, int>();

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("tour-prefab");

            var volume = new GameObject("Volume");
            volume.transform.SetParent(_prefab.transform);
            volume.AddComponent<BoxCollider>();

            var group = new GameObject("Group");
            group.transform.SetParent(_prefab.transform);

            var serialized = new SerializedObject(_prefab.AddComponent<IteTourObject>());
            serialized.FindProperty("_volumeObject").objectReferenceValue = volume;
            serialized.FindProperty("_mainGroupObject").objectReferenceValue = group;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            _anchorRoot = new GameObject("AnchorRoot");
            var tourRoot = new GameObject("TourRoot");
            tourRoot.transform.SetParent(_anchorRoot.transform);
            _camera = new GameObject("Camera");

            _assembler = new IteTourAssembler(_prefab, tourRoot.transform, _anchorRoot.transform, _camera.transform);

            // TourId 在 CreateTourObject 里才写入；回调触发时已经有值
            _loaded.Clear();
            _assembler.TourCreated += tour =>
                tour.OnTourSceneLoaded += () => _loaded[tour.TourId] = LoadedCount(tour.TourId) + 1;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_anchorRoot);
            Object.DestroyImmediate(_camera);
            Object.DestroyImmediate(_prefab);
        }

        [Test]
        public void Assembled_AlwaysDisplayed_IsNotBuilt()
        {
            var tour = Create("a1", Always);

            Assert.That(tour.IsSceneReady, Is.False, "锚定前看不见，就不该建树——首屏效果会在看不见时放完");
            Assert.That(LoadedCount("a1"), Is.EqualTo(0));
        }

        [Test]
        public void Shown_Builds_AndRaisesLoadedOnce()
        {
            var tour = Create("a1", Always);
            int before = LoadedCount("a1");

            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(tour.IsSceneReady, Is.True);
            Assert.That(LoadedCount("a1"), Is.EqualTo(before + 1), "首屏效果在看得见时触发");
        }

        /// <summary>每个 Tour 装配完成后都会按当前状态同步一次，已定位时会反复「显示」。</summary>
        [Test]
        public void ShownTwice_BuildsOnce()
        {
            var tour = Create("a1", Always);
            int before = LoadedCount("a1");

            _assembler.SetAlwaysDisplayedVisible(true);
            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(tour.IsSceneReady, Is.True);
            Assert.That(LoadedCount("a1"), Is.EqualTo(before + 1), "首屏效果不能叠放");
        }

        [Test]
        public void Hidden_TearsDown()
        {
            var tour = Create("a1", Always);
            _assembler.SetAlwaysDisplayedVisible(true);

            _assembler.SetAlwaysDisplayedVisible(false);

            Assert.That(tour.IsSceneReady, Is.False,
                "停用不是可恢复的隐藏（VideoPlaneElement.OnDisable 会销毁 VideoPlayer），只能拆树");
        }

        [Test]
        public void ShownAgain_Rebuilds_AndRaisesLoadedAgain()
        {
            var tour = Create("a1", Always);
            _assembler.SetAlwaysDisplayedVisible(true);
            _assembler.SetAlwaysDisplayedVisible(false);
            int before = LoadedCount("a1");

            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(tour.IsSceneReady, Is.True);
            Assert.That(LoadedCount("a1"), Is.EqualTo(before + 1), "摘下再戴上、扫码后首屏效果重放");
        }

        /// <summary>回归守卫（改动前也成立）：别的类型由 TourDirector 按当前 Tour 激活，不受显隐影响。</summary>
        [Test]
        public void OtherDisplayTypes_AreUnaffected()
        {
            var regional = Create("r1", Regional);

            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(regional.IsSceneReady, Is.False);
        }

        private int LoadedCount(string tourId) => _loaded.TryGetValue(tourId, out var count) ? count : 0;

        private IteTourObject Create(string tourId, IteSpaceScene.Tour.DisplayType displayType)
        {
            var tour = new IteSpaceScene.Tour
            {
                tourID = tourId,
                displayType = displayType,
                transform = new[]
                {
                    new[] { 1f, 0f, 0f, 0f },
                    new[] { 0f, 1f, 0f, 0f },
                    new[] { 0f, 0f, 1f, 0f },
                    new[] { 0f, 0f, 0f, 1f },
                },
                triggerVolume = new IteSpaceScene.Tour.TriggerVolume { width = 1f, height = 1f, depth = 1f },
            };

            // 必须有一个场景：ScenesOrder 为空时建树会 LogError（测试框架会判失败）
            var data = new Data.IteTour
            {
                Id = tourId,
                Assets = new Dictionary<string, Data.Assets.Asset>(),
                Scenes = new Dictionary<string, Data.Scene>
                {
                    ["s1"] = new Data.Scene { Entities = new Dictionary<string, Data.Entity>() },
                },
                ScenesOrder = new[] { "s1" },
            };

            var task = _assembler.CreateAsync(tour, data);
            Assert.That(task.IsCompleted, Is.True, "内容为空时装配应同步完成，用例前提不成立");
            return task.Result;
        }
    }
}
