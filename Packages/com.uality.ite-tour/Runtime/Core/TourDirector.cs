using System;
using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 扫描与区域进出的**效果层**：把 <see cref="TourScanPolicy"/> 与
    /// <see cref="TourRegionPolicy"/> 的决策落到 <see cref="IteTourObject"/> 上。
    ///
    /// 这一层刻意做得很薄——所有判断都在两个纯函数里，这里只剩「照做」。
    /// 与源实现的差异（design D14）：
    /// - 源实现把扫描逻辑摊在**三个订阅同一事件的处理器**里，其中第一个会清掉
    ///   「必须扫码」标志，导致第二个在同一次扫码中也会执行。这里只有
    ///   <see cref="SubmitMarkerScan"/> 一个入口，一次扫描产出一个决策。
    /// - 源实现的二次锚定许可依赖 <c>Enable()</c> 里 <c>_canAnchor = true</c> 有没有
    ///   在 <c>await</c> 之后跑到，内容为空的 Tour 会走出另一套语义。这里由决策明确规定。
    /// </summary>
    public class TourDirector
    {
        private readonly IteTourAssembler _assembler;

        private IteTourObject _activeTour;
        private bool _paused;
        private bool _forcedScanPending;

        private List<string> _pendingTourIds = new List<string>();

        /// <summary>本帧累积的区域进出，等 <see cref="FlushRegionTransitions"/> 统一结算。</summary>
        private bool _reselectPending;
        private IReadOnlyList<string> _reselectCandidates = Array.Empty<string>();

        /// <summary>
        /// 有状态变动、扫码提示待重算。提示是状态的纯函数，状态没变就不可能变——
        /// 所以按变动重算，而不是像源实现那样每 0.75 秒轮询一次。
        /// </summary>
        private bool _promptDirty;

        /// <summary>Tour 被激活（先于内容构建完成）。</summary>
        public Action<string> TourActivated;

        public Action<string> TourDeactivated;

        /// <summary>扫码提示的显隐与内容发生变化时触发（design D5）。仅在变化时发。</summary>
        public Action<ScanPrompt> ScanPromptChanged;

        private ScanPrompt _lastPrompt = ScanPrompt.Hidden;

        public TourDirector(IteTourAssembler assembler)
        {
            _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
        }

        /// <summary>把一个 Tour 的触发体积事件接进来。装配时逐个调用。</summary>
        public void Observe(IteTourObject tour)
        {
            if (tour != null)
            {
                tour.OnCameraVolumeTransition += SubmitVolumeTransition;
            }
        }

        public string ActiveTourId => _activeTour != null ? _activeTour.TourId : null;

        public bool ForcedScanPending => _forcedScanPending;

        public IReadOnlyList<string> PendingTourIds => _pendingTourIds;

        /// <summary>要求下一次扫码无条件生效。冷启动与重新戴上头显时置位。</summary>
        public void RequireScan()
        {
            _forcedScanPending = true;
            _promptDirty = true;
        }

        /// <summary>
        /// 摘下头显：停用当前 Tour 并暂停一切扫描。重新戴上：恢复并要求重新扫码。
        /// </summary>
        public void SetHeadsetMounted(bool mounted)
        {
            _promptDirty = true;

            if (mounted)
            {
                _forcedScanPending = true;
                _paused = false;
                return;
            }

            DeactivateCurrent();
            _paused = true;
        }

        public void SubmitMarkerScan(string markerId, Pose pose)
        {
            _promptDirty = true;

            var decision = TourScanPolicy.Decide(CurrentState(), Descriptors(), markerId);

            switch (decision.Action)
            {
                case ScanAction.Activate:
                    Activate(_assembler.Find(decision.TourId), pose);
                    break;

                case ScanAction.Reanchor:
                    Reanchor(_assembler.Find(decision.TourId), pose, decision.ConsumesSecondAnchor);
                    break;
            }

            if (decision.ClearsForcedScan)
            {
                _forcedScanPending = false;
            }
        }

        /// <summary>
        /// 相机进出某个 Tour 的触发体积。集合立刻更新，**重选推迟到帧末**
        /// （<see cref="FlushRegionTransitions"/>）。
        ///
        /// 推迟是必要的：相邻体积之间移动时，退出 A 与进入 B 在同一物理步内发生，
        /// 立刻结算会先停掉 A、再在下一帧才启用 B，中间空一帧。源实现用
        /// <c>WaitForSeconds(0.01f)</c> 的协程达到同样效果，这里换成帧末结算——
        /// 同样合并，但不必解释 0.01 秒是怎么来的。
        /// </summary>
        public void SubmitVolumeTransition(string tourId, VolumeTransition transition)
        {
            _promptDirty = true;

            var decision = TourRegionPolicy.Decide(CurrentState(), Descriptors(), tourId, transition);

            _pendingTourIds = new List<string>(decision.PendingTourIds);

            if (decision.ShouldReselect)
            {
                _reselectPending = true;
                _reselectCandidates = decision.ReselectCandidates;
            }
        }

        /// <summary>
        /// 帧末结算：处理本帧累积的区域进出，并在状态变过时重算扫码提示。
        /// 由 <see cref="IteRuntimeDriver"/> 在 <c>LateUpdate</c> 调用。
        /// </summary>
        public void FlushRegionTransitions()
        {
            if (_reselectPending)
            {
                _reselectPending = false;

                DeactivateCurrent();

                // 多个候选时随机挑一个——源实现的 OrderBy(Guid.NewGuid())。策略只给候选集，
                // 挑选是这里的显式选择（design D14）。
                if (_reselectCandidates.Count > 0)
                {
                    var pick = _reselectCandidates[UnityEngine.Random.Range(0, _reselectCandidates.Count)];
                    Activate(_assembler.Find(pick), pose: null);
                }

                _reselectCandidates = Array.Empty<string>();
            }

            if (_promptDirty)
            {
                _promptDirty = false;
                EvaluateScanPrompt();
            }
        }

        /// <summary>
        /// 重算扫码提示，有变化才广播。
        ///
        /// 源实现是个 0.75 秒轮询的协程，每次都直接调 UI 的 Show/Hide（无论变没变）。
        /// 这里把轮询留给驱动方，只保留「算 + 变了才发」。
        /// </summary>
        public void EvaluateScanPrompt()
        {
            var prompt = ScanPromptPolicy.Decide(CurrentState(), Descriptors());

            if (SamePrompt(_lastPrompt, prompt))
            {
                return;
            }

            _lastPrompt = prompt;
            ScanPromptChanged?.Invoke(prompt);
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

        private ScanState CurrentState() => new ScanState
        {
            Paused = _paused,
            ForcedScanPending = _forcedScanPending,
            ActiveTourId = ActiveTourId,
            PendingTourIds = _pendingTourIds,
        };

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

        /// <param name="pose">null 表示不重新锚定（区域触发的激活沿用现有位姿）。</param>
        private void Activate(IteTourObject tour, Pose? pose)
        {
            if (tour == null)
            {
                return;
            }

            DeactivateCurrent();

            _activeTour = tour;

            // 与源实现一致：不等内容构建完就返回，构建完成由 OnTourSceneLoaded 通知
            _ = tour.Enable();

            if (pose.HasValue)
            {
                tour.ChangeTourObjectTransform(pose.Value.position, pose.Value.rotation);
            }

            TourActivated?.Invoke(tour.TourId);
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
