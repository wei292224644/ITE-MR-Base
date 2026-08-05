using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Uality.IteTour.Core;
using Uality.IteTour.Data.Assets;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 富文本元素的载体（挂在 prefab 上）：一块双面图片 + 一个播放器面板。
    /// </summary>
    public class RichTextElement : MonoBehaviour
    {
        [SerializeField] private Transform _texturePlane;
        [SerializeField] private Transform _audioControllerPlane;

        private Action _onTap;
        private IteTourObject _iteTourObject;

        private RectTransform _texturePlaneFront;
        private RectTransform _texturePlaneBack;

        private GameObject _audioControllerFront;
        private GameObject _audioControllerBack;
        private AudioSource _audioSource;

        private void Awake()
        {
            _iteTourObject = GetComponentInParent<IteTourObject>();

            _texturePlaneFront = _texturePlane.Find("Front").GetComponent<RectTransform>();
            _texturePlaneBack = _texturePlane.Find("Back").GetComponent<RectTransform>();

            _audioSource = _audioControllerPlane.GetComponent<AudioSource>();
            _audioControllerFront = _audioControllerPlane.Find("Front").gameObject;
            _audioControllerBack = _audioControllerPlane.Find("Back").gameObject;
        }

        public Task Constructor(RichText data)
        {
            var asset = _iteTourObject.GetAsset(data.Asset) as RichTextAsset;
            if (asset == null)
            {
                Debug.LogWarning("[ITE] 富文本资源缺失: " + data.Asset);
                return Task.CompletedTask;
            }

            var audio = asset.audioInstance;
            var sprite = asset.spriteInstance;

            SetUpAudio(data, audio);
            SetUpTexture(data, asset, sprite, hasAudio: audio != null);

            if (!data.DoubleSided)
            {
                _texturePlaneBack.gameObject.SetActive(false);
            }

            _texturePlane.GetComponent<Button>().onClick.AddListener(() => _onTap?.Invoke());

            return Task.CompletedTask;
        }

        private void SetUpAudio(RichText data, AudioClip audio)
        {
            if (audio == null)
            {
                _audioControllerPlane.gameObject.SetActive(false);
                return;
            }

            _audioSource.clip = audio;
            _audioSource.playOnAwake = false;
            _audioSource.loop = data.Loop;

            if (data.AutoPlay)
            {
                PlayAudio();
            }
        }

        private void SetUpTexture(RichText data, RichTextAsset asset, Sprite sprite, bool hasAudio)
        {
            if (asset.AudioOnly || sprite == null)
            {
                _texturePlane.gameObject.SetActive(false);
                return;
            }

            var radius = new Vector4(data.CornerRadius, data.CornerRadius, data.CornerRadius, data.CornerRadius);

            ApplySprite(_texturePlaneFront.GetComponent<Image>(), sprite, radius);
            ApplySprite(_texturePlaneBack.GetComponent<Image>(), sprite, radius);

            var width = sprite.texture.width;
            var height = sprite.texture.height;
            _texturePlane.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);

            // 没有配音时播放器面板是空的，挪到图片外侧
            if (!hasAudio)
            {
                _audioControllerPlane.localPosition = RichTextLayout.AudioControllerOffset(width, height);
            }
        }

        private static void ApplySprite(Image image, Sprite sprite, Vector4 borderRadius)
        {
            image.sprite = sprite;

            var rounded = image.GetComponent<RoundedBoxUIProperties>();
            if (rounded != null)
            {
                rounded.borderRadius = borderRadius;
            }
        }

        public void InjectTapEvent(Action onTap) => _onTap = onTap;

        public void ToggleAudio()
        {
            if (_audioSource.isPlaying)
            {
                PauseAudio();
            }
            else
            {
                PlayAudio();
            }
        }

        public void PauseAudio()
        {
            if (!_audioSource.isPlaying)
            {
                return;
            }

            _audioSource.Pause();
            SetRunning(false);
        }

        public void PlayAudio()
        {
            if (_audioSource.isPlaying)
            {
                return;
            }

            _audioSource.Play();
            SetRunning(true);
        }

        private void SetRunning(bool running)
        {
            _audioControllerFront.GetComponent<Animator>().SetBool("Running", running);
            _audioControllerBack.GetComponent<Animator>().SetBool("Running", running);
        }
    }
}
