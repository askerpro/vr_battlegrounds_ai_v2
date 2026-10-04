using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Core;
using VrBattlegrounds.EditorTools;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor
{
    public static class SpawnZoneCreator
    {
        private const string PrefabPath = "Assets/Prefabs/Maps/TeamSpawnZone.prefab";

        [MenuItem("GameObject/VR Battlegrounds/Team Spawn Zone", false, 10)]
        public static void CreateTeamSpawnZone(MenuCommand menuCommand)
        {
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                GameLog.Error("Зона спавна создаётся в сцене карты. Закройте режим редактирования префаба.");
                return;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                GameLog.Error($"[SpawnZoneCreator] Не найден префаб по пути: {PrefabPath}");
                return;
            }

            var context = menuCommand.context as GameObject;
            var scene = context != null ? context.scene : SceneManager.GetActiveScene();
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            if (instance == null) return;

            // Спавн принадлежит карте, даже если в меню выбрано общее окружение.
            instance.transform.SetParent(MapGameplayHierarchy.SpawnParent(scene, context), false);

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
