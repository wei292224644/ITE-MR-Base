using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MRBase.SacredRelic;

namespace MRBase.SacredRelic.Editor
{
    /// <summary>
    /// One-click Demo scaffold. Menu: Tools/Sacred Relic/Build Demo Scene
    /// </summary>
    public static class SacredRelicDemoBuilder
    {
        const string RootFolder = "Assets/SacredRelicDemo";
        const string PrefabPath = RootFolder + "/SacredRelicTablet.prefab";
        const string ScenePath = RootFolder + "/SacredRelicAwakenDemo.unity";
        const string MatShellPath = RootFolder + "/M_Shell_Sealed.mat";
        const string MatTabletPath = RootFolder + "/M_Tablet_Stone.mat";
        const string BurnSource =
            "Assets/INab Studio/Dissolve-FX/Core URP/Dissolve Materials/Learn/Burn Dissolve/Statue 1.mat";

        // Sacred honey gold (HDR-ish for dissolve edge)
        static readonly Color SacredGold = new Color(1.0f, 0.70f, 0.25f, 1f);
        static readonly Color SacredGoldHdr = new Color(8f, 4.2f, 0.9f, 1f);
        static readonly Color ShellDirt = new Color(0.18f, 0.14f, 0.10f, 1f);
        static readonly Color StoneFaded = new Color(0.32f, 0.30f, 0.27f, 1f);
        static readonly Color InscriptionFaded = new Color(0.40f, 0.38f, 0.34f, 1f);
        static readonly Color InscriptionVermillion = new Color(0.85f, 0.12f, 0.08f, 1f);

        [MenuItem("Tools/Sacred Relic/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            EnsureFolder(RootFolder);

            // Force refresh materials so gold/dirt tuning always applies
            DeleteIfExists(MatShellPath);
            DeleteIfExists(MatTabletPath);

            var shellMat = CreateShellMaterial();
            var tabletMat = CreateTabletMaterial();

            var root = BuildPrefabHierarchy(shellMat, tabletMat);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Mood lighting
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.color = new Color(1f, 0.92f, 0.82f);
                light.intensity = 1.15f;
                light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.12f, 0.11f, 0.10f);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 1.15f, -2.0f);
                cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
                cam.backgroundColor = new Color(0.05f, 0.045f, 0.04f);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            instance.transform.position = new Vector3(0f, 1.0f, 0f);

            CreateHint();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = instance;
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[SacredRelic] Demo rebuilt. Play → Space/Click awaken | R reset.");
        }

        static void DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        static Material CreateShellMaterial()
        {
            var burn = AssetDatabase.LoadAssetAtPath<Material>(BurnSource);
            if (burn != null)
            {
                AssetDatabase.CopyAsset(BurnSource, MatShellPath);
                AssetDatabase.ImportAsset(MatShellPath);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(MatShellPath);
                if (mat != null)
                {
                    mat.EnableKeyword("_USE_DISSOLVE");
                    if (mat.HasProperty("_DissolveAmount")) mat.SetFloat("_DissolveAmount", 0f);
                    if (mat.HasProperty("_DissolveColor")) mat.SetColor("_DissolveColor", SacredGoldHdr);
                    if (mat.HasProperty("_Burn_Color")) mat.SetColor("_Burn_Color", SacredGoldHdr);
                    if (mat.HasProperty("_Burn_Width")) mat.SetFloat("_Burn_Width", 0.32f);
                    if (mat.HasProperty("_Burn_Hardness")) mat.SetFloat("_Burn_Hardness", 0.12f);
                    if (mat.HasProperty("_EdgeWidth")) mat.SetFloat("_EdgeWidth", 0.02f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ShellDirt);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", ShellDirt);
                    EditorUtility.SetDirty(mat);
                    return mat;
                }
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var fallback = new Material(shader) { name = "M_Shell_Sealed" };
            if (fallback.HasProperty("_BaseColor")) fallback.SetColor("_BaseColor", ShellDirt);
            // Transparent so alpha fade works as dissolve fallback
            if (fallback.HasProperty("_Surface")) fallback.SetFloat("_Surface", 1f);
            fallback.SetOverrideTag("RenderType", "Transparent");
            fallback.renderQueue = 3000;
            AssetDatabase.CreateAsset(fallback, MatShellPath);
            return fallback;
        }

        static Material CreateTabletMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "M_Tablet_Stone" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", StoneFaded);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.18f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, MatTabletPath);
            return mat;
        }

        static GameObject BuildPrefabHierarchy(Material shellMat, Material tabletMat)
        {
            var root = new GameObject("SacredRelicTablet");
            var awaken = root.AddComponent<SacredRelicAwaken>();
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.9f, 1.2f, 0.22f);
            root.AddComponent<SacredRelicTouchProxy>();

            // Tablet
            var tablet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tablet.name = "Tablet";
            tablet.transform.SetParent(root.transform, false);
            tablet.transform.localScale = new Vector3(0.78f, 1.08f, 0.09f);
            Object.DestroyImmediate(tablet.GetComponent<BoxCollider>());
            var tabletRend = tablet.GetComponent<MeshRenderer>();
            tabletRend.sharedMaterial = tabletMat;

