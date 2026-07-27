using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;
using TMPro;
using MRBase.SacredRelic;

namespace MRBase.SacredRelic.Editor
{
    /// <summary>
    /// Builds Demo using NEW project-owned VFX Graph under SacredRelicDemo/VFX
    /// (copied from INab template once — originals are never modified).
    /// Menu: Tools/Sacred Relic/Build Demo Scene
    /// </summary>
    public static class SacredRelicDemoBuilder
    {
        const string RootFolder = "Assets/SacredRelicDemo";
        const string VfxFolder = RootFolder + "/VFX";
        const string PrefabPath = RootFolder + "/SacredRelicTablet.prefab";
        const string ScenePath = RootFolder + "/SacredRelicAwakenDemo.unity";
        const string MatShellPath = RootFolder + "/M_Shell_Radial.mat";
        const string MatTabletPath = RootFolder + "/M_Tablet_Stone.mat";
        const string TexNoisePath = RootFolder + "/T_CrackNoise.png";
        const string OurVfxPath = VfxFolder + "/SacredRelic_ShellCollapse.vfx";
        const string TemplateVfx =
            "Assets/INab Studio/Dissolve-FX-MasterKit/Vfx Graph Effects/Core/Standard/1 Template/Standard Dissolve Template.vfx";
        const string NoiseSource =
            "Assets/INab Studio/Common/Textures/Noises/Smooth/Noise_7.png";

        static readonly Color SacredGold = new Color(1f, 0.72f, 0.28f, 1f);
        static readonly Color SacredGoldHdr = new Color(10f, 5.5f, 1.1f, 1f);
        static readonly Color ShellDirt = new Color(0.16f, 0.12f, 0.09f, 1f);
        static readonly Color StoneFaded = new Color(0.34f, 0.32f, 0.28f, 1f);

        [MenuItem("Tools/Sacred Relic/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(VfxFolder);

            EnsureOurVfxAsset();
            EnsureCrackNoiseTexture();

            DeleteIfExists(MatShellPath);
            DeleteIfExists(MatTabletPath);
            var shellMat = CreateShellMaterial();
            var tabletMat = CreateTabletMaterial();

            var root = BuildPrefabHierarchy(shellMat, tabletMat);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var light = Object.FindAnyObjectByType<Light>();
            if (light != null)
            {
                light.color = new Color(1f, 0.9f, 0.78f);
                light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.1f, 0.09f, 0.08f);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 1.15f, -2.05f);
                cam.transform.LookAt(new Vector3(0f, 1f, 0f));
                cam.backgroundColor = new Color(0.04f, 0.035f, 0.03f);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            instance.transform.position = new Vector3(0f, 1f, 0f);

            CreateHint();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = instance;
            EditorSceneManager.OpenScene(ScenePath);

            Debug.Log(
                "[SacredRelic] Demo rebuilt with NEW VFX Graph: " + OurVfxPath +
                "\n(INab originals untouched). Play → Space/Click awaken | R reset.");
        }

        [MenuItem("Tools/Sacred Relic/Ensure VFX Asset Only")]
        public static void EnsureOurVfxAssetMenu()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(VfxFolder);
            EnsureOurVfxAsset();
            Debug.Log("[SacredRelic] VFX ready at " + OurVfxPath);
        }

