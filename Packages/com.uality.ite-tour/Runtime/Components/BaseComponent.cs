using System.Threading.Tasks;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 挂在实体 GameObject 上的 ITE 组件基类。元素、触发器、动作都由此派生。
    ///
    /// <see cref="Constructor"/> 接收该组件在 Tour 描述里的数据对象，由
    /// <c>IteTourObject.CreateTourScene</c> 在 <c>AddComponent</c> 之后立即 await。
    /// </summary>
    public abstract class BaseComponent : MonoBehaviour
    {
        // Core.Entity 必须写全：Uality.IteTour.Data 下另有一个描述用的 Entity
        protected Core.Entity _entity;

        protected IteTourObject _iteTourObject;

        protected EventEmitter _eventEmitter;

        private void Awake()
        {
            _iteTourObject = transform.GetComponentInParent<IteTourObject>();

            _entity = GetComponent<Core.Entity>();
            _eventEmitter = GetComponent<EventEmitter>();

            AfterAwake();
        }

        protected virtual void AfterAwake()
        {
        }

        /// <param name="data">该组件在 Tour 描述里的数据对象，具体类型由子类断言。</param>
        public abstract Task Constructor(object data);
    }
}
