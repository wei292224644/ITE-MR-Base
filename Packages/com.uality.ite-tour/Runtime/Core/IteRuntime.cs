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

        /// <summary>加载完成（含所有 Tour 装配完毕）后置真；加载失败时不置真（ite-guide-state-machine D9）。</summary>
        private bool _initialized;

        /// <summary>装配时收下的标记绑定，供 <see cref="SubmitMarkerScan"/> 反查。</summary>
        private readonly List<MarkerBinding> _bindings = new List<MarkerBinding>();

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
            _director.TourActivated += id =>
            {
                OnTourActivated?.Invoke(id);

                // alwaysDisplayed 在装配时已经建树并派发过组件侧 OnTourSceneLoaded。
                // 再次 Activate 不再重建，组件事件不能重放（LoadTrigger 会再跑一遍）。
                // 宿主超时监视仍需要一次 Loaded，由这里补发给宿主。
                var tour = _assembler.Find(id);
                if (tour != null && tour.IsSceneReady)
                {
                    OnTourSceneLoaded?.Invoke(id);
                }
            };
            _director.TourDeactivated += id => OnTourDeactivated?.Invoke(id);
            _director.ScanPromptChanged += prompt => OnScanPromptChanged?.Invoke(prompt);
            _director.GuideStateChanged += (state, reason) => OnGuideStateChanged?.Invoke(state, reason);

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

            // 锚定结算窗口靠 WaitForFixedUpdate 判定「锚定后那一步物理已经跑完」，只在 FixedUpdate 模拟下成立。
            // 模式被改掉时窗口时序失效，真机上只表现为扫码后内容被切走——必须出声（ite-current-tour D9）。
            if (Physics.simulationMode != SimulationMode.FixedUpdate)
            {
                Debug.LogError(
                    $"[ITE] Physics.simulationMode = {Physics.simulationMode}，区域结算要求 FixedUpdate。" +
                    "检查 Project Settings > Physics。");
            }
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
        /// 导览状态或进入原因变化（ite-guide-state-machine D8）。宿主据此显示对应提示，
        /// 例如「视角已重定位」只在 <see cref="Core.GuideState.AwaitingScan"/> 且原因是
        /// <see cref="Core.GuideStateReason.Recentered"/> 时显示。
        /// </summary>
        public event Action<GuideState, GuideStateReason> OnGuideStateChanged;

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

            var tours = TourAssembly.EnabledTours(scene.tours);
            if (tours.Count == 0)
            {
                throw new Exception("No enabled tours in IteSpaceScene: " + _bootstrap.Config.SceneName);
            }

            _bindings.Clear();

            int completed = 0;
            foreach (var tour in tours)
            {
                var data = await _pipeline.FetchTourAsync(tour.tourID);
                await _assembler.CreateAsync(tour, data);

                // alwaysDisplayed 在装配里就建好树并显示了；按当前导览状态收一次——锚定前不该看见它（ite-current-tour D8）
                _director.SyncAlwaysDisplayed();

                // 绑定在装配时收下：这里 IteSpaceScene.Tour 就在手上，
                // 不必为了一个字段去改 IteTourObject 的形状。
                _bindings.Add(new MarkerBinding(tour.tourID, tour.aprilTagID));

                completed++;
                OnLoadProgress?.Invoke(LoadProgress.ForTours(completed, tours.Count));
            }

            OnLoadProgress?.Invoke(1f);

            // 装配是逐个 Tour 登记进 _liveTours 的（IteTourAssembler.cs），标记桥在 runtime 创建时
            // 就已经接上——加载途中扫到已装配的码本来就能解析。这里置真之前扫码一律忽略，恢复
            // 基线（旧实现）的语义：加载期间扫码不生效（ite-guide-state-machine D9）。
            _initialized = true;
            OnInitialized?.Invoke();

            // 默认打开：忘了配置时应该能工作，而不是静默失效（design D2）
            _assembler.SetAllVolumesActive(true);

            // 冷启动状态由 TourGuide 构造时给出（AwaitingScan / ColdStart），这里不再额外要求重扫
            // （ite-guide-state-machine D9）。加载途中摘下头显时状态是 Suspended，戴上后照常进入等待扫码。
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
        /// 不经传感器直接激活指定 Tour（design D30），沿用现有锚定，它随即成为当前 Tour。只在已定位（Anchored）
        /// 状态下可用，其他状态返回 false（ite-guide-state-machine D4）；alwaysDisplayed 返回 false（ite-current-tour D10）；
        /// 找不到也返回 false。
        /// </summary>
        public bool ActivateTour(string tourId) => _director.ActivateById(tourId);

        /// <summary>导览当前状态（ite-guide-state-machine D1）。</summary>
        public GuideState GuideState => _director.State;

        /// <summary>进入当前状态的原因。</summary>
        public GuideStateReason GuideStateReason => _director.Reason;

        /// <summary>
        /// 当前扫码提示，即最近一次 <see cref="OnScanPromptChanged"/> 广播的值。构造完成后的
        /// 第一帧就会广播一次（ite-guide-state-machine D9），宿主挂钩可能晚于这一刻——
        /// 挂钩时应先读一次这个属性同步，不能只靠事件。
        /// </summary>
        public ScanPrompt ScanPrompt => _director.ScanPrompt;

        /// <summary>当前激活的 tour。无激活时为 null。</summary>
        public string ActiveTourId => _director.ActiveTourId;

        /// <summary>相机当前所在触发体积对应的 tourId，按进入先后排列（ite-current-tour D2）。</summary>
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

        /// <summary>
        /// 宿主推入一次扫码：**原始 payload + 标记种类 + 位姿**（design D5）。包不订阅任何
        /// 平台的标记事件，也不认识宿主的平台类型——种类用包自有的 <see cref="MarkerKind"/>。
        ///
        /// payload 到 tourId 的解析在这里完成，宿主侧不做任何解析：payload 的形状由内容方
        /// 定义且会变，焊在宿主意味着内容每改一次码，宿主就要出一次包。
        ///
        /// <paramref name="pose"/> 是标记的世界位姿，按 <see cref="MarkerFrame"/> 的宿主约定给
        /// （X 印刷左、Y 印刷上、Z 出纸面）；换算到内容锚点在这里做，宿主不要自己转。
        ///
        /// 初始化完成前（含加载失败）忽略扫码，恢复基线语义（ite-guide-state-machine D9）：
        /// 装配是逐个 Tour 登记的，标记桥在 runtime 创建时就已接上，不加这道门禁的话，加载
        /// 途中扫到已装配的码会直接激活并锚定。
        /// </summary>
        public void SubmitMarkerScan(MarkerKind kind, string rawPayload, Pose pose)
        {
            if (!_initialized)
            {
                Debug.Log($"[ITE] 加载未完成，忽略扫码 kind={kind} payload=\"{rawPayload}\"");
                return;
            }

            var outcome = MarkerIdentity.Resolve(kind, rawPayload, _bindings, out string tourId);
            if (outcome != MarkerResolution.Resolved)
            {
                LogUnresolved(outcome, kind, rawPayload, tourId);
                return;
            }

            _director.SubmitMarkerScan(tourId, MarkerFrame.ToContentAnchorPose(pose));
        }

        /// <summary>
        /// 解析不出时必须出声：静默丢弃与「根本没扫到」在真机上不可区分。
        /// 两种失败分开说——一种是码的形状不对（该找内容方），
        /// 一种是码对但场景里没这个 Tour（该找空间场景描述）。
        /// </summary>
        private void LogUnresolved(MarkerResolution outcome, MarkerKind kind, string rawPayload, string tourId)
        {
            if (outcome == MarkerResolution.Unparsable)
            {
                Debug.Log($"[ITE] 标记 payload 解析不出 tourId，已忽略：kind={kind} payload=\"{rawPayload}\"");
                return;
            }

            var available = string.Join("、", AssembledTourIds);
            var who = tourId != null ? $"tourId=\"{tourId}\"" : $"payload=\"{rawPayload}\"";
            Debug.Log($"[ITE] 标记解析出的 Tour 不在场景中，已忽略：kind={kind} {who}；当前可用：{available}");
        }

        /// <summary>
        /// 运行中直接进入「等待扫码定位」：停掉在播的 Tour，状态与冷启动一致（ite-guide-state-machine D2）。
        /// 用于「上一次锚定不再可信」的场合——例如系统重定位改了追踪原点：内容在世界坐标里没动，
        /// 物理世界却整个转了过去，不重扫就会一直偏着（design D18），此时传
        /// <see cref="Core.GuideStateReason.Recentered"/>。头显摘下期间调用不切换状态，戴上时本就会要求扫码（ite-guide-state-machine D3）。
        /// </summary>
        public void RequireScan(GuideStateReason reason = GuideStateReason.HostRequested)
            => _director.RequireScan(reason);

        /// <summary>
        /// 宿主推入头显佩戴状态。摘下：停掉在播 Tour、不认扫码、区域不唤醒 Tour；戴上：进入等待扫码定位
        /// （ite-guide-state-machine §3.2）。
        /// </summary>
        public void SetHeadsetMounted(bool mounted)
            => _director.SetHeadsetMounted(mounted);

        /// <summary>拆掉运行时：销毁所有 Tour 与帧驱动。</summary>
        public void Shutdown()
        {
            _assembler.DestroyAll();

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
