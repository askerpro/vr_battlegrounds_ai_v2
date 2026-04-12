#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mirror;
using System.Collections.Generic;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Кастомный инспектор для WeaponRegistry, добавляющий кнопку валидации NetworkManager.
    /// Автоматически собирает все WeaponPrefab и MagazinePrefab и добавляет их в spawnPrefabs.
    /// Отслеживает отсутствие NetworkIdentity.
    /// </summary>
    [CustomEditor(typeof(WeaponRegistry))]
    public class WeaponRegistryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Отрисовываем стандартный инспектор со списком оружия
            base.OnInspectorGUI();

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("Network Integration", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Validate NetworkManager Prefabs", GUILayout.Height(30)))
            {
                ValidatePrefabs((WeaponRegistry)target);
            }
            EditorGUILayout.HelpBox("Нажмите на кнопку выше, чтобы убедиться, что все префабы оружия и магазинов из этого реестра добавлены в NetworkManager.spawnPrefabs", MessageType.Info);
        }

        private void ValidatePrefabs(WeaponRegistry registry)
        {
            var prefabsToRegister = new HashSet<GameObject>();

            // Собираем все префабы из реестра
            foreach (var weapon in registry.Weapons)
            {
                if (weapon == null) continue;
                
                if (weapon.MagazinePrefab != null) 
                    prefabsToRegister.Add(weapon.MagazinePrefab);
                
                if (weapon.WeaponPrefab != null) 
                    prefabsToRegister.Add(weapon.WeaponPrefab);
            }

            // Ищем NetworkManager в активной сцене
            var netManager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            
            // Если в сцене нет, пробуем найти среди префабов проекта
            if (netManager == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        var nm = prefab.GetComponent<NetworkManager>();
                        if (nm != null)
                        {
                            netManager = nm;
                            break;
                        }
                    }
                }
            }

            if (netManager == null)
            {
                Debug.LogError("[WeaponRegistry] Не удалось найти NetworkManager ни в текущей открытой сцене, ни в префабах. Откройте сцену с NetworkManager и попробуйте снова.");
                return;
            }

            bool changed = false;
            foreach (var prefab in prefabsToRegister)
            {
                var netId = prefab.GetComponent<NetworkIdentity>();
                if (netId == null)
                {
                    Debug.LogWarning($"[WeaponRegistry] Префаб '{prefab.name}' не имеет компонента NetworkIdentity! Он не сможет синхронизироваться по сети Mirror.");
                    continue;
                }

                if (!netManager.spawnPrefabs.Contains(prefab))
                {
                    netManager.spawnPrefabs.Add(prefab);
                    Debug.Log($"[WeaponRegistry] Добавлен '{prefab.name}' в список spawnable prefabs NetworkManager-а.");
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
                Debug.Log($"[WeaponRegistry] Валидация успешна. NetworkManager был обновлён ({netManager.gameObject.name}). Рекомендуется сохранить проект.");
            }
            else
            {
                Debug.Log("[WeaponRegistry] Валидация успешна. Все префабы уже зарегистрированы и корректны.");
            }
        }
    }
}
#endif
