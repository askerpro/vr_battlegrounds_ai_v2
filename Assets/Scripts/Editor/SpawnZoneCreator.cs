using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor
{
    public static class SpawnZoneCreator
    {
        private const string PrefabPath = "Assets/Prefabs/Maps/TeamSpawnZone.prefab";

        [MenuItem("GameObject/VrBattlegrounds/Team Spawn Zone", false, 10)]
        public static void CreateTeamSpawnZone(MenuCommand menuCommand)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[SpawnZoneCreator] Не найден префаб по пути: {PrefabPath}");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return;

            // Ensure it gets parented to the selected object, or the active context
            GameObjectUtility.SetParentAndAlign(instance, menuCommand.context as GameObject);

            // Register the creation in the undo system
            Undo.RegisterCreatedObjectUndo(instance, "Create " + instance.name);

            // Strip the pre-assigned _team so the user is forced/prompted to select their own
            TeamSpawnZone spawnZone = instance.GetComponent<TeamSpawnZone>();
            if (spawnZone != null)
            {
                SerializedObject so = new SerializedObject(spawnZone);
                so.FindProperty("_team").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }

            Selection.activeObject = instance;
            
            // Focus the scene view on the new object
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }
    }
}