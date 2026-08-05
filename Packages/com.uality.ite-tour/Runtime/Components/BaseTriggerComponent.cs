using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 触发器组件：某个条件成立时，向 Tour 内的目标实体派发动作。
    /// </summary>
    public abstract class BaseTriggerComponent : BaseComponent
    {
        protected ComponentAction[] _actions;

        protected void EmitActions() => Dispatch(_iteTourObject.transform, _actions);

        /// <summary>
        /// 按 <see cref="ComponentAction.EntityId"/> 找到目标实体，向它的
        /// <see cref="EventEmitter"/> 投递动作。目标不存在就跳过——Tour 描述里
        /// 引用了已删除的实体是正常情况，不该让整个触发器炸掉。
        ///
        /// 做成静态是为了可测：实例路径上的 <c>_iteTourObject</c> 由 <c>Awake</c> 填，
        /// 而 EditMode 不跑 <c>Awake</c>（design D17）。
        /// </summary>
        /// <param name="tourRoot">Tour 根节点，实体挂在它的 <c>Group</c> 子节点下。</param>
        public static void Dispatch(Transform tourRoot, IReadOnlyList<ComponentAction> actions)
        {
            if (tourRoot == null || actions == null)
            {
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action == null || string.IsNullOrEmpty(action.EntityId))
                {
                    continue;
                }

                var target = tourRoot.Find("Group/" + action.EntityId);
                if (target == null)
                {
                    continue;
                }

                var emitter = target.GetComponent<EventEmitter>();
                if (emitter != null)
                {
                    emitter.Invoke(action.Action, action);
                }
            }
        }
    }
}
