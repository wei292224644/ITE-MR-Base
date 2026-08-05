using System.Threading.Tasks;
using UnityEngine;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// Tour 内容构建完成时派发动作，用来做首屏动效。
    /// </summary>
    public class LoadTriggerUnityComponent : BaseTriggerComponent
    {
        public override Task Constructor(object data)
        {
            _actions = ((LoadTrigger)data).Actions;
            _iteTourObject.OnTourSceneLoaded += EmitActions;

            return Task.CompletedTask;
        }

        private void OnDestroy()
        {
            if (_iteTourObject != null)
            {
                _iteTourObject.OnTourSceneLoaded -= EmitActions;
            }
        }
    }

    /// <summary>
    /// 同一实体上的元素被点击时派发动作。
    /// </summary>
    public class TapTriggerUnityComponent : BaseTriggerComponent
    {
        public override Task Constructor(object data)
        {
            var element = GetComponent<BaseElementComponent>();
            if (element == null)
            {
                Debug.LogError("[ITE] TapTrigger 需要同一 GameObject 上有元素组件");
                return Task.CompletedTask;
            }

            element.OnElementTapped += EmitActions;
            _actions = ((TapTrigger)data).Actions;

            return Task.CompletedTask;
        }

        private void OnDestroy()
        {
            var element = GetComponent<BaseElementComponent>();
            if (element != null)
            {
                element.OnElementTapped -= EmitActions;
            }
        }
    }

    /// <summary>
    /// 相机靠近到 <c>Radius</c> 以内时派发动作。
    ///
    /// **近距离判定从未实现**：源实现的 <c>Constructor</c> 只读 <c>Actions</c>，
    /// 没有任何地方比较距离，因此这个触发器永远不会触发。数据类上的
    /// <c>Position</c> / <c>Radius</c> 也无人消费（<c>Position</c> 甚至用的是
    /// <c>System.Numerics.Vector3</c>）。原样保留，已记入 TODO。
    /// </summary>
    public class ApproximateTriggerUnityComponent : BaseTriggerComponent
    {
        public override Task Constructor(object data)
        {
            _actions = ((ApproximateTrigger)data).Actions;

            return Task.CompletedTask;
        }
    }
}
