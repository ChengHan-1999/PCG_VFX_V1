#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PCG.VFX
{
    [CustomEditor(typeof(ThemeVfxJsonPresetExporter))]
    public class ThemeVfxJsonPresetExporterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "During Play mode: tune the exposed VFX_Theme properties, select the target Theme Id here, then save the current values to ThemeDefinitions.json.",
                MessageType.Info);

            ThemeVfxJsonPresetExporter exporter = (ThemeVfxJsonPresetExporter)target;
            if (GUILayout.Button("Save Current Inspector Values to Theme JSON"))
            {
                if (exporter.SaveCurrentInspectorValuesToThemeJson(out string message))
                {
                    Debug.Log("[PCG VFX] " + message, exporter);
                }
                else
                {
                    Debug.LogWarning("[PCG VFX] " + message, exporter);
                }
            }
        }
    }
}
#endif
