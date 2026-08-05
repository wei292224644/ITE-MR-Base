using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 进出触发体积的是不是相机。
    ///
    /// 源实现用 <c>other.CompareTag("ARCamera")</c>——**tag 是工程级全局配置**，
    /// 包一旦移植到别的工程，那边没有这个 tag，判定永远为假、区域触发整体失效，
    /// 而且不报任何错（design D26）。改为与装配时注入的相机 Transform 比对。
    /// </summary>
    public class CameraColliderMatchTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        private Transform New(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            else
            {
                _spawned.Add(go);
            }

            return go.transform;
        }

        [Test]
        public void IsCamera_MatchesTheCameraItself()
        {
            var camera = New("Camera");

            Assert.That(SceneRoles.IsCamera(camera, camera), Is.True);
        }

        /// <summary>碰撞体常挂在相机的子物体上（XR rig 的常见做法）。</summary>
        [Test]
        public void IsCamera_MatchesAColliderOnAChildOfTheCamera()
        {
            var camera = New("Camera");
            var collider = New("HeadCollider", camera);

            Assert.That(SceneRoles.IsCamera(camera, collider), Is.True);
        }

        /// <summary>也可能反过来：碰撞体挂在整个 rig 上，相机是它的子物体。</summary>
        [Test]
        public void IsCamera_MatchesAColliderOnAnAncestorOfTheCamera()
        {
            var rig = New("XRRig");
            var camera = New("Camera", rig);

            Assert.That(SceneRoles.IsCamera(camera, rig), Is.True);
        }

        [Test]
        public void IsCamera_RejectsUnrelatedTransforms()
        {
            var camera = New("Camera");
            var other = New("SomethingElse");

            Assert.That(SceneRoles.IsCamera(camera, other), Is.False);
        }

        /// <summary>没注入相机时一律为假，且不抛异常。</summary>
        [Test]
        public void IsCamera_WithoutACameraIsFalse()
        {
            Assert.That(SceneRoles.IsCamera(null, New("X")), Is.False);
            Assert.That(SceneRoles.IsCamera(New("Camera"), null), Is.False);
        }
    }
}
