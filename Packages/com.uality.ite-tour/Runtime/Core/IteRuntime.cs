using System;
using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 包的**唯一对外类型**：装配一次，宿主推入两件事，包广播若干事件。
    /// 公开面上没有任何 <c>interface</c>，宿主不需要实现任何行为（design D3）。
    /// </summary>
    public class IteRuntime
    {
        private readonly IteBootstrap _bootstrap;
        private readonly IteContentPipeline _pipeline;
        private readonly IteTourAssembler _assembler;
        private readonly TourDirector _director;
        private readonly IteRuntimeDriver _driver;

        /// <summary>
        /// 装配运行时。必需项缺失时返回 <c>null</c> 并逐项报名，不抛异常——
        /// 装配错误发生在宿主的 <c>Awake</c> 里，抛出去只会变成一条没有上下文的堆栈。
        /// </summary>
        public static IteRuntime Create(IteBootstrap bootstrap)
        {
            if (bootstrap == null)
            {
                Debug.LogError("[ITE] IteBootstrap 为 null，无法启动");
                return null;
            }

            var missing = bootstrap.MissingRequired();
            if (missing.Count > 0)
            {
                Debug.LogError("[ITE] 装配不完整，缺少：" + string.Join("、", missing));
                return null;
            }

            return new IteRuntime(bootstrap);
        }

        private IteRuntime(IteBootstrap bootstrap)
        {
            _bootstrap = bootstrap;

            _pipeline = new IteContentPipeline(bootstrap.Config, bootstrap.IsNetworkAvailable);

            _assembler = new IteTourAssembler(
                bootstrap.Config.TourObjectPrefab,
                bootstrap.TourRoot,
                bootstrap.AnchorRoot,
                bootstrap.Camera);

            _director = new TourDirector(_assembler);
            _director.TourActivated += id => OnTourActivated?.Invoke(id);
            _director.TourDeactivated += id => OnTourDeactivated?.Invoke(id);
            _director.ScanPromptChanged += prompt => OnScanPromptChanged?.Invoke(prompt);

            var driverObject = new GameObject("[ITE] Runtime Driver");
            _driver = driverObject.AddComponent<IteRuntimeDriver>();
            _driver.Director = _director;
        }

        /// <summary>加载进度，0 到 1，单调不减。</summary>
        public event Action<float> OnLoadProgress;

        /// <summary>全部 Tour 实例化完成。</summary>
        public event Action OnInitialized;

        public event Action<string> OnTourActivated;

        public event Action<string> OnTourDeactivated;

        /// <summary>某个 Tour 的实体树构建完成。</summary>
        public event Action<string> OnTourSceneLoaded;

        /// <summary>扫码提示的显隐与内容发生变化（design D5）。仅在变化时触发。</summary>
        public event Action<ScanPrompt> OnScanPromptChanged;

        /// <summary>宿主推入扫码结果。包不订阅任何平台的标记事件。</summary>
        public void SubmitMarkerScan(string markerId, Pose pose)
            => _director.SubmitMarkerScan(markerId, pose);

        /// <summary>宿主推入头显佩戴状态。摘下暂停导览，重新戴上要求重新扫码。</summary>
        public void SetHeadsetMounted(bool mounted)
            => _director.SetHeadsetMounted(mounted);

        /// <summary>拆掉运行时：销毁所有 Tour 与帧驱动。</summary>
        public void Shutdown()
        {
            _assembler.DestroyAll();

            if (_driver != null)
            {
                UnityEngine.Object.Destroy(_driver.gameObject);
            }
        }
    }
}
