using UnityEngine;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 编辑器验收用走位：WASD 平移 + 鼠标视角。无重力、碰撞、蹲跳。
    /// Escape 切换鼠标锁定。
    /// </summary>
    [AddComponentMenu("ITE/Editor Fly")]
    public sealed class IteEditorFly : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 4f;
        [SerializeField] float lookSensitivity = 2f;

        private float _pitch;

        private void Start()
        {
            var x = transform.eulerAngles.x;
            _pitch = x > 180f ? x - 360f : x;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                var locked = Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !locked;
            }

            var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            var look = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"))
                : Vector2.zero;

            EditorFlyMotion.Apply(
                transform, move, look, moveSpeed, lookSensitivity, Time.deltaTime, ref _pitch);
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