        static void EnsureOurVfxAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(OurVfxPath) != null)
                return;

            if (!AssetDatabase.CopyAsset(TemplateVfx, OurVfxPath))
                throw new System.Exception("Failed to copy VFX template to " + OurVfxPath);

            AssetDatabase.ImportAsset(OurVfxPath);
            // Rename display name inside asset for clarity (file already SacredRelic_ShellCollapse)
            Debug.Log("[SacredRelic] Created new VFX (copy): " + OurVfxPath);
        }

        static void DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        static void EnsureCrackNoiseTexture()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexNoisePath) != null) return;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseSource) != null)
                AssetDatabase.CopyAsset(NoiseSource, TexNoisePath);
        }

        static Material CreateShellMaterial()
        {
            var shader = Shader.Find("MRBase/SacredRelic/ShellRadialDissolve")
                         ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = "M_Shell_Radial" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ShellDirt);
            if (mat.HasProperty("_EdgeColor")) mat.SetColor("_EdgeColor", SacredGoldHdr);
            if (mat.HasProperty("_DissolveAmount")) mat.SetFloat("_DissolveAmount", 0f);
            if (mat.HasProperty("_EdgeWidth")) mat.SetFloat("_EdgeWidth", 0.14f);
            if (mat.HasProperty("_NoiseScale")) mat.SetFloat("_NoiseScale", 2.8f);
            if (mat.HasProperty("_NoiseStrength")) mat.SetFloat("_NoiseStrength", 0.2f);
            if (mat.HasProperty("_RadialScale")) mat.SetFloat("_RadialScale", 1.55f);
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(TexNoisePath)
                        ?? AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseSource);
            if (noise != null && mat.HasProperty("_NoiseTex"))
                mat.SetTexture("_NoiseTex", noise);
            AssetDatabase.CreateAsset(mat, MatShellPath);
            return mat;
        }

        static Material CreateTabletMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "M_Tablet_Stone" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", StoneFaded);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(mat, MatTabletPath);
            return mat;
        }

        static GameObject BuildPrefabHierarchy(Material shellMat, Material tabletMat)
        {
            var vfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(OurVfxPath);
            if (vfxAsset == null)
                throw new System.Exception("Missing " + OurVfxPath + " — run Ensure VFX Asset first.");

            var root = new GameObject("SacredRelicTablet");
            var awaken = root.AddComponent<SacredRelicAwaken>();
            var vfxCtrl = root.AddComponent<SacredRelicVfxController>();
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.9f, 1.2f, 0.22f);
            root.AddComponent<SacredRelicTouchProxy>();

            var tablet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tablet.name = "Tablet";
            tablet.transform.SetParent(root.transform, false);
            tablet.transform.localScale = new Vector3(0.78f, 1.08f, 0.09f);
            Object.DestroyImmediate(tablet.GetComponent<BoxCollider>());
            tablet.GetComponent<MeshRenderer>().sharedMaterial = tabletMat;

            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "Shell";
            shell.transform.SetParent(root.transform, false);
            shell.transform.localScale = new Vector3(0.86f, 1.16f, 0.14f);
            Object.DestroyImmediate(shell.GetComponent<BoxCollider>());
            var shellRend = shell.GetComponent<MeshRenderer>();
            shellRend.sharedMaterial = shellMat;
            var shellFilter = shell.GetComponent<MeshFilter>();

            // Inscription muted for now
            var textGo = new GameObject("Inscription");
            textGo.transform.SetParent(tablet.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.05f, -0.55f);
            textGo.transform.localScale = new Vector3(1f / 0.78f, 1f / 1.08f, 1f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "·";
            tmp.fontSize = 2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.35f, 0.32f, 0.28f, 0.25f);
            tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.6f);

            // NEW Visual Effect instance (our asset)
            var vfxGo = new GameObject("VFX_ShellCollapse");
            vfxGo.transform.SetParent(root.transform, false);
            var ve = vfxGo.AddComponent<VisualEffect>();
            ve.visualEffectAsset = vfxAsset;
            ve.Stop();

            vfxCtrl.BindShell(shellFilter, shellRend, ve);

            var dissolveCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.18f, 0.05f),
                new Keyframe(0.45f, 0.4f),
                new Keyframe(0.75f, 0.85f),
                new Keyframe(1f, 1.2f));

            var so = new SerializedObject(awaken);
            so.FindProperty("shellRenderer").objectReferenceValue = shellRend;
            so.FindProperty("tabletRenderer").objectReferenceValue = tablet.GetComponent<MeshRenderer>();
            so.FindProperty("inscriptionText").objectReferenceValue = tmp;
            so.FindProperty("vfxController").objectReferenceValue = vfxCtrl;
            so.FindProperty("chunkParticles").objectReferenceValue = null;
            so.FindProperty("ashParticles").objectReferenceValue = null;
            so.FindProperty("dissolver").objectReferenceValue = null;
            so.FindProperty("duration").floatValue = 6.5f;
            so.FindProperty("restoreStart").floatValue = 0.55f;
            so.FindProperty("sacredGold").colorValue = SacredGold;
            so.FindProperty("goldEdgeIntensity").floatValue = 8f;
            so.FindProperty("dissolveCurve").animationCurveValue = dissolveCurve;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Tune VFX controller defaults for dirt→ash read
            var vso = new SerializedObject(vfxCtrl);
            vso.FindProperty("shellCollapse").objectReferenceValue = ve;
            vso.FindProperty("shellMeshFilter").objectReferenceValue = shellFilter;
            vso.FindProperty("shellMeshRenderer").objectReferenceValue = shellRend;
            vso.FindProperty("particlesScale").floatValue = 0.085f;
            vso.FindProperty("particleDensity").intValue = 140;
            vso.FindProperty("dissolveWidth").floatValue = 0.14f;
            vso.FindProperty("randomInitialVelocity").floatValue = 3.2f;
            vso.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        static void CreateHint()
        {
            var canvasGo = new GameObject("DemoHint");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var textGo = new GameObject("HintText");
            textGo.transform.SetParent(canvasGo.transform, false);
            var ugui = textGo.AddComponent<UnityEngine.UI.Text>();
            ugui.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            ugui.text = "VFX Graph ShellCollapse — Space/Click awaken | R reset";
            ugui.fontSize = 18;
            ugui.alignment = TextAnchor.LowerCenter;
            ugui.color = new Color(1f, 0.9f, 0.7f, 0.85f);
            var rt = ugui.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0.08f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
