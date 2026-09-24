using System;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 扫码提示对外的门禁（marker-rescan D8）：加载完成前不对外发提示。那时 ITE 还不收扫码
    /// （ite-guide-state-machine D9），发出去的「请扫码」是假的——宿主据此放行，码判稳后被丢弃，
    /// 加载完成时提示又没变、不会再放行，用户盯着码却扫不上。
    ///
    /// 打开时补发一次最近的提示（不是隐藏时），之后原样转发。这样「提示可见 ⇔ 收扫码」在包的公开面上成立。
    /// </summary>
    public sealed class ScanPromptGate
    {
        private ScanPrompt _latest = ScanPrompt.Hidden;

        public bool IsOpen { get; private set; }

        /// <summary>对外的当前提示：打开前一律隐藏。</summary>
        public ScanPrompt Current => IsOpen ? _latest : ScanPrompt.Hidden;

        public event Action<ScanPrompt> Changed;

        /// <summary>内部算出的新提示（已去重）。打开前只记下，不转发。</summary>
        public void Offer(ScanPrompt prompt)
        {
            _latest = prompt;

            if (IsOpen)
            {
                Changed?.Invoke(prompt);
            }
        }

        /// <summary>加载完成时调用一次。对外一直是隐藏，最近的提示也是隐藏时没有变化，不广播。</summary>
        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            IsOpen = true;

            if (_latest.State != ScanPromptState.Hidden)
            {
                Changed?.Invoke(_latest);
            }
        }
    }
}
