namespace Uality.IteTour.Components
{
    [System.Serializable]
    public class PlayAnimation
    {
        public int? Repetition;
        public bool? Loop;
        public int? Delay;
        public float? StartAt;
        public float? EndAt;
        public float? Speed;
        public bool? AutoReverse;

        /// <summary>firstFrame | currentFrame</summary>
        public string OnStop;
    }

    [System.Serializable]
    public class PlayAnimationAction : PlayAnimation, Component
    {
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class PlayAnimationActionParameters : ComponentAction
    {
        public PlayAnimation Parameters;
    }

    [System.Serializable]
    public class PlayAudio
    {
        public int? Repetition;
        public bool? Loop;
        public bool? AutoPlay;
        public int? Delay;
    }

    [System.Serializable]
    public class PlayAudioAction : PlayAudio, Component
    {
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class PlayAudioActionParameters : ComponentAction
    {
        public PlayAudio Parameters;
    }

    [System.Serializable]
    public class Spin
    {
        /// <summary>x | y | z，绕哪根轴旋转。</summary>
        public string Axis;

        /// <summary>clockwise | counterclockwise。</summary>
        public string SpinMotion;

        public float? UnitSpinDuration;
        public int? Revolutions;
        public bool? Loop;
        public int? Delay;
    }

    [System.Serializable]
    public class SpinAction : Spin, Component
    {
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class SpinActionParameters : ComponentAction
    {
        public Spin Parameters;
    }

    [System.Serializable]
    public class ToggleVisibility
    {
        public float? Duration;

        /// <summary>Always = 每次触发都切换；Once = 只在第一次触发时切换。</summary>
        public string ToggleCount;
    }

    [System.Serializable]
    public class ToggleVisibilityAction : ToggleVisibility, Component
    {
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class ToggleVisibilityActionParameters : ComponentAction
    {
        public ToggleVisibility Parameters;
    }

    public partial class ComponentType
    {
        // 注意：这个键与类名不一致（多了 "Component" 后缀），是服务端约定的原值，
        // 改了会导致组件解析不出来。原样保留。
        public const string PlayAudioAction = "PlayAudioActionComponent";

        public const string PlayAnimationAction = "PlayAnimationAction";
        public const string SpinAction = "SpinAction";
        public const string ToggleVisibilityAction = "ToggleVisibilityAction";
    }

    public partial class ComponentActionName
    {
        public const string PlayAnimationAction = "PlayAnimation";
        public const string PlayAudioAction = "PlayAudio";
        public const string SpinAction = "Spin";
        public const string ToggleVisibilityAction = "ToggleVisibility";
    }
}
