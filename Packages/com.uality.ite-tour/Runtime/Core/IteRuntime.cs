using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        private bool _started;

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

            var hierarchy = bootstrap.Validate();
            if (hierarchy.Count > 0)
            {
                Debug.LogError("[ITE] 装配不完整：" + string.Join("；", hierarchy));
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

            // 必须在 CreateTourObject 之前挂钩：alwaysDisplayed 的 Tour 在那里面就把内容
            // 建完并触发 OnTourSceneLoaded（design D29）
            _assembler.TourCreated += tour =>
            {
                _director.Observe(tour);
                tour.OnTourSceneLoaded += () => OnTourSceneLoaded?.Invoke(tour.TourId);
            };

            var driverObject = new GameObject("[ITE] Runtime Driver");
            _driver = driverObject.AddComponent<IteRuntimeDriver>();
            _driver.Director = _director;
        }

        /// <summary>加载进度，0 到 1，单调不减。</summary>
        public event Action<float> OnLoadProgress;

        /// <summary>
        /// 空间场景描述解析完成。**此刻图片尚未就绪**，`SpriteLogo` 与
        /// `SpritePreviewImage` 均为 null——图与 Tour 装配并行加载，见
        /// <see cref="OnSpaceSceneAssetsLoaded"/>（design D28）。
        /// </summary>
        public event Action<Data.IteSpaceScene> OnSpaceSceneLoaded;

        /// <summary>场景 logo 与各 Tour 预览图就位，携带同一个场景对象。</summary>
        public event Action<Data.IteSpaceScene> OnSpaceSceneAssetsLoaded;

        /// <summary>全部 Tour 实例化完成。</summary>
        public event Action OnInitialized;

        public event Action<string> OnTourActivated;

        public event Action<string> OnTourDeactivated;

        /// <summary>某个 Tour 的实体树构建完成。</summary>
        public event Action<string> OnTourSceneLoaded;

        /// <summary>扫码提示的显隐与内容发生变化（design D5）。仅在变化时触发。</summary>
        public event Action<ScanPrompt> OnScanPromptChanged;

        /// <summary>
        /// 跑完整条加载链：拉场景描述 → 逐 Tour 拉内容并实例化 → 广播初始化完成。
        /// 只跑一次；重复调用记一条警告后返回。
        /// </summary>
        /// <exception cref="Exception">
        /// 场景描述读不出来、其中不含任何 Tour，或某个 Tour 的描述读不出来。
        /// 抛出前 <see cref="IsLoading"/> 已复位，宿主的加载界面不会卡在「加载中」。
        /// </exception>
        public async Task StartAsync()
        {
            if (_started)
            {
                Debug.LogWarning("[ITE] StartAsync 已经调用过，忽略");
                return;
            }

            _started = true;
            IsLoading = true;

            try
            {
                await LoadAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>加载链是否在进行中。失败时也会复位。</summary>
        public bool IsLoading { get; private set; }

        private async Task LoadAsync()
        {
            OnLoadProgress?.Invoke(0f);

            var scene = await _pipeline.FetchSpaceSceneAsync(_bootstrap.Config.SceneName);

            // 图与 Tour 装配并行——源实现即如此。不 await，否则加载界面的标题与
            // Tour 列表要排在 N 张图的下载之后（design D28）
            _ = LoadSceneAssetsAsync(scene);

            OnSpaceSceneLoaded?.Invoke(scene);
            OnLoadProgress?.Invoke(LoadProgress.SceneParsed);

            int completed = 0;
            foreach (var tour in scene.tours)
            {
                var data = await _pipeline.FetchTourAsync(tour.tourID);
                await _assembler.CreateAsync(tour, data);

                completed++;
                OnLoadProgress?.Invoke(LoadProgress.ForTours(completed, scene.tours.Length));
            }

            OnLoadProgress?.Invoke(1f);
            OnInitialized?.Invoke();

            // 默认打开：忘了配置时应该能工作，而不是静默失效（design D2）
            _assembler.SetAllVolumesActive(true);

            // 冷启动后的第一次扫码无条件生效
            _director.RequireScan();
        }

        /// <summary>
        /// 宿主延后开启或临时关闭触发体积。加载完成时默认已打开，
        /// 不调用本方法区域触发也能工作。
        /// </summary>
        public void SetTriggerVolumesActive(bool active)
            => _assembler.SetAllVolumesActive(active);

        /// <summary>
        /// 缺图不影响导览，所以这条并行链自己吞掉异常——它是 fire-and-forget，
        /// 抛出去只会变成一条没人接的 <c>UnobservedTaskException</c>。
        /// </summary>
        private async Task LoadSceneAssetsAsync(Data.IteSpaceScene scene)
        {
            try
            {
                await _pipeline.LoadSceneSpritesAsync(scene);
                OnSpaceSceneAssetsLoaded?.Invoke(scene);
            }
            catch (Exception e)
            {
                Debug.LogError("[ITE] 场景图片加载失败：" + e);
            }
        }

        /// <summary>
        /// 不经传感器直接激活指定 Tour（design D30）。没有真机与二维码时，
        /// 这是验证内容管线的唯一手段。找不到返回 false。
        /// </summary>
        public bool ActivateTour(string tourId) => _director.ActivateById(tourId);

        /// <summary>当前激活的 tour。无激活时为 null。</summary>
        public string ActiveTourId => _director.ActiveTourId;

        /// <summary>相机当前所在触发体积对应的 tourId 集合。</summary>
        public IReadOnlyList<string> PendingTourIds => _director.PendingTourIds;

        /// <summary>已装配（未必已 Enable）的 tourId。</summary>
        public IReadOnlyList<string> AssembledTourIds
        {
            get
            {
                var ids = new List<string>(_assembler.LiveTours.Count);
                for (int i = 0; i < _assembler.LiveTours.Count; i++)
                {
                    var tour = _assembler.LiveTours[i];
                    if (tour != null)
                    {
                        ids.Add(tour.TourId);
                    }
                }

                return ids;
            }
        }

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

            if (_driver == null)
            {
                return;
            }

            // 编辑器里（非播放态）Destroy 不会真的销毁，驱动对象会留在场景里
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_driver.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_driver.gameObject);
            }
        }
    }
}
