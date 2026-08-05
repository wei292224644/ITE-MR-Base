using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Components;
using Uality.IteTour.Data.Assets;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 动画名 → 配音的映射。源实现把它内联在 <c>EMWModelRenderElement.Constructor</c> 的
    /// 双重下标里（<c>clips[i]</c> 配 <c>json.audio[animationAudio[i].audio]</c>），
    /// 两个下标都直接取自服务端数据，越界即整个 Tour 加载失败。
    ///
    /// 期望值取自源实现，越界行为除外——见 design D18。
    /// </summary>
    public class AnimationAudioMapTests
    {
        private static AudioClip Audio(string name) => AudioClip.Create(name, 1, 1, 1000, false);

        private static AnimationClip Anim(string name) => new AnimationClip { name = name };

        private static EMWModelAsset.PublishJson Json(
            AudioClip[] clips, params (int audio, float volume)[] animationAudio)
        {
            var audios = new EMWModelAsset.PublishJson.EMWModelRenderAudio[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                audios[i] = new EMWModelAsset.PublishJson.EMWModelRenderAudio { audioClip = clips[i] };
            }

            var mapping = new EMWModelAsset.PublishJson.EMWModelRenderAnimationAudio[animationAudio.Length];
            for (int i = 0; i < animationAudio.Length; i++)
            {
                mapping[i] = new EMWModelAsset.PublishJson.EMWModelRenderAnimationAudio
                {
                    audio = animationAudio[i].audio,
                    volume = animationAudio[i].volume,
                };
            }

            return new EMWModelAsset.PublishJson { audio = audios, animationAudio = mapping };
        }

        /// <summary>第 i 条动画配 <c>animationAudio[i]</c> 指向的音频，键是动画名。</summary>
        [Test]
        public void Build_KeysByAnimationNameAndResolvesTheAudioIndex()
        {
            var walk = Audio("walk-sfx");
            var idle = Audio("idle-sfx");
            var json = Json(new[] { idle, walk }, (audio: 1, volume: 0.5f), (audio: 0, volume: 1f));
            var clips = new List<AnimationClip> { Anim("Walk"), Anim("Idle") };

            var map = AnimationAudioMap.Build(json, clips);

            Assert.That(map["Walk"].Clip, Is.SameAs(walk));
            Assert.That(map["Walk"].Volume, Is.EqualTo(0.5f));
            Assert.That(map["Idle"].Clip, Is.SameAs(idle));
            Assert.That(map["Idle"].Volume, Is.EqualTo(1f));
        }

        /// <summary>
        /// D18：配音条数多于动画条数时跳过多余的，而不是像源实现那样
        /// <c>IndexOutOfRangeException</c> 打死整个 Tour。
        /// </summary>
        [Test]
        public void Build_SkipsMappingsWithNoMatchingAnimationClip()
        {
            var json = Json(new[] { Audio("a") }, (audio: 0, volume: 1f), (audio: 0, volume: 1f));
            var clips = new List<AnimationClip> { Anim("Only") };

            var map = AnimationAudioMap.Build(json, clips);

            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(map.ContainsKey("Only"), Is.True);
        }

        /// <summary>D18：音频下标越界时跳过该条，其余照常建立。</summary>
        [Test]
        public void Build_SkipsMappingsWhoseAudioIndexIsOutOfRange()
        {
            var good = Audio("good");
            var json = Json(new[] { good }, (audio: 7, volume: 1f), (audio: 0, volume: 0.3f));
            var clips = new List<AnimationClip> { Anim("Bad"), Anim("Good") };

            var map = AnimationAudioMap.Build(json, clips);

            Assert.That(map.ContainsKey("Bad"), Is.False);
            Assert.That(map["Good"].Clip, Is.SameAs(good));
        }

        /// <summary>D18：没有配音配置是正常情况，返回空映射而不是 NRE。</summary>
        [Test]
        public void Build_ReturnsEmptyWhenThereIsNoAudioConfiguration()
        {
            var json = new EMWModelAsset.PublishJson();

            var map = AnimationAudioMap.Build(json, new List<AnimationClip> { Anim("Walk") });

            Assert.That(map, Is.Empty);
        }
    }
}
