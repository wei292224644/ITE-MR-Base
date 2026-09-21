using NUnit.Framework;
using UnityEngine;
using MRBase.Ite.Host;

namespace MRBase.Ite.Host.Tests
{
    public class EditorFlyMotionTests
    {
        [Test]
        public void Apply_ForwardInput_MovesAlongLookDirection()
        {
            var go = new GameObject("cam");
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward));
                float pitch = 0f;

                EditorFlyMotion.Apply(
                    go.transform,
                    move: new Vector3(0f, 0f, 1f),
                    lookDelta: Vector2.zero,
                    moveSpeed: 10f,
                    lookSensitivity: 1f,
                    deltaTime: 1f,
                    pitch: ref pitch);

                Assert.AreEqual(new Vector3(0f, 0f, 10f), go.transform.position);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Apply_LookYaw_RotatesAroundWorldUp()
        {
            var go = new GameObject("cam");
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                float pitch = 0f;

                EditorFlyMotion.Apply(
                    go.transform,
                    move: Vector3.zero,
                    lookDelta: new Vector2(90f, 0f),
                    moveSpeed: 0f,
                    lookSensitivity: 1f,
                    deltaTime: 0f,
                    pitch: ref pitch);

                Assert.AreEqual(90f, go.transform.eulerAngles.y, 0.5f);
                Assert.Less((go.transform.forward - Vector3.right).sqrMagnitude, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Apply_Pitch_ClampsAndDoesNotRoll()
        {
            var go = new GameObject("cam");
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                float pitch = 0f;

                EditorFlyMotion.Apply(
                    go.transform,
                    move: Vector3.zero,
                    lookDelta: new Vector2(0f, 200f),
                    moveSpeed: 0f,
                    lookSensitivity: 1f,
                    deltaTime: 0f,
                    pitch: ref pitch);

                Assert.AreEqual(-89f, pitch);
                Assert.AreEqual(0f, go.transform.eulerAngles.z, 0.5f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
