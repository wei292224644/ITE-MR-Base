using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 播放富文本的朗读音频。
    ///
    /// 注意：<c>Repetition</c> / <c>Delay</c> / <c>AutoPlay</c> 解析出来了但
    /// **没有任何消费方**——触发时一律直接 <c>PlayAudio()</c>。源实现即如此，已记入 TODO。
    /// </summary>
    public class PlayAudioActionUnityComponent : BaseActionComponent<RichTextUnityComponent>
    {
        private PlayAudioSettings _settings = PlayAudioSettings.Default;

        protected override void AfterAwake()
            => _eventEmitter.On(ComponentActionName.PlayAudioAction, OnActionInvoke);

        private void OnDestroy()
            => _eventEmitter.Off(ComponentActionName.PlayAudioAction, OnActionInvoke);

        public override async Task Constructor(object data)
        {
            await base.Constructor(data);
            _settings = _settings.Merge((PlayAudio)data);
        }

        private void OnActionInvoke(object data)
        {
            _settings = _settings.Merge(((PlayAudioActionParameters)data).Parameters);
            element?.PlayAudio();
        }
    }

    /// <summary>
    /// 让实体绕轴旋转。
    /// </summary>
    public class SpinActionUnityComponent : BaseActionComponent
    {
        private SpinSettings _settings = SpinSettings.Default;

        private Coroutine _spinCoroutine;
        private Quaternion _initialRotation;

        protected override void AfterAwake()
        {
            _eventEmitter.On(ComponentActionName.SpinAction, OnActionInvoke);
            _entity.OnPreDisable += ResetSpin;
        }

        private void OnDisable() => ResetSpin();

        /// <summary>
        /// **拼写错误原样保留**（tasks 6.8）：源实现写的就是 <c>Oestroy</c> 而不是
        /// <c>OnDestroy</c>，因此销毁时事件与 <c>OnPreDisable</c> 从未反注册。
        /// 实际无害——<c>_eventEmitter</c> 与 <c>_entity</c> 都在同一 GameObject 上、
        /// 一起销毁。已记入 TODO。
        /// </summary>
        private void Oestroy()
        {
            _eventEmitter.Off(ComponentActionName.SpinAction, OnActionInvoke);
            _entity.OnPreDisable -= ResetSpin;
        }

        public override async Task Constructor(object data)
        {
            await base.Constructor(data);

            _initialRotation = _entity.Root.transform.localRotation;
            _settings = _settings.Merge((SpinAction)data);
        }

        private void ResetSpin()
        {
            if (_spinCoroutine == null)
            {
                return;
            }

            StopCoroutine(_spinCoroutine);
            _spinCoroutine = null;
            _entity.Root.transform.localRotation = _initialRotation;
        }

        private void OnActionInvoke(object data)
        {
            _settings = _settings.Merge(((SpinActionParameters)data).Parameters);

            ResetSpin();
            _spinCoroutine = StartCoroutine(SpinRoutine());
        }

        private IEnumerator SpinRoutine()
        {
            if (_settings.Delay > 0)
            {
                yield return new WaitForSeconds(_settings.Delay);
            }

            var degreesPerSecond = _settings.DegreesPerSecond;
            var targetRotation = Mathf.Abs(360f * _settings.Revolutions);
            var totalRotated = 0f;

            while (true)
            {
                var angleThisFrame = degreesPerSecond * Time.deltaTime;

                // 轴每帧重新取：实体可能跟着 Tour 一起被重新锚定
                _entity.Root.transform.Rotate(
                    _settings.AxisVector(transform.parent), angleThisFrame, Space.World);

                yield return null;

                if (_settings.Loop)
                {
                    continue;
                }

                totalRotated += Mathf.Abs(angleThisFrame);
                if (totalRotated >= targetRotation)
                {
                    ResetSpin();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 播放 EMW 模型自带的动画片段，支持区间播放、倒播与循环次数。
    /// </summary>
    public class PlayAnimationActionUnityComponent : BaseActionComponent<EMWModelRenderUnityComponent>
    {
        private Internal.LegacyAnimationController Animation => element?.Animation;

        private PlayAnimationSettings _settings;

        private AnimationState _state;
        private bool _isPlaying;
        private bool _isReversing;
        private bool _isDelayed;
        private float _delayTimer;
        private int _loopCounter;

        protected override void AfterAwake()
        {
            _eventEmitter.On(ComponentActionName.PlayAnimationAction, OnActionInvoke);
            _entity.OnPreDisable += StopPartial;
        }

        private void OnDestroy()
        {
            _eventEmitter.Off(ComponentActionName.PlayAnimationAction, OnActionInvoke);
            _entity.OnPreDisable -= StopPartial;
        }

        public override async Task Constructor(object data)
        {
            await base.Constructor(data);

            if (Animation == null)
            {
                Debug.LogError("[ITE] PlayAnimationAction 找不到动画控制器，模型可能不带动画");
                return;
            }

            _settings = PlayAnimationSettings.From((PlayAnimation)data, Animation.clip.length);
        }

        private void OnActionInvoke(object data)
        {
            if (Animation == null)
            {
                return;
            }

            _settings = PlayAnimationSettings.From(
                ((PlayAnimationActionParameters)data).Parameters, Animation.clip.length);

            StopAllCoroutines();
            StartCoroutine(PlayPartial());
        }

        private IEnumerator PlayPartial()
        {
            // 等到帧末再起播：Constructor 与本协程可能在同一帧内接连发生
            yield return new WaitForEndOfFrame();

            var clipName = Animation.clip.name;
            _state = Animation[clipName];
            _state.time = _settings.StartTime;
            _state.speed = 0f; // 延时期间不动
            _state.enabled = true;
            Animation.Play(clipName);

            _isPlaying = true;
            _isReversing = false;
            _isDelayed = true;
            _delayTimer = _settings.Delay;
            _loopCounter = 0;
        }

        public void PausePartial()
        {
            if (_state != null)
            {
                _state.speed = 0;
            }
        }

        public void ResumePartial()
        {
            if (_state != null)
            {
                _state.speed = _isReversing ? -Mathf.Abs(_settings.Speed) : Mathf.Abs(_settings.Speed);
            }
        }

        public void StopPartial()
        {
            _isPlaying = false;
            _isDelayed = false;

            if (_state != null)
            {
                _state.enabled = false;
            }

            Animation?.Stop();
        }

        private void OnDisable() => StopPartial();

        private void Update()
        {
            if (!_isPlaying || _state == null)
            {
                return;
            }

            if (_isDelayed)
            {
                _delayTimer -= Time.deltaTime;
                if (_delayTimer <= 0f)
                {
                    _isDelayed = false;
                    ResumePartial();
                }

                return;
            }

            var reachedEnd = _isReversing
                ? _state.time <= _settings.StartTime
                : _state.time >= _settings.EndTime;

            if (!reachedEnd)
            {
                return;
            }

            if (_settings.AutoReverse)
            {
                _isReversing = !_isReversing;
                _state.speed = _isReversing ? -Mathf.Abs(_settings.Speed) : Mathf.Abs(_settings.Speed);
            }
            else if (_settings.Loop)
            {
                _loopCounter++;
                if (_settings.Repetition > 0 && _loopCounter >= _settings.Repetition)
                {
                    StopPartial();
                }
                else
                {
                    _state.time = _isReversing ? _settings.EndTime : _settings.StartTime;
                }
            }
            else
            {
                StopPartial();
            }
        }
    }

    /// <summary>
    /// 切换实体可见性，可带缩放动画。
    /// </summary>
    public class ToggleVisibilityActionUnityComponent : BaseActionComponent
    {
        private ToggleVisibilitySettings _settings;
        private Coroutine _animCoroutine;

        /// <summary>已触发次数。<c>Once</c> 模式据此只放行第一次。</summary>
        private int _invokeCount;

        protected override void AfterAwake()
            => _eventEmitter.On(ComponentActionName.ToggleVisibilityAction, OnActionInvoke);

        private void OnDestroy()
            => _eventEmitter.Off(ComponentActionName.ToggleVisibilityAction, OnActionInvoke);

        public override async Task Constructor(object data)
        {
            await base.Constructor(data);
            _settings = ToggleVisibilitySettings.From((ToggleVisibility)data);
        }

        private void OnActionInvoke(object data)
        {
            _settings = ToggleVisibilitySettings.From(((ToggleVisibilityActionParameters)data).Parameters);

            if (_settings.ToggleCount == ToggleCount.Always || _invokeCount <= 0)
            {
                Toggle();
            }

            _invokeCount++;
        }

        private void Toggle()
        {
            if (_entity.Root.activeSelf)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Show() => Animate(true);

        public void Hide() => Animate(false);

        private void Animate(bool show)
        {
            if (_animCoroutine != null)
            {
                StopCoroutine(_animCoroutine);
            }

            _animCoroutine = StartCoroutine(AnimateVisibility(show));
        }

        private IEnumerator AnimateVisibility(bool show)
        {
            // 切换前先通知动作组件复位（停旋转、停动画）
            if (show)
            {
                _entity.OnPreEnable?.Invoke();
            }
            else
            {
                _entity.OnPreDisable?.Invoke();
            }

            if (_settings.Duration <= 0f)
            {
                _entity.Root.SetActive(show);
                _entity.Root.transform.localScale = show ? Vector3.one : Vector3.zero;
                yield break;
            }

            if (show)
            {
                _entity.Root.SetActive(true);
            }

            var startScale = show ? Vector3.zero : Vector3.one;
            var endScale = show ? Vector3.one : Vector3.zero;

            var time = 0f;
            while (time < _settings.Duration)
            {
                _entity.Root.transform.localScale = Vector3.Lerp(startScale, endScale, time / _settings.Duration);
                time += Time.deltaTime;
                yield return null;
            }

            _entity.Root.transform.localScale = endScale;

            if (!show)
            {
                _entity.Root.SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (_animCoroutine != null)
            {
                StopCoroutine(_animCoroutine);
                _animCoroutine = null;
            }
        }
    }
}
