using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MRBase.SacredRelic;

namespace MRBase.SacredRelic.Editor
{
    /// <summary>
    /// Rebuild Demo: radial center-out shell dissolve + non-pink particles.
    /// Menu: Tools/Sacred Relic/Build Demo Scene
    /// </summary>
    public static class SacredRelicDemoBuilder
    {
        const string RootFolder = "Assets/SacredRelicDemo";
        const string PrefabPath = RootFolder + "/SacredRelicTablet.prefab";
        const string ScenePath = RootFolder + "/SacredRelicAwakenDemo.unity";
        const string MatShellPath = RootFolder + "/M_Shell_Radial.mat";
        const string MatTabletPath = RootFolder + "/M_Tablet_Stone.mat";
        const string MatChunkPath = RootFolder + "/M_Particle_Chunk.mat";
        const string MatAshPath = RootFolder + "/M_Particle_Ash.mat";
        const string TexRadialPath = RootFolder + "/T_CrackNoise.png";
        const string ParticleTex =
            "Assets/INab Studio/Dissolve-FX-MasterKit/Textures/Particles/Particle_3.png";
        const string NoiseSource =
            "Assets/INab Studio/Common/Textures/Noises/Smooth/Noise_7.png";

        static readonly Color SacredGold = new Color(1f, 0.72f, 0.28f, 1f);
        static readonly Color SacredGoldHdr = new Color(10f, 5.5f, 1.1f, 1f);
        static readonly Color ShellDirt = new Color(0.16f, 0.12f, 0.09f, 1f);
        static readonly Color StoneFaded = new Color(0.34f, 0.32f, 0.28f, 1f);
        static readonly Color ChunkBrown = new Color(0.32f, 0.22f, 0.12f, 1f);
        static readonly Color AshGrey = new Color(0.55f, 0.55f, 0.58f, 0.85f);

        [MenuItem("Tools/Sacred Relic/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            EnsureFolder(RootFolder);

            DeleteIfExists(MatShellPath);
            DeleteIfExists(MatTabletPath);
            DeleteIfExists(MatChunkPath);
            DeleteIfExists(MatAshPath);

            EnsureCrackNoiseTexture();
            var shellMat = CreateShellMaterial();
            var tabletMat = CreateTabletMaterial();
            var chunkMat = CreateParticleMaterial(MatChunkPath, ChunkBrown, false);
            var ashMat = CreateParticleMaterial(MatAshPath, AshGrey, true);

            var root = BuildPrefabHierarchy(shellMat, tabletMat, chunkMat, ashMat);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var light = Object.FindFirstObjectByType<Light>();
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
                cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
                cam.backgroundColor = new Color(0.04f, 0.035f, 0.03f);
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
            Debug.Log("[SacredRelic] Rebuilt: center-out radial dissolve ~5s, dirt/ash particles. Space/Click awaken, R reset.");
        }

        static void DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        static void EnsureCrackNoiseTexture()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexRadialPath) != null) return;

