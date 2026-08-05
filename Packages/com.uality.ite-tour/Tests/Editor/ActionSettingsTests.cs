using NUnit.Framework;
using Uality.IteTour.Components;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 动作参数的合并规则。四个动作的 <c>ProcessData</c> 各写各的，而它们的口径
    /// **并不一致**——这件事在源码里完全看不出来，却决定了「连续触发两次、第二次
    /// 只带部分参数」时的结果（design D22）：
    ///
    /// | 动作 | 缺省字段 | 效果 |
    /// |---|---|---|
    /// | Spin / PlayAudio | `?? 当前值` | 沿用上次，**状态跨次累积** |
    /// | PlayAnimation / ToggleVisibility | `?? 固定默认值` | 每次重置 |
    ///
    /// 期望值取自源工程各自的 <c>ProcessData</c>。
    /// </summary>
    public class ActionSettingsTests
    {
        [Test]
        public void SpinSettings_Defaults_MatchTheInspectorDefaults()
        {
            var s = SpinSettings.Default;

            Assert.That(s.Axis, Is.EqualTo(SpinAxis.Y));
            Assert.That(s.Direction, Is.EqualTo(SpinDirection.Clockwise));
            Assert.That(s.UnitSpinDuration, Is.EqualTo(5f));
            Assert.That(s.Revolutions, Is.EqualTo(1));
            Assert.That(s.Loop, Is.False);
            Assert.That(s.Delay, Is.EqualTo(0f));
        }

        [Test]
        public void SpinSettings_Merge_TakesTheSuppliedFields()
        {
            var merged = SpinSettings.Default.Merge(new Spin
            {
                Axis = "z",
                SpinMotion = "counterclockwise",
                UnitSpinDuration = 2f,
                Revolutions = 3,
                Loop = true,
                Delay = 4,
            });

            Assert.That(merged.Axis, Is.EqualTo(SpinAxis.Z));
            Assert.That(merged.Direction, Is.EqualTo(SpinDirection.CounterClockwise));
            Assert.That(merged.UnitSpinDuration, Is.EqualTo(2f));
            Assert.That(merged.Revolutions, Is.EqualTo(3));
            Assert.That(merged.Loop, Is.True);
            Assert.That(merged.Delay, Is.EqualTo(4f));
        }

        /// <summary>缺省字段沿用**当前值**，不是回到默认值——所以状态会跨次累积。</summary>
        [Test]
        public void SpinSettings_Merge_KeepsCurrentValuesForOmittedFields()
        {
            var first = SpinSettings.Default.Merge(new Spin { Axis = "x", Revolutions = 9, Loop = true });
            var second = first.Merge(new Spin { UnitSpinDuration = 1f });

            Assert.That(second.Axis, Is.EqualTo(SpinAxis.X), "第二次没带 Axis，沿用第一次的 x");
            Assert.That(second.Revolutions, Is.EqualTo(9));
            Assert.That(second.Loop, Is.True);
            Assert.That(second.UnitSpinDuration, Is.EqualTo(1f));
        }

        [TestCase("X")]
        [TestCase("up")]
        [TestCase("")]
        [TestCase(null)]
        public void SpinSettings_Merge_KeepsCurrentAxisWhenTheStringIsNotRecognised(string axis)
        {
            // 注意：轴名是**大小写敏感**的，源实现的 switch 只匹配小写字面量
            var merged = SpinSettings.Default.Merge(new Spin { Axis = "z" }).Merge(new Spin { Axis = axis });

            Assert.That(merged.Axis, Is.EqualTo(SpinAxis.Z));
        }

        // ---- PlayAudio：同样是累积口径 ----

        [Test]
        public void PlayAudioSettings_Merge_KeepsCurrentValuesForOmittedFields()
        {
            var first = PlayAudioSettings.Default.Merge(new PlayAudio { Loop = true, Repetition = 5 });
            var second = first.Merge(new PlayAudio { Delay = 2 });

            Assert.That(second.Loop, Is.True, "第二次没带 Loop，沿用第一次");
            Assert.That(second.Repetition, Is.EqualTo(5));
            Assert.That(second.Delay, Is.EqualTo(2));
            Assert.That(second.AutoPlay, Is.False);
        }

        // ---- PlayAnimation：重置口径，与上面两个相反 ----

        /// <summary>
        /// 缺省字段回到**固定默认值**而不是沿用上次——与 Spin / PlayAudio 相反。
        /// </summary>
        [Test]
        public void PlayAnimationSettings_From_ResetsOmittedFieldsToDefaults()
        {
            var first = PlayAnimationSettings.From(
                new PlayAnimation { Loop = true, Speed = 3f, Repetition = 7 }, clipLength: 10f);
            var second = PlayAnimationSettings.From(new PlayAnimation { Delay = 1 }, clipLength: 10f);

            Assert.That(first.Loop, Is.True);
            Assert.That(second.Loop, Is.False, "第二次没带 Loop，回到默认 false 而不是沿用 true");
            Assert.That(second.Speed, Is.EqualTo(1f));
            Assert.That(second.Repetition, Is.EqualTo(0));
        }

        /// <summary><c>-1</c> 是「不指定」的哨兵值，与 null 同义。</summary>
        [TestCase(null)]
        [TestCase(-1f)]
        public void PlayAnimationSettings_From_TreatsMinusOneAsUnspecified(float? endAt)
        {
            var s = PlayAnimationSettings.From(new PlayAnimation { StartAt = endAt, EndAt = endAt }, clipLength: 8f);

            Assert.That(s.StartTime, Is.EqualTo(0f));
            Assert.That(s.EndTime, Is.EqualTo(8f), "未指定的结束时间取整段动画的长度");
        }

        // ---- ToggleVisibility：重置口径 ----

        [TestCase("Always", ToggleCount.Always)]
        [TestCase("Once", ToggleCount.Once)]
        [TestCase("once", ToggleCount.Always)]
        [TestCase(null, ToggleCount.Always)]
        public void ToggleVisibilitySettings_From_FallsBackToAlways(string toggleCount, ToggleCount expected)
        {
            var s = ToggleVisibilitySettings.From(new ToggleVisibility { ToggleCount = toggleCount });

            Assert.That(s.ToggleCount, Is.EqualTo(expected));
        }

        [Test]
        public void ToggleVisibilitySettings_From_ResetsDurationWhenOmitted()
        {
            var s = ToggleVisibilitySettings.From(new ToggleVisibility());

            Assert.That(s.Duration, Is.EqualTo(0f));
        }
    }
}
