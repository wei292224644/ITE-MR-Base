using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

namespace MRBase.SacredRelic.EditorTools
{
    /// <summary>
    /// Assembles the demo scene from the Blender-generated relic: imports the fractured FBX,
    /// builds materials wired to the crack mask, and reads each shard's crack timing out of
    /// the generator's manifest so the burst order matches the painted cracks.
    /// </summary>
    public static class SacredRelicDemoBuilder
    {
        const string Generated = "Assets/Assets/SacredRelicDemo/Generated";
        const string FbxPath = Generated + "/Models/SacredRelic_Fractured.fbx";
        const string ManifestPath = Generated + "/Models/SacredRelic_Fractured.json";
        const string MaskPath = Generated + "/Textures/T_SacredRelic_CrackMask.png";
        const string DemoFolder = "Assets/Assets/SacredRelicDemo";
        const string ScenePath = DemoFolder + "/SacredRelicAwakenDemo.unity";
        const string ShellShader = "MRBase/Sacred Relic Shell";

        [System.Serializable]
        class PieceEntry
        {
            public string name;
            public float arrive;
            public float detach;
            public float radial;
        }

        [System.Serializable]
        class Manifest
        {
            public float width;
            public float height;
            public float thickness;
            public int cellCount;
            public PieceEntry[] pieces;
        }

        [MenuItem("Tools/Sacred Relic/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            if (!File.Exists(FbxPath))
            {
                EditorUtility.DisplayDialog("Sacred Relic",
                    "Missing " + FbxPath + "\n\nRun Tools/Blender/gen_sacred_relic.py first.", "OK");
                return;
            }

            ConfigureImporter();
            Manifest manifest = LoadManifest();
            Texture2D mask = ConfigureMask();

            Material outer = CreateShellMaterial("M_Relic_Shell_Outer", mask,
                // Mud / soot / weathered crust — what peels away.
                new Color(0.145f, 0.118f, 0.090f), crackStrength: 1f);
            Material inner = CreateShellMaterial("M_Relic_Shell_Inner", mask,
                // Fresher stone revealed on the crack faces of the crust.
                new Color(0.470f, 0.430f, 0.365f), crackStrength: 0f);
            Material core = CreateCoreMaterial("M_Relic_Core");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildEnvironment(manifest);

            GameObject relic = InstantiateRelic(outer, inner, core, out Renderer coreRenderer,
                out List<SacredRelicFracture.Shard> shards, manifest);

            // Both dust paths get built so they can be compared by swapping the Dust Source
            // reference on the fracture component; only the baked one is wired up by default.
            RelicDustSource dust = CreateBakedDust(relic.transform, manifest);
            CreateVfxDust(relic.transform);

            // The crust sits on the world -Z side of the core; expressing it in the relic's own
            // space keeps the burst correct if the tablet is later rotated in the scene.
            Vector3 faceLocal = relic.transform.InverseTransformDirection(Vector3.back);

            var fracture = relic.AddComponent<SacredRelicFracture>();
            fracture.Bind(relic.transform, coreRenderer, shards, dust, faceLocal);
            ScaleMotionForStele(fracture, manifest);
            relic.AddComponent<SacredRelicTrigger>();

            Directory.CreateDirectory(DemoFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = relic;
            Debug.Log($"[SacredRelic] Demo built: {shards.Count} shards, mask {mask.width}x{mask.height}. " +
                      "Press Play then Space to awaken, R to reseal.");
        }

        static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            if (importer == null) return;
            bool dirty = false;
            void Set<T>(ref T field, T value)
            {
                if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; dirty = true; }
            }

            // Blender leaves the Z-up to Y-up conversion as a rotation on the root; letting
            // Unity bake it keeps every shard's own transform clean.
            bool bake = importer.bakeAxisConversion;
            Set(ref bake, true);
            importer.bakeAxisConversion = bake;

            bool off = false;
            if (importer.importCameras) { importer.importCameras = off; dirty = true; }
            if (importer.importLights) { importer.importLights = off; dirty = true; }
            if (importer.importBlendShapes) { importer.importBlendShapes = off; dirty = true; }
            if (importer.importVisibility) { importer.importVisibility = off; dirty = true; }
            if (importer.weldVertices) { importer.weldVertices = off; dirty = true; }
            if (importer.importNormals != ModelImporterNormals.Import)
            { importer.importNormals = ModelImporterNormals.Import; dirty = true; }
            if (importer.importTangents != ModelImporterTangents.CalculateMikk)
            { importer.importTangents = ModelImporterTangents.CalculateMikk; dirty = true; }
            // Both dust paths sample the shard meshes on the CPU at bake time.
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }

