using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Components;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 触发器把动作派发到目标实体。源实现把这段藏在 <c>BaseTriggerComponent.EmitActions()</c>
    /// 的实例方法里，取根节点靠 <c>_iteTourObject.transform</c>，而 <c>Awake</c> 在 EditMode
    /// 不跑——整段无法离机验证。抽成静态纯派发后，路由规则本身可测（design D17）。
    /// </summary>
    public class TriggerActionDispatchTests
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

        private Transform TourRoot()
        {
            var go = new GameObject("Tour");
            _spawned.Add(go);
            var group = new GameObject("Group");
            group.transform.SetParent(go.transform, false);
            return go.transform;
        }

        /// <summary>在 <c>&lt;tourRoot&gt;/Group/&lt;entityId&gt;</c> 建一个实体，返回其事件通道。</summary>
        private EventEmitter Entity(Transform tourRoot, string entityId)
        {
            var entity = new GameObject(entityId);
            entity.transform.SetParent(tourRoot.Find("Group"), false);
            return entity.AddComponent<EventEmitter>();
        }

        [Test]
        public void Dispatch_InvokesTargetEntityWithActionNameAndPayload()
        {
            var tourRoot = TourRoot();
            var emitter = Entity(tourRoot, "e1");

            object received = null;
            var calls = 0;
            emitter.On("Spin", data => { received = data; calls++; });

            var action = new ComponentAction { EntityId = "e1", Action = "Spin" };
            BaseTriggerComponent.Dispatch(tourRoot, new[] { action });

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(received, Is.SameAs(action), "载荷是动作本身，动作组件从中读参数");
        }
    }
}
