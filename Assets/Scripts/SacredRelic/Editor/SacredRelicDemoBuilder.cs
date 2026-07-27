using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using MRBase.SacredRelic;

namespace MRBase.SacredRelic.Editor
{
    /// <summary>
    /// One-click Demo scaffold: Prefab + scene for sacred relic awaken.
    /// Menu: Tools/Sacred Relic/Build Demo Scene
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

        [MenuItem("Tools/Sacred Relic/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            EnsureFolder(RootFolder);

            var shellMat = CreateOrLoadShellMaterial();
            var tabletMat = CreateOrLoadTabletMaterial();

            var root = BuildPrefabHierarchy(shellMat, tabletMat);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Frame camera
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 1.2f, -2.2f);
                cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            instance.transform.position = new Vector3(0f, 1.0f, 0f);

            // Hint canvas
            CreateHint();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = instance;
            Debug.Log(
                "[SacredRelic] Demo ready: " + ScenePath +
                "\nPlay → Space/Click = awaken, R = reset.");
        }

        static Material CreateOrLoadShellMaterial()
        {
            var burn = AssetDatabase.LoadAssetAtPath<Material>(BurnSource);
            if (burn != null)
            {
                if (!AssetDatabase.CopyAsset(BurnSource, MatShellPath))
                {
                    // already exists
                }
                AssetDatabase.ImportAsset(MatShellPath);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(MatShellPath);
                if (mat != null)
                {
                    var gold = new Color(1f, 0.72f, 0.28f) * 4f;
                    if (mat.HasProperty("_DissolveAmount")) mat.SetFloat("_DissolveAmount", 0f);
                    if (mat.HasProperty("_DissolveColor")) mat.SetColor("_DissolveColor", gold);
                    if (mat.HasProperty("_Burn_Color")) mat.SetColor("_Burn_Color", gold);
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", new Color(0.25f, 0.2f, 0.15f, 1f));
                    EditorUtility.SetDirty(mat);
                    return mat;
                }
            }

            // Fallback URP Lit
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            var fallback = new Material(shader) { name = "M_Shell_Sealed" };
            if (fallback.HasProperty("_BaseColor"))
                fallback.SetColor("_BaseColor", new Color(0.22f, 0.18f, 0.14f, 1f));
            AssetDatabase.CreateAsset(fallback, MatShellPath);
            return fallback;
        }

        static Material CreateOrLoadTabletMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MatTabletPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "M_Tablet_Stone" };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.35f, 0.33f, 0.30f, 1f));
            AssetDatabase.CreateAsset(mat, MatTabletPath);
            return mat;
        }

        static GameObject BuildPrefabHierarchy(Material shellMat, Material tabletMat)
        {
            var root = new GameObject("SacredRelicTablet");
            var awaken = root.AddComponent<SacredRelicAwaken>();
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.85f, 1.15f, 0.18f);
            root.AddComponent<SacredRelicTouchProxy>();

            // Tablet body
            var tablet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tablet.name = "Tablet";
            tablet.transform.SetParent(root.transform, false);
            tablet.transform.localScale = new Vector3(0.75f, 1.05f, 0.08f);
            Object.DestroyImmediate(tablet.GetComponent<BoxCollider>());
            var tabletRend = tablet.GetComponent<MeshRenderer>();
            tabletRend.sharedMaterial = tabletMat;

            // Shell (slightly larger)
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "Shell";
            shell.transform.SetParent(root.transform, false);
            shell.transform.localScale = new Vector3(0.82f, 1.12f, 0.12f);
            Object.DestroyImmediate(shell.GetComponent<BoxCollider>());
            var shellRend = shell.GetComponent<MeshRenderer>();
            shellRend.sharedMaterial = shellMat;

            // Inscription TMP
            var textGo = new GameObject("Inscription");
            textGo.transform.SetParent(tablet.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.52f);
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = new Vector3(1f / 0.75f, 1f / 1.05f, 1f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "SEALED DUST\nTOUCH THE LIGHT";
            tmp.fontSize = 3.2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.45f, 0.42f, 0.38f, 1f);
            tmp.enableWordWrapping = true;
            var rect = tmp.rectTransform;
            rect.sizeDelta = new Vector2(0.65f, 0.9f);

            // Particles
            var chunk = CreateChunkParticles(root.transform);
            var ash = CreateAshParticles(root.transform);

            // Wire SacredRelicAwaken via SerializedObject
            var so = new SerializedObject(awaken);
            so.FindProperty("shellRenderer").objectReferenceValue = shellRend;
            so.FindProperty("tabletRenderer").objectReferenceValue = tabletRend;
            so.FindProperty("inscriptionText").objectReferenceValue = tmp;
            so.FindProperty("chunkParticles").objectReferenceValue = chunk;
            so.FindProperty("ashParticles").objectReferenceValue = ash;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Optional Dissolver on shell
            var dissolverType = System.Type.GetType("INab.Dissolve.Dissolver, Assembly-CSharp");
            if (dissolverType == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    dissolverType = asm.GetType("INab.Dissolve.Dissolver");
                    if (dissolverType != null) break;
                }
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
                    dso.ApplyModifiedPropertiesWithoutUndo();
                }
                var duration = dso.FindProperty("duration");
                if (duration != null) { duration.floatValue = 2.0f; dso.ApplyModifiedPropertiesWithoutUndo(); }
            }

            return root;
        }

        static ParticleSystem CreateChunkParticles(Transform parent)
        {
            var go = new GameObject("ChunkDebris");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.4f;
            main.startLifetime = 1.2f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new Color(0.35f, 0.28f, 0.2f, 1f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18, 28) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.7f, 1.0f, 0.1f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, -0.6f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static ParticleSystem CreateAshParticles(Transform parent)
        {
            var go = new GameObject("FineAsh");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.8f;
            main.startLifetime = 1.6f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.035f);
            main.startColor = new Color(0.55f, 0.55f, 0.58f, 0.7f);
            main.gravityModifier = -0.05f; // slight lift
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.15f, 25, 40),
                new ParticleSystem.Burst(0.5f, 15, 25)
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.75f, 1.05f, 0.12f);

            return ps;
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
            ugui.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (ugui.font == null)
                ugui.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            ugui.text = "Sacred Relic Demo — Space/Click: Awaken | R: Reset";
            ugui.fontSize = 22;
            ugui.alignment = TextAnchor.LowerCenter;
            ugui.color = Color.white;
            var rt = ugui.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0.12f);
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
