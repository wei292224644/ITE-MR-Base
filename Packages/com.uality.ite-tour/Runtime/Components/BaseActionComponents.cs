using System.Threading.Tasks;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 动作组件：由触发器经 <see cref="Internal.EventEmitter"/> 唤起，作用在同一实体的元素上。
    /// </summary>
    /// <typeparam name="T">该动作要操作的元素类型。</typeparam>
    /// <seealso cref="BaseTriggerComponent.Dispatch"/>
    public abstract class BaseActionComponent<T> : BaseComponent where T : BaseElementComponent
    {
        protected T element { get; private set; }

        public override Task Constructor(object data)
        {
            element = GetComponent<T>();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 不关心具体元素类型的动作。
    ///
    /// 源工程把它写成与泛型版逐行重复的第二个类；这里改为泛型版的实例化，
    /// 两者行为一致而只有一份实现。
    /// </summary>
    public abstract class BaseActionComponent : BaseActionComponent<BaseElementComponent>
    {
    }
}
