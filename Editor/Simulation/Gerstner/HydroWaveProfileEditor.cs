using UnityEditor;
using UnityEngine;

namespace HydroEngine.Editor
{
    [CustomEditor(typeof(HydroWaveProfile))]
    public sealed class HydroWaveProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Generate a reproducible 12-component wind spectrum. Swell, primary, and chop are relative amplitude multipliers; their squared values redistribute variance while Significant Height keeps the total normalized. Generating replaces the manual Waves list; Unity Undo can restore it.", MessageType.Info);

            if (GUILayout.Button("Generate Wind Spectrum"))
            {
                HydroWaveProfile profile = (HydroWaveProfile)target;
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(profile, "Generate Hydro Wave Spectrum");
                profile.GenerateWindSpectrum();
                EditorUtility.SetDirty(profile);
                serializedObject.Update();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
