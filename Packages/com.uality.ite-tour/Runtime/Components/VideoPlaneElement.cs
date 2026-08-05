using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Uality.IteTour.Core;
using Uality.IteTour.Data.Assets;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 视频元素的载体（挂在 prefab 上）：一块双面视频面 + 一个播放/暂停控制面板。
    /// </summary>
    public class VideoPlaneElement : MonoBehaviour
    {
        [SerializeField] private GameObject _videoPlaneObject;
        [SerializeField] private GameObject _canvasObject;

        [Space(10)]
        [Header("Video Plane Controller")]
        [SerializeField] private GameObject _videoPlaneControllerObject;
        [SerializeField] private Sprite _playSprite;
        [SerializeField] private Sprite _pauseSprite;

        private Action _onTap;
        private IteTourObject _iteTourObject;

        private RawImage _videoPlaneFront;
        private RawImage _videoPlaneBack;
        private Image _controllerFront;
        private Image _controllerBack;

        private RenderTexture _renderTexture;

        private void Awake()
        {
            _iteTourObject = GetComponentInParent<IteTourObject>();

            _videoPlaneFront = _videoPlaneObject.transform.Find("Front").GetComponent<RawImage>();
            _videoPlaneBack = _videoPlaneObject.transform.Find("Back").GetComponent<RawImage>();

            _controllerFront = _videoPlaneControllerObject.transform.Find("Front").GetComponent<Image>();
            _controllerBack = _videoPlaneControllerObject.transform.Find("Back").GetComponent<Image>();
        }

        public async Task Constructor(VideoPlane data)
        {
            var asset = _iteTourObject.GetAsset(data.Asset) as VideoAsset;
            if (asset == null)
            {
                return;
            }

            var videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.url = "file://" + asset.videoAbsoluteUrl;

            await PrepareAsync(videoPlayer);

            var width = (int)videoPlayer.width;
            var height = (int)videoPlayer.height;

            _renderTexture = new RenderTexture(width, height, 0);
            videoPlayer.targetTexture = _renderTexture;

            var radius = new Vector4(data.CornerRadius, data.CornerRadius, data.CornerRadius, data.CornerRadius);
            ApplyTexture(_videoPlaneFront, _renderTexture, radius);
            ApplyTexture(_videoPlaneBack, _renderTexture, radius);

            videoPlayer.isLooping = data.Loop;
            videoPlayer.playOnAwake = false;

            PauseVideo();
            if (data.AutoPlay)
            {
                PlayVideo();
            }

            videoPlayer.loopPointReached += vp =>
            {
                if (videoPlayer.isLooping)
                {
                    vp.Play();
                }
                else
                {
                    PauseVideo();
                }
            };

            if (!data.DoubleSided)
            {
                _videoPlaneBack.gameObject.SetActive(false);
            }

            _canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
            _videoPlaneObject.GetComponent<Button>().onClick.AddListener(() => _onTap?.Invoke());
        }

        private static Task PrepareAsync(VideoPlayer videoPlayer)
        {
            var tcs = new TaskCompletionSource<bool>();

            void OnPrepared(VideoPlayer vp)
            {
                vp.prepareCompleted -= OnPrepared;
                tcs.TrySetResult(true);
            }

            videoPlayer.prepareCompleted += OnPrepared;
            videoPlayer.Prepare();

            return tcs.Task;
        }

        private static void ApplyTexture(RawImage image, RenderTexture texture, Vector4 borderRadius)
        {
            image.texture = texture;

            var rounded = image.GetComponent<RoundedBoxUIProperties>();
            if (rounded != null)
            {
                rounded.borderRadius = borderRadius;
            }
        }

        private void OnDisable()
        {
            PauseVideo();

            var videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer != null)
            {
                videoPlayer.Stop();
                videoPlayer.targetTexture = null;
                Destroy(videoPlayer);
            }

            // 源实现只解绑不释放，RenderTexture 留在显存里。1080p 一张约 8MB，
            // 每次切 Tour 泄一张，头显上很快就吃不消了（design D21）。
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }

        public void InjectTapEvent(Action onTap) => _onTap = onTap;

        public void PlayVideo()
        {
            var videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                return;
            }

            videoPlayer.Play();
            SetControllerSprite(_pauseSprite);
        }

        public void PauseVideo()
        {
            var videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer != null)
            {
                videoPlayer.Pause();
            }

            // 注意：拿不到播放器时也照样把图标切回「播放」，与源实现一致
            SetControllerSprite(_playSprite);
        }

        public void ToggleVideo()
        {
            var videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                return;
            }

            if (videoPlayer.isPlaying)
            {
                PauseVideo();
            }
            else
            {
                PlayVideo();
            }
        }

        /// <summary>控制面板的淡入淡出。暂停时保留半透明，播放时才完全隐藏。</summary>
        public void SetControllerActive(bool active)
        {
            var videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                return;
            }

            var group = _videoPlaneControllerObject.GetComponent<CanvasGroup>();
            group.alpha = videoPlayer.isPlaying
                ? (active ? 1f : 0f)
                : (active ? 1f : 0.5f);
        }

        private void SetControllerSprite(Sprite sprite)
        {
            _controllerFront.sprite = sprite;
            _controllerBack.sprite = sprite;
        }
    }
}
