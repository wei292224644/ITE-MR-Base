using UnityEngine;

public static class PoseMath
{
    public static Pose Compose(Pose parent, Pose localOffset)
    {
        Vector3 position = parent.position + parent.rotation * localOffset.position;
        Quaternion rotation = parent.rotation * localOffset.rotation;
        return new Pose(position, rotation);
    }
}
