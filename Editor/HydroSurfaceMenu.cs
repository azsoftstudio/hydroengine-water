using UnityEditor;
using UnityEngine;

namespace HydroEngine.Editor
{
    public static class HydroSurfaceMenu
    {
        [MenuItem("GameObject/Water/HydroEngine Water Surface", false, 10)]
        private static void CreateSurface(MenuCommand command)
        {
            var go = new GameObject("HydroEngine Water Surface", typeof(HydroSurface));
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create HydroEngine Water Surface");
            Selection.activeGameObject = go;
        }
    }
}
