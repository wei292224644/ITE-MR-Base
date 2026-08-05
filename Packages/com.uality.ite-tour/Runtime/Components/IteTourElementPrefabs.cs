using UnityEngine;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 元素组件要实例化的预制体。在 <c>IteTourObject</c> 预制体上作者时连线，随包发布。
    ///
    /// 之所以挂在 Tour 对象上而不是别处：元素组件是运行时 <c>AddComponent</c> 上去的，
    /// 拿不到序列化引用，只能从被作者化过的祖先要（design D20）。
    /// </summary>
    [System.Serializable]
    public class IteTourElementPrefabs
    {
        public GameObject EmwModelRender;
        public GameObject RichText;
        public GameObject VideoPlane;
    }
}
