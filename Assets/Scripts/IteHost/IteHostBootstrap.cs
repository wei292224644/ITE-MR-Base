using UnityEngine;
using Uality.IteTour.Config;
using Uality.IteTour.Core;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// ITE 导览在本工程里的装配点：把场景里的东西交给包，把包推出来的两件事接回来。
    ///
    /// 这是**唯一**知道 <c>Uality.IteTour</c> 与 <c>MRBase.*</c> 两边的类型。
    /// 依赖方向单向：包对宿主零知识（design D2）。
    /// </summary>
    public class IteHostBootstrap : MonoBehaviour
    {
        [Header("包配置")]
        [SerializeField] private IteRuntimeConfig config;

        [Header("场景装配")]
        [SerializeField]
        [Tooltip("锚定目标，扫码后被移动到标记位姿")]
        private Transform anchorRoot;

        [SerializeField]
        [Tooltip("Tour 实例的父节点")]
        private Transform tourRoot;

        [SerializeField]
        [Tooltip("用于识别谁进出触发体积，通常是 XR Origin 的主相机")]
        private Transform xrCamera;

        // 标记来源字段与 MarkerSourceAdapter 一并下线(unified-marker-tracking-contract change,
        // task 5.4)：MarkerTrackingBootstrapper 已删除,ITE 导览的扫码激活暂时不可用,
        // 只能靠 ActivateTour 手动激活。重新接入见新契约(MarkerTrackingSession)。
        // [Header("标记来源")]
        // [SerializeField]
        // [Tooltip("留空则不接标记源，只能靠 ActivateTour 手动激活")]
        // private MarkerTrackingBootstrapper markerTracking;

        [Header("行为")]
        [SerializeField]
        [Tooltip("关掉可用于离线调试：跳过全部下载与版本查询，直接读本地缓存")]
        private bool networkAvailable = true;

        private IteRuntime _ite;
        // private MarkerSourceAdapter _markerSource; // 见上方 markerTracking 字段注释
        private HeadsetPresenceAdapter _headsetPresence;

        /// <summary>装配好的运行时。启动失败时为 null。</summary>
        public IteRuntime Runtime => _ite;

        private async void Start()
        {
            _ite = IteRuntime.Create(new IteBootstrap
            {
                Config = config,
                AnchorRoot = anchorRoot,
                TourRoot = tourRoot,
                Camera = xrCamera,
                IsNetworkAvailable = () => networkAvailable,
            });

            // Create 已经逐项报过缺什么，这里不再重复
            if (_ite == null)
            {
                enabled = false;
                return;
            }

            // 标记源已随旧生产链下线,扫码激活暂不可用,只能靠 ActivateTour 手动激活
            // （unified-marker-tracking-contract change,task 5.4）。
            Debug.LogWarning("[ITE Host] 未接标记源，扫码激活不可用");

            _headsetPresence = new HeadsetPresenceAdapter(_ite.SetHeadsetMounted);

            await _ite.StartAsync();
        }

        private void Update()
        {
            if (_headsetPresence != null)
            {
                _headsetPresence.Poll();
            }
        }

        private void OnDestroy()
        {
            if (_ite != null)
            {
                _ite.Shutdown();
                _ite = null;
            }
        }
    }
}
