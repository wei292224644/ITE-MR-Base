using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 扫描、区域进出与佩戴状态的**效果层**：输入交给 <see cref="TourGuide"/>（纯状态核心），
    /// 它返回的 <see cref="GuideEffect"/> 在这里落到 <see cref="IteTourObject"/> 上。
    ///
    /// 这一层刻意做得很薄——状态与所有判断都在 TourGuide 与三个策略函数里，这里只剩「照做」
    /// （ite-guide-state-machine D7）。与源实现的差异见 design D14：源实现把扫描逻辑摊在三个订阅
    /// 同一事件的处理器里、二次锚定许可依赖 await 时序，这里都由一次决策明确规定。
    /// </summary>
    public class TourDirector
    {
        private readonly IteTourAssembler _assembler;
        private readonly TourGuide _guide;

        // Descriptors 是方法组，每次直接传给 EndOfFrame 都会新分配一个委托实例；缓存一份，
        // 每帧的 LateUpdate 不用再分配。
        private readonly Func<IReadOnlyList<TourDescriptor>> _descriptors;

        private IteTourObject _activeTour;
        private ScanPrompt _lastPrompt = ScanPrompt.Hidden;

        /// <summary>Tour 被激活（先于内容构建完成）。</summary>
        public Action<string> TourActivated;

        public Action<string> TourDeactivated;

        /// <summary>扫码提示的显隐与内容发生变化时触发（design D5）。仅在变化时触发。</summary>
        public Action<ScanPrompt> ScanPromptChanged;

        /// <summary>导览状态或进入原因变化时触发（ite-guide-state-machine D8）。</summary>
        public Action<GuideState, GuideStateReason> GuideStateChanged;

        /// <param name="pick">
        /// 区域重选有多个候选时挑哪个。缺省随机——与源实现的 OrderBy(Guid.NewGuid()) 一致。
        /// </param>
        public TourDirector(IteTourAssembler assembler, Func<IReadOnlyList<string>, string> pick = null)
        {
            _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
            _guide = new TourGuide(pick ?? (ids => ids[UnityEngine.Random.Range(0, ids.Count)]));
            _descriptors = Descriptors;
            _guide.StateChanged += (state, reason) =>
            {
                Debug.Log($"[ITE] 状态 → {state}（原因={reason}）");
                GuideStateChanged?.Invoke(state, reason);
            };
        }

        /// <summary>把一个 Tour 的触发体积事件接进来。装配时逐个调用。</summary>
        public void Observe(IteTourObject tour)
        {
            if (tour != null)
            {
                tour.OnCameraVolumeTransition += SubmitVolumeTransition;
            }
        }

        public string ActiveTourId => _guide.ActiveTourId;

        public GuideState State => _guide.State;

        public GuideStateReason Reason => _guide.Reason;

        /// <summary>当前扫码提示，即最近一次广播的值；宿主挂钩晚于首次广播时用它同步。</summary>
        public ScanPrompt ScanPrompt => _lastPrompt;

        public IReadOnlyList<string> PendingTourIds => _guide.PendingTourIds;

        /// <summary>
        /// 运行中直接进入「等待扫码定位」：停掉在播 Tour，与冷启动同一状态（ite-guide-state-machine D2）。
        /// 摘下期间调用不切换（ite-guide-state-machine D3）。
        /// </summary>
        public void RequireScan(GuideStateReason reason) => Apply(_guide.RequireScan(reason));

        /// <summary>摘下：停掉在播 Tour，进入 Suspended。戴上：从 Suspended 进入 AwaitingScan。</summary>
        public void SetHeadsetMounted(bool mounted) => Apply(_guide.SetHeadsetMounted(mounted));

        /// <summary>
        /// 不经传感器直接激活指定 Tour（design D30），沿用现有锚定。只在 Anchored 下可用
        /// （ite-guide-state-machine D4）。找不到或状态不允许时返回 false。
        /// </summary>
        public bool ActivateById(string tourId)
        {
            if (_assembler.Find(tourId) == null)
            {
                Debug.LogError("[ITE] 找不到 Tour：" + tourId);
                return false;
            }

            if (!_guide.TryActivateById(tourId, out var effect))
            {
                Debug.LogWarning($"[ITE] 当前状态 {_guide.State} 下不能直接激活 Tour：{tourId}（需先扫码定位）");
                return false;
            }

            Apply(effect);
            return true;
        }

        public void SubmitMarkerScan(string markerId, Pose pose)
        {
            var before = _guide.Snapshot();
            var effect = _guide.SubmitScan(markerId, pose, Descriptors(), out var decision);

            // 真机上判断「为什么没反应」只能靠这一行：决策依据的状态与扫到的位姿都带上。
            Debug.Log(
                $"[ITE] 扫码 {markerId} → {decision.Action}" +
                $"（状态={before.State} 在播={before.ActiveTourId ?? "无"} 所在区域=[{JoinIds(before.PendingTourIds)}]）" +
                $" 位姿 pos={pose.position.ToString("F3")} rot={pose.rotation.eulerAngles.ToString("F1")}");

            Apply(effect);
        }

        /// <summary>
        /// 相机进出某个 Tour 的触发体积：只更新所在区域集合（I3），要不要换 Tour 在帧末按净变化
        /// 判一次（ite-guide-state-machine D5）。
        /// </summary>
        public void SubmitVolumeTransition(string tourId, VolumeTransition transition)
        {
            _guide.SubmitVolumeTransition(tourId, transition);
            Debug.Log($"[ITE] 区域 {transition} {tourId} → 所在区域=[{JoinIds(_guide.PendingTourIds)}]");
        }

        /// <summary>
        /// 帧末结算，由 <see cref="IteRuntimeDriver"/> 在 <c>LateUpdate</c> 调用：区域重选（ite-guide-state-machine D5），
        /// （状态、在播、所在区域）变了才重算扫码提示（ite-guide-state-machine D6）。
        ///
        /// 在帧末而不是在事件上结算：相邻体积之间移动时，退出 A 与进入 B 在同一物理步内发生，
        /// 按净变化判一次就直接从 A 换到 B，不会先停 A 再启 B。
        /// </summary>
        public void EndOfFrame()
        {
            var previousActive = _guide.ActiveTourId;
            var effect = _guide.EndOfFrame(_descriptors, out bool changed);

            if (effect.Deactivate || effect.ActivateTourId != null)
            {
                Debug.Log(effect.ActivateTourId != null
                    ? $"[ITE] 区域重选：{previousActive ?? "无"} → {effect.ActivateTourId}（沿用现有锚定）"
                    : $"[ITE] 区域重选：停用 {previousActive}，范围内无 regionalTrigger");
            }

            Apply(effect);

            if (changed)
            {
                EvaluateScanPrompt();
            }
        }

        /// <summary>
        /// 重算扫码提示，有变化才广播。源实现是个 0.75 秒轮询的协程，每次都直接调 UI 的
        /// Show/Hide（无论变没变）；这里只保留「算 + 变了才发」。
        /// </summary>
        private void EvaluateScanPrompt()
        {
            var prompt = ScanPromptPolicy.Decide(_guide.Snapshot(), Descriptors());

            if (SamePrompt(_lastPrompt, prompt))
            {
                return;
            }

            _lastPrompt = prompt;
            ScanPromptChanged?.Invoke(prompt);
        }

        private void Apply(GuideEffect effect)
        {
            if (effect.Deactivate)
            {
                DeactivateCurrent();
            }

            if (effect.ActivateTourId != null)
            {
                var tour = _assembler.Find(effect.ActivateTourId);
                if (tour == null)
                {
                    // 核心已记下它在播，效果层却找不到对象：不能静默，否则两边从此不一致
                    Debug.LogError("[ITE] 要激活的 Tour 不存在：" + effect.ActivateTourId);
                }
                else
                {
                    Activate(tour, effect.AnchorPose);
                }
            }

            if (effect.ReanchorTourId != null)
            {
                Reanchor(_assembler.Find(effect.ReanchorTourId), effect.ReanchorPose, effect.ConsumesSecondAnchor);
            }
        }

        private static bool SamePrompt(ScanPrompt a, ScanPrompt b)
        {
            if (a.State != b.State)
            {
                return false;
            }

            var left = a.TourIds ?? Array.Empty<string>();
            var right = b.TourIds ?? Array.Empty<string>();

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string JoinIds(IReadOnlyList<string> ids)
            => ids == null ? "" : string.Join(",", ids);

        private List<TourDescriptor> Descriptors()
        {
            var descriptors = new List<TourDescriptor>();

            foreach (var tour in _assembler.LiveTours)
            {
                if (tour == null)
                {
                    continue;
                }

                descriptors.Add(new TourDescriptor
                {
                    TourId = tour.TourId,
                    DisplayType = tour.DisplayType,
                    SecondAnchorAvailable = tour.CanSecondAnchor(),
                });
            }

            return descriptors;
        }

        /// <param name="pose">null 表示不重新锚定（区域唤醒与直接激活沿用现有位姿）。</param>
        private void Activate(IteTourObject tour, Pose? pose)
        {
            if (ReferenceEquals(_activeTour, tour))
            {
                if (pose.HasValue)
                {
                    tour.ChangeTourObjectTransform(pose.Value.position, pose.Value.rotation);
                }

                _ = tour.Enable();
                return;
            }

            DeactivateCurrent();

            _activeTour = tour;

            if (pose.HasValue)
            {
                tour.ChangeTourObjectTransform(pose.Value.position, pose.Value.rotation);
            }

            // 先广播再 Enable。已建树时 Enable 是空操作、不再派发组件侧 Loaded
            // （LoadTrigger 不能重放）；宿主超时由 IteRuntime 在 Activated 回调里补一次。
            TourActivated?.Invoke(tour.TourId);

            // 与源实现一致：不等内容构建完就返回，构建完成由 OnTourSceneLoaded 通知
            _ = tour.Enable();
        }

        private void Reanchor(IteTourObject tour, Pose pose, bool consumesSecondAnchor)
        {
            if (tour == null)
            {
                return;
            }

            tour.ChangeTourObjectTransform(pose.position, pose.rotation);

            if (consumesSecondAnchor)
            {
                tour.SecondAnchored();
            }

            Debug.Log("[ITE] Reanchor " + tour.TourId + (consumesSecondAnchor ? " consumesSecondAnchor" : ""));
        }

        private void DeactivateCurrent()
        {
            if (_activeTour == null)
            {
                return;
            }

            var tourId = _activeTour.TourId;

            _activeTour.Disable();
            _activeTour = null;

            TourDeactivated?.Invoke(tourId);
        }
    }
}
