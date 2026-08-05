using System.Collections.Generic;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 一个实体上的组件按什么次序实例化。
    ///
    /// 次序不是美观问题：<c>TapTrigger.Constructor</c> 要
    /// <c>GetComponent&lt;BaseElementComponent&gt;()</c>，动作组件要
    /// <c>GetComponent&lt;T&gt;()</c> 拿元素——**元素必须先在**，否则触发器直接报错退出。
    /// </summary>
    public static class ComponentLoadOrder
    {
        /// <summary>
        /// 实例化次序：元素 → 触发器 → 动作。组内次序照抄源实现，
        /// 组内本无依赖，但照抄可以让「换了次序会不会出问题」这个问题不必回答。
        /// </summary>
        private static readonly string[] Order =
        {
            ComponentType.EMWModelRender,
            ComponentType.PrimitiveModelRender,
            ComponentType.RichText,
            ComponentType.VideoPlane,

            ComponentType.TapTrigger,
            ComponentType.LoadTrigger,
            ComponentType.ApproximateTrigger,

            ComponentType.PlayAnimationAction,
            ComponentType.PlayAudioAction,
            ComponentType.SpinAction,
            ComponentType.ToggleVisibilityAction,
        };

        /// <summary>
        /// 未登记的类型跳过；同一类型出现多次只取第一个（源实现用 <c>Find</c>，
        /// 多出来的静默丢弃）。
        /// </summary>
        public static List<Component> Sort(IReadOnlyList<Component> components)
        {
            var ordered = new List<Component>();
            if (components == null)
            {
                return ordered;
            }

            foreach (var key in Order)
            {
                var match = FindFirst(components, key);
                if (match != null)
                {
                    ordered.Add(match);
                }
            }

            return ordered;
        }

        private static Component FindFirst(IReadOnlyList<Component> components, string componentType)
        {
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null && components[i].ComponentType == componentType)
                {
                    return components[i];
                }
            }

            return null;
        }
    }
}
