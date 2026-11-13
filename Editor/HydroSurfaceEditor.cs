using UnityEditor;
using UnityEngine;

namespace HydroEngine.Editor
{
    [CustomEditor(typeof(HydroSurface))]
    public sealed class HydroSurfaceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            HydroSurface surface = (HydroSurface)target;
            SerializedProperty materialProperty = serializedObject.FindProperty("waterMaterial");
            if (materialProperty.objectReferenceValue == null && GUILayout.Button("Create URP Water Material"))
                CreateWaterMaterial(surface, materialProperty);
            HydroSpectralOceanRenderer spectral = surface.GetComponent<HydroSpectralOceanRenderer>();
            if (spectral != null && spectral.isActiveAndEnabled && spectral.IsReady)
                EditorGUILayout.HelpBox("GPU spectral displacement is active. Height and velocity queries return false rather than reporting a mismatched Gerstner surface.", MessageType.Warning);
            else if (spectral != null && spectral.isActiveAndEnabled)
                EditorGUILayout.HelpBox("GPU spectral simulation is enabled but did not initialize. Check compute shader support and the HydroSpectralOcean resource.", MessageType.Error);
        }

        private static void CreateWaterMaterial(HydroSurface surface, SerializedProperty materialProperty)
        {
            Shader shader = Shader.Find("HydroEngine/Water (URP)");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("HydroEngine Water", "The HydroEngine URP shader could not be found.", "OK");
                return;
            }
            const string folder = "Assets/HydroEngine/Generated";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/HydroEngine", "Generated");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/HydroWater.mat");
            Material material = new Material(shader) { name = "Hydro Water" };
            AssetDatabase.CreateAsset(material, path);
            Undo.RecordObject(surface, "Assign HydroEngine Water Material");
            materialProperty.objectReferenceValue = material;
            materialProperty.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(surface);
        }
    }
}
