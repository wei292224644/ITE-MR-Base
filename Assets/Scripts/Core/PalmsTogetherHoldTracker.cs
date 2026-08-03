using System;

/// <summary>
/// 合十 hold / 迟滞边沿状态机：raw 连续满足 holdSeconds 后 Performed，之后 raw 失败则 Released。
/// 纯逻辑，可离机测。
/// </summary>
public sealed class PalmsTogetherHoldTracker
{
    float heldFor;

    public bool IsHeld { get; private set; }

    /// <summary>
    /// 推进一帧。返回本帧是否触发了 Performed（true）或 Released（false 且 was held）；
    /// 无边沿时 <paramref name="edge"/> 为 null。
    /// </summary>
    public void Tick(bool rawMatch, float holdSeconds, float deltaTime,
                     out bool? performedOrReleased)
    {
        performedOrReleased = null;

        if (!rawMatch)
        {
            heldFor = 0f;
            if (IsHeld)
            {
                IsHeld = false;
                performedOrReleased = false;
            }
            return;
        }

        if (IsHeld)
            return;

        heldFor += deltaTime;
        if (heldFor < holdSeconds)
            return;

        IsHeld = true;
        performedOrReleased = true;
    }

    public void Reset()
    {
        heldFor = 0f;
        IsHeld = false;
    }
}
