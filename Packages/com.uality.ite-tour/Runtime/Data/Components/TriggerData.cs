namespace Uality.IteTour.Components
{
    [System.Serializable]
    public class LoadTrigger : Component
    {
        public ComponentAction[] Actions;
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class TapTrigger : Component
    {
        public ComponentAction[] Actions;
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class ApproximateTrigger : Component
    {
        // 源工程此处用的是 System.Numerics.Vector3（文件顶部 using System.Numerics），
        // 不是 UnityEngine.Vector3。原样保留：这两个字段没有任何消费方——
        // ApproximateTriggerUnityComponent.Constructor 只读 Actions，
        // 近距离判定从未实现。已记入 TODO。
        public System.Numerics.Vector3 Position;
        public float Radius;

        public ComponentAction[] Actions;
        public string ComponentType { get; set; }
    }

    public partial class ComponentType
    {
        public const string LoadTrigger = "LoadTrigger";
        public const string TapTrigger = "TapTrigger";
        public const string ApproximateTrigger = "ApproximateTrigger";
    }
}
