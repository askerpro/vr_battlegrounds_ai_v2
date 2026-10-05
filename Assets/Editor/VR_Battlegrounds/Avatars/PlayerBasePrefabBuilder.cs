using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Утилита для создания чистого PlayerBase.prefab из существующего рабочего аватара.
    /// Дублирует текущий PlayerControllersCyborgAvatar как независимый (НЕ variant) prefab,
    /// чтобы он мог служить базой для Prefab Variants с разными скинами.
    /// </summary>
    public static class PlayerBasePrefabBuilder
    {
        private const string SourcePrefabPath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        private const string OutputPrefabPath = "Assets/Prefabs/Player/PlayerBase.prefab";

        public static void CreatePlayerBase()
        {
            // 1. Загружаем исходный prefab
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                GameLog.Player.Error($"[PlayerBasePrefabBuilder] Не найден исходный prefab: {SourcePrefabPath}");
                return;
            }

            // 2. Проверяем, не существует ли уже целевой файл
            var existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
            if (existingAsset != null)
            {
                if (!EditorUtility.DisplayDialog(
                    "PlayerBase уже существует",
                    $"Файл {OutputPrefabPath} уже существует.\nПерезаписать?",
                    "Да, перезаписать",
                    "Отмена"))
                {
                    return;
                }
            }

            // 3. Инстанцируем исходный prefab на сцену (полностью распаковывая)
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            if (instance == null)
            {
                GameLog.Player.Error("[PlayerBasePrefabBuilder] Не удалось инстанцировать prefab.");
                return;
            }

            // 4. Полностью распаковываем, чтобы разорвать связь с CyborgAvatar_URP.prefab
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // 5. Переименовываем корень
            instance.name = "PlayerBase";

            // 6. Сохраняем как новый чистый prefab (не variant!)
            bool success;
            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, OutputPrefabPath, out success);

            // 7. Удаляем экземпляр со сцены
            Object.DestroyImmediate(instance);

            if (success && savedPrefab != null)
            {
                GameLog.Player.Info($"[PlayerBasePrefabBuilder] ✅ PlayerBase.prefab создан: {OutputPrefabPath}");
                GameLog.Player.Info("[PlayerBasePrefabBuilder] Все связи с CyborgAvatar_URP.prefab разорваны.");
                GameLog.Player.Info("[PlayerBasePrefabBuilder] Теперь можно создавать Prefab Variants с разными скинами.");

                // Выделяем созданный prefab в Project
                Selection.activeObject = savedPrefab;
                EditorGUIUtility.PingObject(savedPrefab);
            }
            else
            {
                GameLog.Player.Error("[PlayerBasePrefabBuilder] ❌ Ошибка при сохранении PlayerBase.prefab");
            }
        }

        private static bool ValidateCreatePlayerBase()
        {
            // Меню доступно только если исходный prefab существует
            return AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) != null;
        }
    }
}
