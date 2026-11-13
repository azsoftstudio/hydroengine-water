using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HydroEngine.Editor
{
    public static class HydroDemoSceneBuilder
    {
        private const string DemoFolder = "Assets/HydroEngine/Demo";
        private const string ScenePath = DemoFolder + "/HydroEngineDemo.unity";
        private const string WaterMaterialPath = DemoFolder + "/HydroDemoWater.mat";
        private const string LandMaterialPath = DemoFolder + "/HydroDemoLand.mat";
        private const string BuoyMaterialPath = DemoFolder + "/HydroDemoBuoys.mat";

        [MenuItem("Tools/HydroEngine/Generate Water Demo Scene")]
        private static void GenerateDemoScene()
        {
            if (File.Exists(ScenePath) || File.Exists(WaterMaterialPath)
                || File.Exists(LandMaterialPath) || File.Exists(BuoyMaterialPath))
            {
                EditorUtility.DisplayDialog("HydroEngine Demo", "A demo scene or one of its sample assets already exists. Move or rename the existing files before generating a fresh demo.", "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder(DemoFolder)) AssetDatabase.CreateFolder("Assets/HydroEngine", "Demo");
            Shader shader = Shader.Find("HydroEngine/Water (URP)");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("HydroEngine Demo", "The HydroEngine URP shader was not found. Fix shader import errors, then try again.", "OK");
                return;
            }

            Material waterMaterial = new Material(shader) { name = "Hydro Demo Water" };
            AssetDatabase.CreateAsset(waterMaterial, WaterMaterialPath);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.48f, 0.63f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.27f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.1f, 0.12f);

            GameObject water = new GameObject("HydroEngine Water", typeof(HydroSurface));
            SerializedObject waterData = new SerializedObject(water.GetComponent<HydroSurface>());
            waterData.FindProperty("size").floatValue = 60f;
            waterData.FindProperty("resolution").intValue = 80;
            waterData.FindProperty("maximumAutoResolution").intValue = 80;
            waterData.FindProperty("cameraCenteredLod").boolValue = true;
            waterData.FindProperty("lodRingCount").intValue = 4;
            waterData.FindProperty("lodPatchSize").floatValue = 64f;
            waterData.FindProperty("lodResolution").intValue = 80;
            waterData.FindProperty("waterMaterial").objectReferenceValue = waterMaterial;
            waterData.FindProperty("followTransform").boolValue = false;
            waterData.ApplyModifiedPropertiesWithoutUndo();
            water.AddComponent<HydroPlanarReflection>();

            Material landMaterial = CreateLitMaterial("Hydro Demo Land", new Color(0.29f, 0.38f, 0.22f), LandMaterialPath);
            Material buoyMaterial = CreateLitMaterial("Hydro Demo Buoys", new Color(1f, 0.36f, 0.07f), BuoyMaterialPath);
            CreateIsland("North Shore", new Vector3(-8f, -1.15f, 25f), new Vector3(28f, 3.5f, 13f), landMaterial);
            CreateIsland("East Shore", new Vector3(27f, -1.3f, 5f), new Vector3(13f, 4.2f, 27f), landMaterial);
            CreateIsland("Southwest Shore", new Vector3(-27f, -1.1f, -17f), new Vector3(13f, 3.7f, 20f), landMaterial);
            CreateIsland("Rock Islet", new Vector3(8f, -0.25f, 13f), new Vector3(5f, 2.8f, 4f), landMaterial);

            for (int i = 0; i < 3; i++)
            {
                GameObject buoy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                buoy.name = $"Physics Buoy {i + 1}";
                buoy.transform.position = new Vector3(-8f + i * 8f, 8f, -2f + i * 5f);
                buoy.transform.localScale = Vector3.one * 1.2f;
                buoy.GetComponent<Renderer>().sharedMaterial = buoyMaterial;
                Rigidbody body = buoy.AddComponent<Rigidbody>();
                body.mass = 150f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                HydroBuoyancy buoyancy = buoy.AddComponent<HydroBuoyancy>();
                buoyancy.Surface = water.GetComponent<HydroSurface>();
                buoyancy.DisplacedVolume = 0.9f;
            }

            GameObject lightObject = new GameObject("Demo Sun", typeof(Light));
            Light sun = lightObject.GetComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            RenderSettings.sun = sun;

            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 27f, -41f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.42f, 1f));
            Camera camera = cameraObject.GetComponent<Camera>(); camera.fieldOfView = 48f; camera.farClipPlane = 250f;
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.requiresDepthTexture = true;
            cameraData.requiresColorTexture = true;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Selection.activeGameObject = water;
            EditorUtility.DisplayDialog("HydroEngine Demo", "Created HydroEngine/Demo/HydroEngineDemo.unity with still water, a material, shoreline shapes, and Rigidbody buoyancy examples.", "Open Scene");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/HydroEngine/Upgrade Open Demo Buoys To Physics")]
        private static void UpgradeOpenDemoBuoys()
        {
            HydroSurface surface = Object.FindFirstObjectByType<HydroSurface>();
            if (surface == null)
            {
                EditorUtility.DisplayDialog("HydroEngine Buoyancy", "No HydroSurface was found in the open scene.", "OK");
                return;
            }

            int upgraded = 0;
            HydroBuoyancy[] existingBuoyancies = Object.FindObjectsByType<HydroBuoyancy>(FindObjectsSortMode.None);
            foreach (HydroBuoyancy buoyancy in existingBuoyancies)
            {
                GameObject buoy = buoyancy.gameObject;
                if (!buoy.name.StartsWith("Physics Buoy")) continue;
                ConfigureDemoBuoy(buoy, surface, buoyancy);
                upgraded++;
            }

            HydroHeightFollower[] followers = Object.FindObjectsByType<HydroHeightFollower>(FindObjectsSortMode.None);
            foreach (HydroHeightFollower follower in followers)
            {
                GameObject buoy = follower.gameObject;
                if (!buoy.name.StartsWith("Wave Query Buoy")) continue;

                HydroBuoyancy buoyancy = buoy.GetComponent<HydroBuoyancy>();
                if (buoyancy == null) buoyancy = Undo.AddComponent<HydroBuoyancy>(buoy);
                ConfigureDemoBuoy(buoy, surface, buoyancy);
                Undo.DestroyObjectImmediate(follower);
                buoy.name = buoy.name.Replace("Wave Query", "Physics");
                upgraded++;
            }

            if (upgraded > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorUtility.DisplayDialog("HydroEngine Buoyancy", $"Upgraded {upgraded} demo buoy(s). Save the scene, then enter Play mode to watch them fall and float.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("HydroEngine Buoyancy", "No demo buoy objects were found. This updates 'Physics Buoy' objects and upgrades old 'Wave Query Buoy' followers.", "OK");
            }
        }

        private static void ConfigureDemoBuoy(GameObject buoy, HydroSurface surface, HydroBuoyancy buoyancy)
        {
            Undo.RegisterCompleteObjectUndo(buoy, "Configure Hydro Demo Buoy");
            Rigidbody body = buoy.GetComponent<Rigidbody>();
            if (body == null) body = Undo.AddComponent<Rigidbody>(buoy);
            Undo.RecordObject(body, "Configure Hydro Demo Buoy Rigidbody");
            body.mass = 150f;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Undo.RecordObject(buoyancy, "Assign Hydro Buoyancy Surface");
            buoyancy.Surface = surface;
            buoyancy.DisplacedVolume = 0.9f;

            Undo.RecordObject(buoy.transform, "Raise Hydro Demo Buoy");
            Vector3 position = buoy.transform.position;
            position.y = Mathf.Max(position.y, surface.transform.position.y + 8f);
            buoy.transform.position = position;
            EditorUtility.SetDirty(buoy);
            EditorUtility.SetDirty(buoyancy);
        }

        private static Material CreateLitMaterial(string materialName, Color color, string assetPath)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = materialName };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        private static void CreateIsland(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject island = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            island.name = name; island.transform.position = position; island.transform.localScale = scale;
            island.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
