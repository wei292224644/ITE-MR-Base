namespace Uality.IteTour.Core
{
    /// <summary>
    /// Tour 内容树的生命周期。纯状态机，不碰 GameObject。
    ///
    /// <see cref="IteTourObject.Enable"/> 必须幂等：再调一次不能再叠一层实体。
    /// Disable 与 Destroy 通过递增世代作废进行中的构建，避免「拆树的同时 Enable 还在 await」。
    /// </summary>
    public struct TourSceneLifecycle
    {
        public enum State
        {
            Empty,
            Building,
            Ready,
            Destroyed,
        }

        private State _state;
        private int _generation;

        public State Current => _state;

        public int Generation => _generation;

        public bool IsDestroyed => _state == State.Destroyed;

        public bool IsReady => _state == State.Ready;

        /// <summary>Empty → Building，并颁发本轮世代。其它状态拒绝。</summary>
        public bool TryBeginBuild(out int generation)
        {
            if (_state != State.Empty)
            {
                generation = _generation;
                return false;
            }

            _state = State.Building;
            generation = ++_generation;
            return true;
        }

        public bool IsCurrentBuild(int generation)
            => _state == State.Building && _generation == generation;

        public bool TryMarkReady(int generation)
        {
            if (!IsCurrentBuild(generation))
            {
                return false;
            }

            _state = State.Ready;
            return true;
        }

        /// <summary>构建抛错时回到 Empty。世代不匹配则不动（已被 TearDown / Destroy 作废）。</summary>
        public void Abandon(int generation)
        {
            if (_generation != generation || _state != State.Building)
            {
                return;
            }

            _state = State.Empty;
        }

        /// <summary>Disable：作废进行中的构建，回到 Empty。</summary>
        public void TearDown()
        {
            if (_state == State.Destroyed)
            {
                return;
            }

            _generation++;
            _state = State.Empty;
        }

        public void Destroy()
        {
            _generation++;
            _state = State.Destroyed;
        }
    }
}
