using System;
using UnityEngine;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// glTF 模型带的是 Legacy <see cref="Animation"/>，不是 Animator。
    /// 这层把播放/停止/暂停包装成事件，供动画音频与 PlayAnimationAction 消费。
    /// </summary>
    [RequireComponent(typeof(Animation))]
    public class LegacyAnimationController : MonoBehaviour
    {
        public event Action<string> OnPlay;
        public event Action<string> OnStop;
        public event Action<string> OnPause;
        public event Action<string> OnResume;
        public event Action<string, float> OnProgress;

        private Animation anim;
        private string currentAnim = null;
        private bool isPaused = false;

        public WrapMode wrapMode
        {
            get => anim.wrapMode;
            set => anim.wrapMode = value;
        }

        public AnimationClip clip
        {
            get => anim.clip;
        }

        public AnimationState this[string name] => anim[name];

        void Awake()
        {
            anim = GetComponent<Animation>();
        }

        public void Play(string animName)
        {
            if (anim == null || anim[animName] == null)
            {
                Debug.LogWarning("动画不存在: " + animName);
                return;
            }

            anim.Play(animName);
            currentAnim = animName;
            isPaused = false;
            OnPlay?.Invoke(currentAnim);
        }

        public void Stop()
        {
            if (!string.IsNullOrEmpty(currentAnim))
            {
                anim.Stop();
                OnStop?.Invoke(currentAnim);
                currentAnim = null;
                isPaused = false;
            }
        }

        public void Pause()
        {
            if (!string.IsNullOrEmpty(currentAnim) && !isPaused)
            {
                anim[currentAnim].speed = 0f;
                isPaused = true;
                OnPause?.Invoke(currentAnim);
            }
        }

        public void Resume()
        {
            if (!string.IsNullOrEmpty(currentAnim) && isPaused)
            {
                anim[currentAnim].speed = 1f;
                isPaused = false;
                OnResume?.Invoke(currentAnim);
            }
        }

        void LateUpdate()
        {
            if (!string.IsNullOrEmpty(currentAnim))
            {
                var state = anim[currentAnim];

                // 进度事件
                OnProgress?.Invoke(currentAnim, state.time);

                // 检测动画播放结束（非循环）
                if (!anim.IsPlaying(currentAnim) && !isPaused && state.time >= state.length)
                {
                    OnStop?.Invoke(currentAnim);
                    currentAnim = null;
                }
            }
        }
    }
}
