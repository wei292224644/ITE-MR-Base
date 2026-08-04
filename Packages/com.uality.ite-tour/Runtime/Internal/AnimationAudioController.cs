using System.Collections.Generic;
using UnityEngine;

namespace Uality.IteTour.Internal
{
    public class AudioData
    {
        public AudioClip Clip;
        public float Volume;
    }

    /// <summary>
    /// EMW 模型的每条动画可以绑一段音频，由 publish.json 的 animationAudio 表决定。
    /// 播放由 <see cref="LegacyAnimationController"/> 的事件驱动。
    /// </summary>
    [RequireComponent(typeof(Animation), typeof(LegacyAnimationController))]
    public class AnimationAudioController : MonoBehaviour
    {
        // 运行时由 EMWModelRenderElement 填表；Unity 无法序列化 Dictionary，
        // 所以这里不是 Inspector 可配项。
        public Dictionary<string, AudioData> AnimationAudioClips = new Dictionary<string, AudioData>();

        private AudioSource audioSource;

        private LegacyAnimationController _animationController;

        void Awake()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;

            _animationController = GetComponent<LegacyAnimationController>();
        }

        void Start()
        {
            _animationController.OnPlay += OnAnimationPlay;
            _animationController.OnStop += OnAnimationStop;
        }

        private void OnAnimationPlay(string animName)
        {
            if (AnimationAudioClips.TryGetValue(animName, out var audioData))
            {
                audioSource.clip = audioData.Clip;
                audioSource.volume = audioData.Volume;
                audioSource.Play();
            }
        }

        private void OnAnimationStop(string animName)
        {
            audioSource.Stop();
        }
    }
}
