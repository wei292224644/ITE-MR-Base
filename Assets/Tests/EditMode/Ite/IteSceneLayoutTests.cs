using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 锁住两个 ITE 场景的根层组织约定，以及锚定层级的几何前提。
    ///
    /// 为什么值得一条测试：<c>IteBootstrap.Validate()</c> 的同类检查只在**运行时**跑，而根层
    /// 分组头是新加的一层、最容易被手滑拖歪；场景搭坏在真机上的表现是「内容错位、无日志」。
    /// 这里在离机就把它变红。
    /// </summary>
    public class IteSceneLayoutTests
    {
        private const string DeviceScene = "Assets/Scenes/IteTour.unity";
        private const string EditorScene = "Assets/Scenes/IteTourSpace.unity";

        private const string ManagementGroup = "-- Management --";
        private const string HarnessGroup = "-- Harness --";

        [TestCase(DeviceScene)]
        [TestCase(EditorScene)]
        public void RootObjects_AreOnlyTheAgreedGroups(string scenePath)
        {
            WithScene(scenePath, scene =>
            {
                var roots = new List<string>();
                foreach (var go in scene.GetRootGameObjects())
                {
                    roots.Add(go.name);
                }

                Assert.That(roots, Is.EquivalentTo(new[] { ManagementGroup, HarnessGroup }),
                    scenePath + " 的根层只应有这两个分组；散装对象要收进对应分组或 harness 预制体");
            });
        }

        /// <summary>
        /// 分组头与预制体根上的任何变换都会被二次施加到内容上：标记位姿是**世界**位姿，
        /// 却以 <c>SetLocalPositionAndRotation</c> 写进 AnchorRoot（见 IteBootstrap.Validate）。
        /// </summary>
        [TestCase(DeviceScene)]
        [TestCase(EditorScene)]
        public void GroupsAndPrefabRoots_StayAtIdentity(string scenePath)
        {
            WithScene(scenePath, scene =>
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    AssertIdentity(root.transform, scenePath);

                    foreach (Transform child in root.transform)
                    {
                        AssertIdentity(child, scenePath);
                    }
                }
            });
        }

        [TestCase(DeviceScene)]
        [TestCase(EditorScene)]
        public void TourRoot_IsDirectChildOfAnchorRoot(string scenePath)
        {
            WithScene(scenePath, scene =>
            {
                var anchorRoot = Find(scene, "AnchorRoot");
                var tourRoot = Find(scene, "TourRoot");

                Assert.IsNotNull(anchorRoot, scenePath + " 里找不到 AnchorRoot");
                Assert.IsNotNull(tourRoot, scenePath + " 里找不到 TourRoot");
                Assert.AreSame(anchorRoot, tourRoot.parent,
                    "TourRoot 必须是 AnchorRoot 的直接子物体，中间夹任何节点锚定等式即破");
            });
        }

        private static void AssertIdentity(Transform t, string scenePath)
        {
            Assert.AreEqual(Matrix4x4.identity, t.localToWorldMatrix,
                $"{scenePath} 的 \"{t.name}\" 必须在世界原点、无旋转、无缩放；" +
                "分组头或预制体根上的变换会被二次施加到内容上");
        }

        private static Transform Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 附加打开再关掉：不动用户当前的场景设置（测试跑完 Editor 还停在原来那个场景）。
        /// </summary>
        private static void WithScene(string path, System.Action<Scene> body)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                body(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
