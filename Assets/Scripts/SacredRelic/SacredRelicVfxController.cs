using UnityEngine;
using UnityEngine.VFX;

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Drives our NEW SacredRelic VFX Graph assets (does not modify INab packages).
    /// Expects VisualEffectAsset copied under Assets/SacredRelicDemo/VFX/.
    /// </summary>
    public class SacredRelicVfxController : MonoBehaviour
    {
        [Header("VFX (project-owned assets)")]
        [SerializeField] VisualEffect shellCollapse;

        [Header("Mesh source")]
        [SerializeField] MeshFilter shellMeshFilter;
        [SerializeField] MeshRenderer shellMeshRenderer;

        [Header("Look tuning")]
        [SerializeField] float particlesScale = 0.07f;
        [SerializeField] int particleDensity = 120;
        [SerializeField] float dissolveWidth = 0.12f;
        [SerializeField] float dissolveAdjust = 0.35f;
        [SerializeField] float randomInitialVelocity = 2.8f;
        [SerializeField] Gradient colorOverLife;

        static readonly int DissolveAmountId = Shader.PropertyToID("Dissolve Amount");

        bool _playing;

        void Reset()
        {
            colorOverLife = DefaultDirtToAshGradient();
        }

        void Awake()
        {
            if (colorOverLife == null || colorOverLife.colorKeys == null || colorOverLife.colorKeys.Length == 0)
                colorOverLife = DefaultDirtToAshGradient();
        }

        public void BindShell(MeshFilter filter, MeshRenderer renderer, VisualEffect vfx)
        {
            shellMeshFilter = filter;
            shellMeshRenderer = renderer;
            shellCollapse = vfx;
        }

        public void Play()
        {
            if (shellCollapse == null || shellCollapse.visualEffectAsset == null)
            {
                Debug.LogWarning("[SacredRelic] Shell collapse VFX is missing.", this);
                return;
            }

            ApplyMeshAndTransform();
            ApplyLook();
            SetDissolveAmount(0f);
            shellCollapse.Reinit();
            shellCollapse.SendEvent("OnPlay");
            _playing = true;
        }

        public void SetDissolveAmount(float amount)
        {
            if (shellCollapse == null) return;
            if (shellCollapse.HasFloat(DissolveAmountId))
                shellCollapse.SetFloat(DissolveAmountId, amount);
            else if (shellCollapse.HasFloat("Dissolve Amount"))
                shellCollapse.SetFloat("Dissolve Amount", amount);
        }

        public void StopEmitting()
        {
            if (shellCollapse == null || !_playing) return;
            shellCollapse.SendEvent("OnStop");
        }

        public void HardStop()
        {
            if (shellCollapse == null) return;
            shellCollapse.Stop();
            shellCollapse.Reinit();
            _playing = false;
        }

        void ApplyMeshAndTransform()
        {
            Mesh mesh = null;
            if (shellMeshFilter != null) mesh = shellMeshFilter.sharedMesh;
            if (mesh != null && shellCollapse.HasMesh("Mesh"))
                shellCollapse.SetMesh("Mesh", mesh);

            // Match shell transform so particles spawn on the crust
            var t = shellMeshRenderer != null ? shellMeshRenderer.transform : transform;
            if (shellCollapse.HasVector3("Transform_position"))
                shellCollapse.SetVector3("Transform_position", t.position);
            if (shellCollapse.HasVector3("Transform_angles"))
                shellCollapse.SetVector3("Transform_angles", t.eulerAngles);
            if (shellCollapse.HasVector3("Transform_scale"))
                shellCollapse.SetVector3("Transform_scale", t.lossyScale);

            // Some graphs use a Transform space directly via component transform
            shellCollapse.transform.SetPositionAndRotation(t.position, t.rotation);
            shellCollapse.transform.localScale = Vector3.one;
        }

        void ApplyLook()
        {
            if (shellCollapse.HasFloat("Particles Scale"))
                shellCollapse.SetFloat("Particles Scale", particlesScale);
            if (shellCollapse.HasInt("Particle Density"))
                shellCollapse.SetInt("Particle Density", particleDensity);
            if (shellCollapse.HasFloat("Width"))
                shellCollapse.SetFloat("Width", dissolveWidth);
            if (shellCollapse.HasFloat("Dissolve Adjust"))
                shellCollapse.SetFloat("Dissolve Adjust", dissolveAdjust);
            if (shellCollapse.HasFloat("Random Initial Velocity"))
                shellCollapse.SetFloat("Random Initial Velocity", randomInitialVelocity);
            if (shellCollapse.HasGradient("Color Over Life") && colorOverLife != null)
                shellCollapse.SetGradient("Color Over Life", colorOverLife);
        }

        static Gradient DefaultDirtToAshGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.35f, 0.22f, 0.10f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.35f, 0.15f), 0.35f),
                    new GradientColorKey(new Color(0.55f, 0.55f, 0.58f), 0.75f),
                    new GradientColorKey(new Color(0.4f, 0.4f, 0.42f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.05f),
                    new GradientAlphaKey(0.85f, 0.45f),
                    new GradientAlphaKey(0.35f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            return g;
        }
    }
}
