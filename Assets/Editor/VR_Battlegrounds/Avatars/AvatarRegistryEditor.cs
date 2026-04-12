#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mirror;
using System.Collections.Generic;
using VrBattlegrounds.Core;
using System.Linq;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Кастомный инспектор для AvatarRegistry.
    /// Автоматически собирает все префабы аватаров и добавляет их в spawnPrefabs NetworkManager'а
    /// при изменении списка аватаров в инспекторе.
    /// </summary>
    [CustomEditor(typeof(AvatarRegistry))]
    public class AvatarRegistryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();

            // Отрисовываем стандартный инспектор со списком аватаров
            base.OnInspectorGUI();

            bool hasChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("Network Integration", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Validate NetworkManager Prefabs", GUILayout.Height(30)))
            {
                ValidatePrefabs((AvatarRegistry)target, true);
            }
            
            EditorGUILayout.HelpBox("При изменении списка аватаров скрипт автоматически попытается зарегистрировать их в NetworkManager.spawnPrefabs.", MessageType.Info);

            if (hasChanged)
            {
                ValidatePrefabs((AvatarRegistry)target, false);
            }
        }

        private void ValidatePrefabs(AvatarRegistry registry, bool forceLog)
        {
            var prefabsToRegister = new HashSet<GameObject>();

            // Собираем все префабы из реестра
            if (registry.avatars != null)
            {
                foreach (var avatarData in registry.avatars)
                {
                    if (avatarData != null && avatarData.prefab != null)
                    {
                        prefabsToRegister.Add(avatarData.prefab);
                    }
                }
            }

            // Ищем NetworkManager в активной сцене
            var netManager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            
            // Если в сцене нет, пробуем найти среди префабов проекта
            if (netManager == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:GameObject --- MANAGERS ---");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        netManager = prefab.GetComponentInChildren<NetworkManager>(true);
                    }
                }
                
                // Fallback (search all prefabs)
                if (netManager == null)
                {
                    string[] allGuids = AssetDatabase.FindAssets("t:Prefab");
                    foreach (string guid in allGuids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab != null)
                        {
                            var nm = prefab.GetComponentInChildren<NetworkManager>(true);
                            if (nm != null)
                            {
                                netManager = nm;
                                break;
                            }
                        }
                    }
                }
            }

            if (netManager == null)
            {
                if (forceLog) Debug.LogError("[AvatarRegistry] Не удалось найти NetworkManager ни в текущей открытой сцене, ни в префабах. Откройте сцену с NetworkManager и попробуйте снова.");
                return;
            }

            bool changed = false;

            // Сначала удаляем NULL-элементы (чистим мусор)
            if (netManager.spawnPrefabs.Any(p => p == null))
            {
                netManager.spawnPrefabs.RemoveAll(p => p == null);
                changed = true;
            }

            foreach (var prefab in prefabsToRegister)
            {
                var netId = prefab.GetComponent<NetworkIdentity>();
                if (netId == null)
                {
                    Debug.LogWarning($"[AvatarRegistry] Префаб '{prefab.name}' не имеет компонента NetworkIdentity! Он не сможет синхронизироваться по сети Mirror.");
                    continue;
                }

                if (!netManager.spawnPrefabs.Contains(prefab))
                {
                    netManager.spawnPrefabs.Add(prefab);
                    if (forceLog || changed) Debug.Log($"[AvatarRegistry] Добавлен аватара '{prefab.name}' в список spawnable prefabs NetworkManager-а.");
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(netManager.gameObject);
                if (!netManager.gameObject.scene.IsValid()) // Значит, это префаб
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(netManager.gameObject);
                }
                else
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(netManager.gameObject.scene);
                }
                
                AssetDatabase.SaveAssets(); // Сохраняем изменения ассетов
                if (forceLog) Debug.Log($"[AvatarRegistry] Валидация успешна. NetworkManager был обновлён ({netManager.gameObject.name}).");
            }
            else
            {
                if (forceLog) Debug.Log("[AvatarRegistry] Валидация успешна. Все аватары уже зарегистрированы и корректны.");
            }
        }
    }
}
#endif
