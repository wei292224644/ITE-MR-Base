using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MRBase.SacredRelic.EditorTools
{
    /// <summary>
    /// Wires SacredRelicFracture onto a user-imported SacredRelic_Stele instance in the open scene.
    /// </summary>
    public static class SacredRelicSteleBinder
    {
        const string ManifestPath = "Assets/SacredRelicDemo/Generated/Models/SacredRelic_Fractured.json";
        const string MaskPath = "Assets/SacredRelicDemo/Generated/Textures/T_SacredRelic_CrackMask.png";
        const string AlbedoPath = "Assets/SacredRelicDemo/Generated/Textures/Image_0.png";
        const string ShellShader = "MRBase/Sacred Relic Shell";

        [Serializable]
        class PieceEntry
        {
            public string name;
            public float arrive;
            public float detach;
        }

        [Serializable]
        class Manifest
        {
            public float width;
            public float height;
            public float thickness;
            public PieceEntry[] pieces;
        }

        [MenuItem("Tools/Sacred Relic/Bind Scene Stele")]
        public static void BindSceneStele()
        {
            var stele = GameObject.Find("SacredRelic_Stele");
            if (stele == null)
            {
                EditorUtility.DisplayDialog("Sacred Relic",
                    "Scene has no SacredRelic_Stele. Import/place that FBX first.", "OK");
                return;
            }

            var old = GameObject.Find("SacredRelic");
            if (old != null) old.SetActive(false);

            foreach (Transform t in stele.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Stele_Body") t.gameObject.SetActive(false);
            }

            // Inscription normals on the imported FBX face +Z; turn to face the camera (-Z).
            stele.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

            Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            Material outer = EnsureShellMaterial("Assets/SacredRelicDemo/M_Relic_Shell_Outer.mat",
                mask, new Color(0.145f, 0.118f, 0.090f), 1f);
            Material inner = EnsureShellMaterial("Assets/SacredRelicDemo/M_Relic_Shell_Inner.mat",
                mask, new Color(0.470f, 0.430f, 0.365f), 0f);
            Material coreMat = EnsureCoreMaterial(albedo);

            Manifest manifest = LoadManifest();
            var timing = new Dictionary<string, PieceEntry>();
            if (manifest.pieces != null)
                foreach (PieceEntry p in manifest.pieces) timing[p.name] = p;

            Renderer coreRenderer = null;
            var shards = new List<SacredRelicFracture.Shard>();
            foreach (Renderer rend in stele.GetComponentsInChildren<Renderer>(true))
            {
                if (rend.name.Contains("Core"))
                {
                    rend.sharedMaterial = coreMat;
                    coreRenderer = rend;
                    continue;
                }

                if (!rend.name.StartsWith("Shell_Piece", StringComparison.Ordinal)) continue;

                // Only hand over the second material if the mesh actually has a second submesh.
                // The sketchfab crust prisms leave every face on material index 0, so they
                // export as a single submesh — and Unity renders a surplus material by drawing
                // the last submesh AGAIN. That redraw put the fracture-face material (which has
                // _CrackStrength 0 by design) on top of the weathered outside at the same depth,
                // erasing the crack network and its gold entirely.
                var filter = rend.GetComponent<MeshFilter>();
                int submeshes = filter != null && filter.sharedMesh != null
                    ? filter.sharedMesh.subMeshCount
                    : 1;
                rend.sharedMaterials = submeshes >= 2
                    ? new[] { outer, inner }
                    : new[] { outer };
                var shard = new SacredRelicFracture.Shard
                {
                    transform = rend.transform,
                    renderer = rend,
                };
                if (timing.TryGetValue(rend.name, out PieceEntry entry))
                {
                    shard.arrive = entry.arrive;
                    shard.detach = entry.detach;
                }
                shards.Add(shard);
            }

            if (coreRenderer == null || shards.Count == 0)
            {
                EditorUtility.DisplayDialog("Sacred Relic",
                    "SacredRelic_Stele is missing Relic_Core or Shell_Piece_* children.", "OK");
                return;
            }

            // Dust bake samples shell meshes on the CPU — Read/Write must be on.
            EnsureSteleMeshesReadable();

            float height = Mathf.Max(0.9f, manifest.height);
            RelicDustSource dust = EnsureBakedDust(stele.transform, height);

            var fracture = stele.GetComponent<SacredRelicFracture>();
            bool freshComponent = fracture == null;
            if (freshComponent) fracture = stele.AddComponent<SacredRelicFracture>();
            Vector3 faceLocal = stele.transform.InverseTransformDirection(Vector3.back);
            fracture.Bind(stele.transform, coreRenderer, shards, dust, faceLocal);

            // Seed the look only when this component is new. Re-binding after a mesh or mask
            // change must not throw away hand-tuned values — the numbers below are a sane
            // starting point, not the authority. Delete the component to get them back.
            if (freshComponent)
            {
                var so = new SerializedObject(fracture);
                // A 16 m stele needs metres of travel before the wind reads at all; 4.5% of
                // height put every flake inside its own silhouette. At 38% the crust clears
                // roughly a third of the stele before it powders, which is what makes the gust
                // look like it is actually carrying the pieces off.
                so.FindProperty("burstReach").floatValue = Mathf.Clamp(height * 0.38f, 2.5f, 10f);
                so.FindProperty("seamOpening").floatValue =
                    Mathf.Clamp(height * 0.0012f, 0.006f, 0.04f);
                // A steady gust carries the late flakes nearly as far as the early ones; the
                // difference should read as when they left, not how hard they were thrown.
                so.FindProperty("rimReach").floatValue = 0.75f;
                // Long enough for the peel hinge to actually swing before the shard lets go.
                so.FindProperty("goldHold").floatValue = 0.55f;
                so.FindProperty("spreadMode").enumValueIndex = 1; // Directional
                so.FindProperty("spreadFrom").vector2Value = new Vector2(0f, 0f);
                so.FindProperty("spreadTo").vector2Value = new Vector2(1f, 1f);
                so.FindProperty("radialFan").floatValue = 0f;
                so.FindProperty("spinDegrees").floatValue = 40f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (stele.GetComponent<SacredRelicTrigger>() == null)
                stele.AddComponent<SacredRelicTrigger>();

            FrameCamera(stele);

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = stele;
            Debug.Log($"[SacredRelic] Bound SacredRelic_Stele: {shards.Count} shards, " +
                      $"faceLocal={faceLocal}. Play → Space to awaken.");
        }

        static void EnsureSteleMeshesReadable()
        {
            const string fbx = "Assets/SacredRelicDemo/Generated/Models/SacredRelic_Stele.fbx";
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null) return;
            if (importer.isReadable) return;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        static RelicDustSource EnsureBakedDust(Transform parent, float steleHeight)
        {
            Transform dustTf = parent.Find("Dust (Baked Points)");
            GameObject go = dustTf != null ? dustTf.gameObject : new GameObject("Dust (Baked Points)");
            if (dustTf == null) go.transform.SetParent(parent, false);

            var ps = go.GetComponent<ParticleSystem>() ?? go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float sizeScale = Mathf.Clamp(steleHeight / 1.2f, 1f, 12f);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 3f;
            main.startLifetime = 2.4f;
            main.startSize = 0.01f * sizeScale;
            main.startSpeed = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.68f, 0.60f, 0.47f, 0.75f), new Color(0.86f, 0.74f, 0.55f, 0.55f));
            main.gravityModifier = -0.03f;
            main.maxParticles = 20000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.06f * sizeScale;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.12f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.78f, 0.45f), 0f),
                    new GradientColorKey(new Color(0.74f, 0.68f, 0.58f), 0.35f),
                    new GradientColorKey(new Color(0.6f, 0.57f, 0.52f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.15f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = LoadOrCreateDustMaterial();
            renderer.sortMode = ParticleSystemSortMode.Distance;

            var dust = go.GetComponent<RelicDustBakedPoints>()
                       ?? go.AddComponent<RelicDustBakedPoints>();

            // Scale the baked-point emitter so grains read on a ~16m stele.
            var dso = new SerializedObject(dust);
            dso.FindProperty("samplesPerShard").intValue = 480;
            dso.FindProperty("sizeRange").vector2Value =
                new Vector2(0.006f, 0.018f) * sizeScale;
            dso.FindProperty("driftUp").floatValue = 0.11f * sizeScale;
            dso.FindProperty("pushOffSurface").floatValue = 0.035f * sizeScale;
            dso.FindProperty("scatter").floatValue = 0.03f * sizeScale;
            dso.ApplyModifiedPropertiesWithoutUndo();
            return dust;
        }

        static Material LoadOrCreateDustMaterial()
        {
            const string path = "Assets/SacredRelicDemo/M_Relic_Dust.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            // Fallback if the demo builder has not created it yet.
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = "M_Relic_Dust" };
            material.SetColor("_BaseColor", new Color(0.82f, 0.72f, 0.55f, 1f));
            var grain = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/SacredRelicDemo/Generated/Textures/T_RelicDustGrain.png");
            if (grain != null) material.SetTexture("_BaseMap", grain);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Manifest LoadManifest()
        {
            if (!File.Exists(ManifestPath))
                return new Manifest { pieces = Array.Empty<PieceEntry>(), height = 16f, width = 6.6f, thickness = 2.7f };
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        }

        static Material EnsureShellMaterial(string path, Texture2D mask, Color baseColour, float crack)
        {
            Shader shader = Shader.Find(ShellShader);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool freshMaterial = material == null;
            if (freshMaterial)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            // Structural, and safe to reassert every bind: which mask this material reads and
            // whether it is the weathered outside or a fracture face.
            material.shader = shader;
            material.SetTexture("_CrackMask", mask);
            material.SetFloat("_CrackStrength", crack);
            material.enableInstancing = true;

            // Look values are a starting point only. Re-binding after a mask change must not
            // undo hand-tuning in the material inspector; delete the .mat to reseed them.
            if (freshMaterial)
            {
                material.SetColor("_BaseColor", baseColour);
                material.SetColor("_GlowColor", new Color(1f, 0.63f, 0.22f));
                material.SetFloat("_GlowStrength", 7f);
                material.SetFloat("_TipBoost", 5f);
                material.SetFloat("_EdgeStrength", 2.1f);
                material.SetFloat("_EdgeWidth", 0.09f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureCoreMaterial(Texture2D albedo)
        {
            const string path = "Assets/SacredRelicDemo/M_Relic_Core.mat";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = lit;
            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetColor("_BaseColor", Color.white);
            }
            else
            {
                material.SetColor("_BaseColor", new Color(0.51f, 0.47f, 0.40f));
            }
            material.SetFloat("_Smoothness", 0.28f);
            // The relic's light is driven per frame into _EmissionColor via a property block.
            // Without the keyword and a non-black GI flag URP compiles the emission out and
            // nothing the component writes ever shows.
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (material.GetColor("_EmissionColor").maxColorComponent <= 0f)
                material.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void FrameCamera(GameObject stele)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Renderer[] rends = stele.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (rends.Length == 0) return;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            float dist = Mathf.Max(b.size.y, b.size.x) * 1.35f + b.size.z * 0.5f + 1.5f;
            cam.transform.SetPositionAndRotation(
                new Vector3(b.center.x, b.center.y, b.center.z - dist),
                Quaternion.Euler(4f, 0f, 0f));
            cam.farClipPlane = Mathf.Max(100f, dist * 4f);
            cam.backgroundColor = new Color(0.035f, 0.038f, 0.048f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }
    }
}
