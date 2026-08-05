using UnityEngine;

namespace Uality.IteTour.Components
{
    public enum SpinAxis
    {
        X,
        Y,
        Z,
    }

    public enum SpinDirection
    {
        Clockwise,
        CounterClockwise,
    }

    /// <summary>
    /// 旋转动作的参数。**缺省字段沿用当前值**，因此连续触发时状态跨次累积
    /// （design D22）——源实现的 <c>ProcessData</c> 就是这个口径，不是笔误：
    /// 它同时被 <c>Constructor</c>（拿组件默认值当底）和每次动作调用复用。
    /// </summary>
    public struct SpinSettings
    {
        public SpinAxis Axis;
        public SpinDirection Direction;
        public float UnitSpinDuration;
        public int Revolutions;
        public bool Loop;
        public float Delay;

        /// <summary>与源实现 Inspector 上的初值一致。</summary>
        public static SpinSettings Default => new SpinSettings
        {
            Axis = SpinAxis.Y,
            Direction = SpinDirection.Clockwise,
            UnitSpinDuration = 5f,
            Revolutions = 1,
            Loop = false,
            Delay = 0f,
        };

        public SpinSettings Merge(Spin data)
        {
            if (data == null)
            {
                return this;
            }

            return new SpinSettings
            {
                // 轴名与方向名**大小写敏感**：源实现的 switch 只匹配小写字面量，
                // 认不出来就沿用当前值
                Axis = ParseAxis(data.Axis, Axis),
                Direction = ParseDirection(data.SpinMotion, Direction),
                UnitSpinDuration = data.UnitSpinDuration ?? UnitSpinDuration,
                Revolutions = data.Revolutions ?? Revolutions,
                Loop = data.Loop ?? Loop,
                Delay = data.Delay ?? Delay,
            };
        }

        /// <summary>旋转轴在**父节点**的坐标系里取，与源实现一致。</summary>
        public Vector3 AxisVector(Transform parent)
        {
            if (parent == null)
            {
                return Vector3.up;
            }

            switch (Axis)
            {
                case SpinAxis.X: return parent.right;
                case SpinAxis.Z: return parent.forward;
                default: return parent.up;
            }
        }

        /// <summary>顺时针为负角速度。</summary>
        public float DegreesPerSecond
            => 360f / UnitSpinDuration * (Direction == SpinDirection.Clockwise ? -1f : 1f);

        private static SpinAxis ParseAxis(string value, SpinAxis current)
        {
            switch (value)
            {
                case "x": return SpinAxis.X;
                case "y": return SpinAxis.Y;
                case "z": return SpinAxis.Z;
                default: return current;
            }
        }

        private static SpinDirection ParseDirection(string value, SpinDirection current)
        {
            switch (value)
            {
                case "clockwise": return SpinDirection.Clockwise;
                case "counterclockwise": return SpinDirection.CounterClockwise;
                default: return current;
            }
        }
    }

    /// <summary>
    /// 播放音频动作的参数。与 <see cref="SpinSettings"/> 同为**累积**口径（D22）。
    /// </summary>
    public struct PlayAudioSettings
    {
        public bool AutoPlay;
        public int Repetition;
        public bool Loop;
        public int Delay;

        public static PlayAudioSettings Default => new PlayAudioSettings
        {
            AutoPlay = false,
            Repetition = 1,
            Loop = false,
            Delay = 0,
        };

        public PlayAudioSettings Merge(PlayAudio data)
        {
            if (data == null)
            {
                return this;
            }

            return new PlayAudioSettings
            {
                AutoPlay = data.AutoPlay ?? AutoPlay,
                Repetition = data.Repetition ?? Repetition,
                Loop = data.Loop ?? Loop,
                Delay = data.Delay ?? Delay,
            };
        }
    }

    /// <summary>
    /// 播放动画动作的参数。**重置**口径：缺省字段回到固定默认值，不沿用上次（D22）。
    /// </summary>
    public struct PlayAnimationSettings
    {
        public float StartTime;
        public float EndTime;
        public float Speed;
        public float Delay;
        public int Repetition;
        public bool Loop;
        public bool AutoReverse;

        /// <param name="clipLength">未指定结束时间时取整段动画的长度。</param>
        public static PlayAnimationSettings From(PlayAnimation data, float clipLength)
        {
            if (data == null)
            {
                return new PlayAnimationSettings { EndTime = clipLength, Speed = 1f };
            }

            return new PlayAnimationSettings
            {
                Loop = data.Loop ?? false,
                AutoReverse = data.AutoReverse ?? false,

                // -1 是「不指定」的哨兵值，与 null 同义
                StartTime = Specified(data.StartAt) ? data.StartAt.Value : 0f,
                EndTime = Specified(data.EndAt) ? data.EndAt.Value : clipLength,

                Speed = data.Speed ?? 1f,
                Delay = data.Delay ?? 0f,
                Repetition = data.Repetition ?? 0,
            };
        }

        private static bool Specified(float? value) => value.HasValue && value.Value != -1f;
    }

    public enum ToggleCount
    {
        /// <summary>每次触发都切换。</summary>
        Always,

        /// <summary>只在第一次触发时切换。</summary>
        Once,
    }

    /// <summary>
    /// 显隐切换动作的参数。**重置**口径（D22）。
    /// </summary>
    public struct ToggleVisibilitySettings
    {
        public float Duration;
        public ToggleCount ToggleCount;

        public static ToggleVisibilitySettings From(ToggleVisibility data)
        {
            if (data == null)
            {
                return default;
            }

            return new ToggleVisibilitySettings
            {
                Duration = data.Duration ?? 0f,

                // 大小写敏感，认不出来一律按 Always——与源实现一致
                ToggleCount = data.ToggleCount == "Once" ? ToggleCount.Once : ToggleCount.Always,
            };
        }
    }
}
