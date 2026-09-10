namespace Uality.IteTour.Core
{
    /// <summary>
    /// 宿主推入的标记是哪一类（design D6）。
    ///
    /// 包自有的枚举，**不引用宿主的 <c>MarkerPlatform</c>**——包对宿主零知识。
    /// 也不用字符串：拼错不报错，只是永远解析不出，正是本 change 要消灭的静默失败。
    ///
    /// 语义是「payload 的形状」而不是「哪台设备」：将来同一台设备上出现第二种码，
    /// 加的是这里的一个成员，不是一个平台。
    /// </summary>
    public enum MarkerKind
    {
        /// <summary>二维码里的文本原文。当前形如 <c>******{tourId}******</c>，形状会变。</summary>
        QrText,

        /// <summary>AprilTag 的整数 ID 的字符串形式，如 <c>"250"</c>。</summary>
        AprilTagId,
    }
}
