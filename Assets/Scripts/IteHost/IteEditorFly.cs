using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

        /// <summary>
        /// Input System 的 <c>Mouse.delta</c> 是像素；旧 Input 的 Mouse X/Y 已做过缩放。
        /// 乘这个系数后，现有 <see cref="lookSensitivity"/> 手感大致对齐。
        /// </summary>
        const float MouseDeltaToLook = 0.05f;

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
            ReadEditorInput(out var toggleCursor, out var move, out var look);

            if (toggleCursor)
            {
                var locked = Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !locked;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                look = Vector2.zero;
            }

            EditorFlyMotion.Apply(
                transform, move, look, moveSpeed, lookSensitivity, Time.deltaTime, ref _pitch);
        }

        static void ReadEditorInput(out bool toggleCursor, out Vector3 move, out Vector2 look)
        {
#if ENABLE_INPUT_SYSTEM
            toggleCursor = false;
            move = Vector3.zero;
            look = Vector2.zero;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                toggleCursor = keyboard.escapeKey.wasPressedThisFrame;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                {
                    move.x -= 1f;
                }

                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                {
                    move.x += 1f;
                }

                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                {
                    move.z -= 1f;
                }

                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                {
                    move.z += 1f;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                look = mouse.delta.ReadValue() * MouseDeltaToLook;
            }
#else
            toggleCursor = Input.GetKeyDown(KeyCode.Escape);
            move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