            if (dirty) importer.SaveAndReimport();
        }

        static Manifest LoadManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                Debug.LogWarning("[SacredRelic] No manifest; shards will burst in name order.");
                return new Manifest { pieces = new PieceEntry[0], width = 0.6f, height = 0.9f };
            }
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        }

        static Texture2D ConfigureMask()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(MaskPath);
            if (importer != null)
            {
                bool dirty = false;
                // The mask packs geometry data, not colour: sRGB or compression would smear
                // the arrival channel and make the crack grow in blotches.
                if (importer.sRGBTexture) { importer.sRGBTexture = false; dirty = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
                if (importer.wrapMode != TextureWrapMode.Clamp)
                { importer.wrapMode = TextureWrapMode.Clamp; dirty = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
                if (dirty) importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        }

        static Material CreateShellMaterial(string name, Texture2D mask, Color baseColour,
                                            float crackStrength)
        {
            Shader shader = Shader.Find(ShellShader);
            if (shader == null)
            {
                Debug.LogError("[SacredRelic] Shader not found: " + ShellShader);
                return null;
            }
            var material = new Material(shader) { name = name };
            material.SetTexture("_CrackMask", mask);
            material.SetColor("_BaseColor", baseColour);
            material.SetFloat("_CrackStrength", crackStrength);
            material.SetColor("_GlowColor", new Color(1f, 0.63f, 0.22f));
            material.SetFloat("_GlowStrength", 7f);
            material.SetFloat("_TipBoost", 5f);
            material.SetFloat("_Progress", 0f);
            material.SetFloat("_GoldIntensity", 0f);
            material.SetFloat("_Dissolve", 0f);
            material.SetFloat("_NoiseScale", 42f);
            // The erosion rim was blowing out into flat yellow blobs; a narrow, dimmer band reads
            // as heat in the stone instead.
            material.SetColor("_EdgeColor", new Color(1f, 0.55f, 0.18f));
            material.SetFloat("_EdgeStrength", 2.1f);
            material.SetFloat("_EdgeWidth", 0.09f);
            material.enableInstancing = true;
            return SaveAsset(material, DemoFolder + "/" + name + ".mat");
        }

        static Material CreateCoreMaterial(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = name };
            // Prefer the Sketchfab albedo packed with the FBX; fall back to a clean stone tint.
            Texture2D albedo = FindSteleAlbedo();
            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetColor("_BaseColor", Color.white);
            }
            else
            {
                material.SetColor("_BaseColor", new Color(0.512f, 0.470f, 0.404f));
            }
            material.SetFloat("_Smoothness", 0.28f);
            material.enableInstancing = true;
            return SaveAsset(material, DemoFolder + "/" + name + ".mat");
        }

        static Texture2D FindSteleAlbedo()
        {
            string[] candidates =
            {
                Generated + "/Models/Image_0.png",
                Generated + "/Textures/Image_0.png",
                Generated + "/Models/SacredRelic_Fractured.fbm/Image_0.png",
            };
            foreach (string path in candidates)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) return tex;
            }

            // FBX may have imported the embedded texture as a sub-asset.
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            {
                if (asset is Texture2D tex && tex.width > 64)
                    return tex;
            }
            return null;
        }

        static Material SaveAsset(Material material, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = material.shader;
                existing.CopyPropertiesFromMaterial(material);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void ScaleMotionForStele(SacredRelicFracture fracture, Manifest manifest)
        {
            // Procedural tablet was ~1m; Sketchfab stele can be ~10m+. Keep travel readable.
            float height = Mathf.Max(0.9f, manifest.height);
            var so = new SerializedObject(fracture);
            so.FindProperty("burstReach").floatValue = Mathf.Clamp(height * 0.045f, 0.4f, 1.5f);
            so.FindProperty("seamOpening").floatValue = Mathf.Clamp(height * 0.0012f, 0.006f, 0.04f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildEnvironment(Manifest manifest)
        {
            float height = Mathf.Max(0.9f, manifest.height);
            float width = Mathf.Max(0.6f, manifest.width);
            float thickness = Mathf.Max(0.2f, manifest.thickness);
            // Face the inscription (-Z after FBX); stand far enough to see the full stele.
            float distance = Mathf.Max(height, width) * 1.35f + thickness * 0.5f + 1.5f;
            float eye = height * 0.55f;

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(0f, eye, -distance),
                                                    Quaternion.Euler(4f, 0f, 0f));
            camera.backgroundColor = new Color(0.035f, 0.038f, 0.048f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = Mathf.Max(100f, distance * 4f);

            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.35f;
            fill.color = new Color(0.62f, 0.72f, 0.95f);
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(-12f, 145f, 0f);
        }

        /// <summary>
        /// Ensure triangle winding + normals point toward the camera-facing side of the stele
        /// (world -Z after the FBX axis bake). Without this, Sketchfab scans often render
        /// inside-out once Unity flips Z-up to Y-up.
        /// </summary>
        static void FixImportedNormals(GameObject root)
        {
            // Camera looks down +Z toward the stele; the inscription faces world -Z.
            Vector3 faceHintWorld = Vector3.back;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh source = filter.sharedMesh;
                if (source == null || !source.isReadable) continue;

                Mesh mesh = Object.Instantiate(source);
                mesh.name = source.name + "_Outward";
                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                if (verts.Length == 0 || tris.Length < 3) continue;

                Vector3 localFace = filter.transform.InverseTransformDirection(faceHintWorld);
                if (localFace.sqrMagnitude < 1e-8f) localFace = Vector3.back;
                localFace.Normalize();
                // Only judge the camera-facing slab — a closed volume always has ~50/50 otherwise.
                float minDot = float.PositiveInfinity, maxDot = float.NegativeInfinity;
                for (int i = 0; i < verts.Length; i++)
                {
                    float d = Vector3.Dot(verts[i], localFace);
                    if (d < minDot) minDot = d;
                    if (d > maxDot) maxDot = d;
                }
                float frontThresh = maxDot - (maxDot - minDot) * 0.2f;

                int toward = 0, away = 0;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = verts[tris[i]];
                    Vector3 b = verts[tris[i + 1]];
                    Vector3 c = verts[tris[i + 2]];
                    Vector3 mid = (a + b + c) * (1f / 3f);
                    if (Vector3.Dot(mid, localFace) < frontThresh) continue;

                    Vector3 geo = Vector3.Cross(b - a, c - a);
                    if (geo.sqrMagnitude < 1e-12f) continue;
                    if (Vector3.Dot(geo, localFace) >= 0f) toward++;
                    else away++;
                }

                if (away > toward)
                {
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        int tmp = tris[i];
                        tris[i] = tris[i + 1];
                        tris[i + 1] = tmp;
                    }
                    mesh.triangles = tris;
                }

                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;
            }
        }

        static GameObject InstantiateRelic(Material outer, Material inner, Material core,
                                          out Renderer coreRenderer,
                                          out List<SacredRelicFracture.Shard> shards,
                                          Manifest manifest)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                              InteractionMode.AutomatedAction);
            instance.name = "SacredRelic";

            // Blender writes 89.98 rather than a clean 90; snap it so the tablet is dead upright.
            Vector3 euler = instance.transform.localEulerAngles;
            instance.transform.localRotation = Quaternion.Euler(Mathf.Round(euler.x / 90f) * 90f,
                                                               Mathf.Round(euler.y / 90f) * 90f,
                                                               Mathf.Round(euler.z / 90f) * 90f);
            float lift = manifest.height > 0.01f ? manifest.height * 0.5f + 0.35f : 0.8f;
            instance.transform.position = new Vector3(0f, lift, 0f);

            // Blender/FBX can leave winding and normals disagreeing with the camera-facing
            // side after axis conversion. Rebuild every mesh so fronts light correctly.
            FixImportedNormals(instance);

            var timings = new Dictionary<string, PieceEntry>();
            if (manifest.pieces != null)
                foreach (PieceEntry p in manifest.pieces) timings[p.name] = p;

            coreRenderer = null;
            shards = new List<SacredRelicFracture.Shard>();

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name.Contains("Core"))
                {
                    renderer.sharedMaterial = core;
                    coreRenderer = renderer;
                    continue;
                }

                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = i == 0 ? outer : inner;
                renderer.sharedMaterials = slots;

                // No rest pose here on purpose: Bind() clears restCached and lets CacheRest read
                // the poses itself, in relic space. Seeding a world pose from this side is what
                // used to leave the crust anchored wherever the relic happened to be built.
                var shard = new SacredRelicFracture.Shard
                {
                    transform = renderer.transform,
                    renderer = renderer,
                };
                if (timings.TryGetValue(renderer.name, out PieceEntry entry))
                {
                    shard.arrive = entry.arrive;
                    shard.detach = entry.detach;
                }
                shards.Add(shard);
            }

            var collider = instance.AddComponent<BoxCollider>();
            collider.size = new Vector3(Mathf.Max(0.1f, manifest.width),
                                        Mathf.Max(0.1f, manifest.height),
                                        Mathf.Max(0.02f, manifest.thickness) + 0.04f);
            collider.center = new Vector3(0f, 0f, Mathf.Max(0.02f, manifest.thickness) * 0.5f);
            collider.isTrigger = true;

            return instance;
        }

        static RelicDustSource CreateBakedDust(Transform parent, Manifest manifest)
        {
            var go = new GameObject("Dust (Baked Points)");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 3f;
            // Per-particle lifetime, size and velocity all come from the emitter, which places
            // each grain on the point of the surface that just eroded away.
            main.startLifetime = 1.6f;
            main.startSize = 0.01f;
            main.startSpeed = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.68f, 0.60f, 0.47f, 0.75f), new Color(0.86f, 0.74f, 0.55f, 0.55f));
            main.gravityModifier = -0.03f;
            main.maxParticles = 12000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.06f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.12f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.78f, 0.45f), 0f),
                        new GradientColorKey(new Color(0.74f, 0.68f, 0.58f), 0.35f),
                        new GradientColorKey(new Color(0.6f, 0.57f, 0.52f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f),
                        new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = FindDustMaterial();
            renderer.sortMode = ParticleSystemSortMode.Distance;

            return go.AddComponent<RelicDustBakedPoints>();
        }

        /// <summary>
        /// Builds the alternative dust path, left unwired so it can be swapped in for comparison.
        /// It clones INab's axis dissolve graph onto every shard, which also means the shell
        /// material has to be switched to axis mode for the particles to line up.
        /// </summary>
        static RelicDustSource CreateVfxDust(Transform parent)
        {
            var go = new GameObject("Dust (VFX Graph)");
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<RelicDustVfx>();

            const string graphPath = "Assets/INab Studio/Dissolve-FX-MasterKit/Vfx Graph Effects/" +
                                     "Core/Axis/6/Axis Dissolve 6.vfx";
            var graph = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(graphPath);
            if (graph == null)
            {
                Debug.LogWarning($"[SacredRelic] Dissolve graph not found at {graphPath}; " +
                                 "assign one on the Dust (VFX Graph) object to try that path.");
            }

            var serialised = new SerializedObject(source);
            serialised.FindProperty("dissolveGraph").objectReferenceValue = graph;
            serialised.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return source;
        }

        static Material FindDustMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = "M_Relic_Dust" };
            material.SetColor("_BaseColor", new Color(0.82f, 0.72f, 0.55f, 1f));
            // Without a soft grain the shader fills the whole billboard and every mote reads as a
            // hard square instead of powder.
            material.SetTexture("_BaseMap", CreateDustGrain());

            // Setting _Surface alone does not move URP off the opaque path; the blend state and
            // keyword have to be written too.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return SaveAsset(material, DemoFolder + "/M_Relic_Dust.mat");
        }

        const string DustGrainPath = Generated + "/Textures/T_RelicDustGrain.png";

        /// <summary>
        /// A soft, slightly irregular grain. Round enough to read as powder, uneven enough that a
        /// few thousand of them do not look like a field of identical dots.
        /// </summary>
        static Texture2D CreateDustGrain()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            const float centre = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - centre) / centre;
                    float dy = (y - centre) / centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Wobble the silhouette so grains are not perfect circles.
                    float angle = Mathf.Atan2(dy, dx);
                    distance *= 1f + 0.12f * Mathf.Sin(angle * 3f) + 0.07f * Mathf.Cos(angle * 5f);

                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.7f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(DustGrainPath));
            File.WriteAllBytes(DustGrainPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(DustGrainPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(DustGrainPath);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DustGrainPath);
        }
    }
}