            // Shell slightly larger, darker crust
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "Shell";
            shell.transform.SetParent(root.transform, false);
            shell.transform.localScale = new Vector3(0.86f, 1.16f, 0.14f);
            Object.DestroyImmediate(shell.GetComponent<BoxCollider>());
            var shellRend = shell.GetComponent<MeshRenderer>();
            shellRend.sharedMaterial = shellMat;

            // Inscription — world-space TMP on front face
            var textGo = new GameObject("Inscription");
            textGo.transform.SetParent(tablet.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.05f, -0.55f);
            textGo.transform.localScale = new Vector3(1f / 0.78f, 1f / 1.08f, 1f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "SEALED\nDUST";
            tmp.fontSize = 5.5f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = InscriptionFaded;
            tmp.enableWordWrapping = true;
            tmp.outlineWidth = 0.12f;
            tmp.outlineColor = new Color(0.05f, 0.04f, 0.03f, 0.8f);
            tmp.rectTransform.sizeDelta = new Vector2(0.7f, 0.85f);

            var chunk = CreateChunkParticles(root.transform);
            var ash = CreateAshParticles(root.transform);

            var so = new SerializedObject(awaken);
            so.FindProperty("shellRenderer").objectReferenceValue = shellRend;
            so.FindProperty("tabletRenderer").objectReferenceValue = tabletRend;
            so.FindProperty("inscriptionText").objectReferenceValue = tmp;
            so.FindProperty("chunkParticles").objectReferenceValue = chunk;
            so.FindProperty("ashParticles").objectReferenceValue = ash;
            so.FindProperty("sacredGold").colorValue = SacredGold;
            so.FindProperty("goldEdgeIntensity").floatValue = 6f;
            so.FindProperty("fadedInscription").colorValue = InscriptionFaded;
            so.FindProperty("restoredInscription").colorValue = InscriptionVermillion;
            so.FindProperty("fadedStone").colorValue = StoneFaded;
            so.FindProperty("restoredStone").colorValue = new Color(0.72f, 0.66f, 0.55f, 1f);
            so.FindProperty("duration").floatValue = 2.4f;
            so.FindProperty("restoreStart").floatValue = 0.4f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Optional INab Dissolver
            System.Type dissolverType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                dissolverType = asm.GetType("INab.Dissolve.Dissolver");
                if (dissolverType != null) break;
            }
            if (dissolverType != null)
            {
                var dissolver = shell.AddComponent(dissolverType);
                so = new SerializedObject(awaken);
                so.FindProperty("dissolver").objectReferenceValue = dissolver;
                so.ApplyModifiedPropertiesWithoutUndo();

                var dso = new SerializedObject(dissolver);
                var mats = dso.FindProperty("materials");
                if (mats != null)
                {
                    mats.arraySize = 1;
                    mats.GetArrayElementAtIndex(0).objectReferenceValue = shellMat;
                }
                var duration = dso.FindProperty("duration");
                if (duration != null) duration.floatValue = 2.2f;
                var initial = dso.FindProperty("initialState");
                // Materialized = 1 in their enum (shell visible / not dissolved)
                if (initial != null) initial.enumValueIndex = 1;
                dso.ApplyModifiedPropertiesWithoutUndo();
            }

            return root;
        }

        static ParticleSystem CreateChunkParticles(Transform parent)
        {
            var go = new GameObject("ChunkDebris");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new Color(0.28f, 0.20f, 0.12f, 1f);
            main.gravityModifier = 0.55f;
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 22, 32) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.75f, 1.05f, 0.12f);
            // Push outward from front face
            shape.position = new Vector3(0f, 0f, -0.08f);

            // Avoid velocityOverLifetime mode mismatches — use force instead
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.Local;
            force.z = -0.8f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.35f, 0.25f, 0.15f), 0f),
                    new GradientColorKey(new Color(0.2f, 0.16f, 0.12f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            AssignDefaultParticleMaterial(renderer);
            return ps;
        }

        static ParticleSystem CreateAshParticles(Transform parent)
        {
            var go = new GameObject("FineAsh");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 2.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
            main.startColor = new Color(0.62f, 0.62f, 0.66f, 0.75f);
            main.gravityModifier = -0.08f; // slight lift
            main.maxParticles = 100;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.2f, 30, 45),
                new ParticleSystem.Burst(0.55f, 20, 30)
            });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.8f, 1.1f, 0.14f);

            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.y = 0.25f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.7f, 0.7f, 0.72f), 0f),
                    new GradientColorKey(new Color(0.45f, 0.45f, 0.48f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.8f, 0f),
                    new GradientAlphaKey(0.4f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            AssignDefaultParticleMaterial(renderer);
            return ps;
        }

        static void AssignDefaultParticleMaterial(ParticleSystemRenderer renderer)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader != null)
                renderer.sharedMaterial = new Material(shader);
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
            ugui.text = "Sacred Relic Demo — Space / Click: Awaken   |   R: Reset";
            ugui.fontSize = 20;
            ugui.alignment = TextAnchor.LowerCenter;
            ugui.color = new Color(1f, 0.92f, 0.75f, 0.9f);
            var rt = ugui.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0.1f);
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
