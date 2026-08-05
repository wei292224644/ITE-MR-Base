using System;
using System.Collections.Generic;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 组件类型键 → MonoBehaviour 类型。编译期常量表，无实例、无生命周期。
    ///
    /// 源实现是个 <c>StaticInstance&lt;ComponentsUtils&gt;</c> 单例 MonoBehaviour，靠
    /// <c>executionOrder: 500</c> 保证在别人用它之前 <c>Awake</c> 完成建表——一张
    /// 内容完全固定的表，却被绑在场景对象的生命周期与执行顺序上（design D23）。
    ///
    /// 键取自服务端约定，与类名不一定一致（见 <see cref="ComponentType.PlayAudioAction"/>）。
    /// </summary>
    public static class ComponentRegistry
    {
        // 源实现中 VolumeTrigger / CustomGestureTrigger 两行是注释状态，对应组件也不存在，
        // 因此这里同样不登记——扫到这两个键会走「未知类型」路径。
        private static readonly Dictionary<string, Type> Table = new Dictionary<string, Type>
        {
            { ComponentType.EMWModelRender, typeof(EMWModelRenderUnityComponent) },
            { ComponentType.RichText, typeof(RichTextUnityComponent) },
            { ComponentType.VideoPlane, typeof(VideoPlaneUnityComponent) },
            { ComponentType.PrimitiveModelRender, typeof(PrimitiveModelRenderUnityComponent) },

            { ComponentType.LoadTrigger, typeof(LoadTriggerUnityComponent) },
            { ComponentType.TapTrigger, typeof(TapTriggerUnityComponent) },
            { ComponentType.ApproximateTrigger, typeof(ApproximateTriggerUnityComponent) },

            { ComponentType.PlayAudioAction, typeof(PlayAudioActionUnityComponent) },
            { ComponentType.SpinAction, typeof(SpinActionUnityComponent) },
            { ComponentType.ToggleVisibilityAction, typeof(ToggleVisibilityActionUnityComponent) },
            { ComponentType.PlayAnimationAction, typeof(PlayAnimationActionUnityComponent) },
        };

        /// <summary>未登记的键返回 null，调用方跳过该组件。</summary>
        public static Type Resolve(string componentType)
        {
            if (string.IsNullOrEmpty(componentType))
            {
                return null;
            }

            return Table.TryGetValue(componentType, out var type) ? type : null;
        }
    }
}