            // Prefer project noise; otherwise bake a procedural grain
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseSource);
            if (src != null)
            {
                AssetDatabase.CopyAsset(NoiseSource, TexRadialPath);
                return;
            }

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.07f, y * 0.07f);
                tex.SetPixel(x, y, new Color(n, n, n, 1f));
            }
            tex.Apply();
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(Application.dataPath, "SacredRelicDemo/T_CrackNoise.png"),
                tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexRadialPath);
        }

        static Material CreateShellMaterial()
        {
            var shader = Shader.Find("MRBase/SacredRelic/ShellRadialDissolve");
            if (shader == null)
            {
                Debug.LogError("[SacredRelic] ShellRadialDissolve shader missing.");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            var mat = new Material(shader) { name = "M_Shell_Radial" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ShellDirt);
            if (mat.HasProperty("_EdgeColor")) mat.SetColor("_EdgeColor", SacredGoldHdr);
            if (mat.HasProperty("_DissolveAmount")) mat.SetFloat("_DissolveAmount", 0f);
            if (mat.HasProperty("_EdgeWidth")) mat.SetFloat("_EdgeWidth", 0.14f);
            if (mat.HasProperty("_NoiseScale")) mat.SetFloat("_NoiseScale", 2.8f);
            if (mat.HasProperty("_NoiseStrength")) mat.SetFloat("_NoiseStrength", 0.2f);
            if (mat.HasProperty("_RadialScale")) mat.SetFloat("_RadialScale", 1.55f);

            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(TexRadialPath)
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

        static Material CreateParticleMaterial(string path, Color tint, bool additiveSoft)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };

            // Surface: Transparent
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additiveSoft ? 1f : 0f); // 0 Alpha, 1 Additive-ish depending on URP
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ParticleTex);
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }

            mat.renderQueue = 3000;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static GameObject BuildPrefabHierarchy(
            Material shellMat, Material tabletMat, Material chunkMat, Material ashMat)
        {
            var root = new GameObject("SacredRelicTablet");
            var awaken = root.AddComponent<SacredRelicAwaken>();
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
            // Unit cube local coords ±0.5; radial dissolve uses object-space XY
            shell.transform.localScale = new Vector3(0.86f, 1.16f, 0.14f);
            Object.DestroyImmediate(shell.GetComponent<BoxCollider>());
            var shellRend = shell.GetComponent<MeshRenderer>();
            shellRend.sharedMaterial = shellMat;

            // Keep inscription but muted — user said ignore text for now
            var textGo = new GameObject("Inscription");
            textGo.transform.SetParent(tablet.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.05f, -0.55f);
            textGo.transform.localScale = new Vector3(1f / 0.78f, 1f / 1.08f, 1f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "·";
            tmp.fontSize = 2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.35f, 0.32f, 0.28f, 0.35f);
            tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.6f);

            var chunk = CreateChunkParticles(root.transform, chunkMat);
            var ash = CreateAshParticles(root.transform, ashMat);

            // Slow ease: linger at center crack, then expand
            var dissolveCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.15f, 0.08f, 0.4f, 0.4f),
                new Keyframe(0.55f, 0.45f, 0.9f, 0.9f),
                new Keyframe(1f, 1.15f, 0.6f, 0f));

            var so = new SerializedObject(awaken);
            so.FindProperty("shellRenderer").objectReferenceValue = shellRend;
            so.FindProperty("tabletRenderer").objectReferenceValue = tablet.GetComponent<MeshRenderer>();
            so.FindProperty("inscriptionText").objectReferenceValue = tmp;
            so.FindProperty("chunkParticles").objectReferenceValue = chunk;
            so.FindProperty("ashParticles").objectReferenceValue = ash;
            so.FindProperty("dissolver").objectReferenceValue = null; // own radial shader
            so.FindProperty("duration").floatValue = 5.0f;
            so.FindProperty("goldSeepEnd").floatValue = 0.5f;
            so.FindProperty("restoreStart").floatValue = 0.6f;
            so.FindProperty("sacredGold").colorValue = SacredGold;
            so.FindProperty("goldEdgeIntensity").floatValue = 8f;
            so.FindProperty("dissolveCurve").animationCurveValue = dissolveCurve;
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        static ParticleSystem CreateChunkParticles(Transform parent, Material mat)
        {
            var go = new GameObject("ChunkDebris");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 3.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = ChunkBrown;
            main.gravityModifier = 0.65f;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            // Staggered bursts as crack expands from center
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.2f, 10, 16),
                new ParticleSystem.Burst(0.8f, 14, 22),
                new ParticleSystem.Burst(1.6f, 12, 18),
                new ParticleSystem.Burst(2.6f, 8, 14)
            });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.08f;
            shape.radiusThickness = 1f;
            shape.position = Vector3.zero;
            // Emit in face plane, outward
            shape.rotation = new Vector3(0f, 0f, 0f);
            shape.scale = Vector3.one;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(ChunkBrown, 0f), new GradientColorKey(new Color(0.2f, 0.14f, 0.08f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            // Expand emission radius over time via shape — approximate with velocity outward
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            force.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.25f);
            force.z = new ParticleSystem.MinMaxCurve(-0.9f, -0.35f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
            return ps;
        }

        static ParticleSystem CreateAshParticles(Transform parent, Material mat)
        {
            var go = new GameObject("FineAsh");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 4.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            main.startColor = AshGrey;
            main.gravityModifier = -0.06f;
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.5f, 25, 40),
                new ParticleSystem.Burst(1.4f, 30, 45),
                new ParticleSystem.Burst(2.5f, 25, 40),
                new ParticleSystem.Burst(3.5f, 15, 25)
            });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.12f;
            shape.radiusThickness = 1f;

            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.y = 0.2f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.65f, 0.65f, 0.68f), 0f), new GradientColorKey(new Color(0.4f, 0.4f, 0.42f), 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.35f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
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
            ugui.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            ugui.text = "Center-out dissolve ~5s — Space/Click awaken | R reset";
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
