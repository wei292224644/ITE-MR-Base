using UnityEngine;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 假扫码位姿与 payload：贴在相机前方、正对观察者（design D6）。
    /// </summary>
    public static class EditorFakeScan
    {
        public const float DefaultDistance = 1.5f;

        public static Pose PoseInFront(
            Vector3 cameraPosition,
            Quaternion cameraRotation,
            float distance = DefaultDistance)
        {
            var forward = cameraRotation * Vector3.forward;
            var position = cameraPosition + forward * distance;
            var rotation = Quaternion.LookRotation(-forward, cameraRotation * Vector3.up);
            return new Pose(position, rotation);
        }

        public static string WrapPayload(string tourId) => "******" + tourId + "******";
    }
}
