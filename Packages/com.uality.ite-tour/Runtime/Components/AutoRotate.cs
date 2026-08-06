using UnityEngine;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 匀速自转。富文本的音频加载指示器用它。
    ///
    /// 源工程里这是宿主的通用小工具 <c>Assets/Scripts/Utils/AutoRotate.cs</c>（全局命名空间）。
    /// 只有 ITE 的 prefab 在用，随 prefab 一起搬进包。
    /// </summary>
    public class AutoRotate : MonoBehaviour
    {
        [Tooltip("角速度，度/秒")]
        public float speed = 10f;

        public Vector3 rotationAxis = Vector3.up;

        [Tooltip("绕自身坐标系还是世界坐标系")]
        public bool isRotateWithLocal = true;

        private void LateUpdate()
            => transform.Rotate(rotationAxis, speed * Time.deltaTime,
                isRotateWithLocal ? Space.Self : Space.World);

        // 停用时归位，下次启用不带着上次的角度
        private void OnDisable() => transform.localRotation = Quaternion.identity;
    }
}
