using System;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 元素组件：实体的可见内容（模型、富文本、视频、几何体）。
    /// </summary>
    public abstract class BaseElementComponent : BaseComponent
    {
        /// <summary>元素被点击。<c>TapTrigger</c> 订阅它来派发动作。</summary>
        public Action OnElementTapped;
    }
}
