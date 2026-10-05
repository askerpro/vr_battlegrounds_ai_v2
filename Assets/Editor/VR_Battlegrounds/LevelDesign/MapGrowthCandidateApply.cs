using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public sealed class MapGrowthApplyResult
    {
        public ReadOnlyCollection<GameObject> CreatedObjects { get; internal set; }
        public ReadOnlyCollection<string> CreatedIds { get; internal set; }
    }

    /// <summary>Атомарно заменяет только явно освобождённые собственные корни и создаёт оценённые рецепты.</summary>
    public static class MapGrowthCandidateApply
    {
        public static string Refusal(Scene target, MapGrowthSceneCapture capture, MapGrowthCandidateResult result)
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return "Применение доступно вне Play Mode, компиляции и импорта.";
            if (capture == null || !capture.CaptureComplete || !target.IsValid() || !target.isLoaded || target != capture.SourceScene)
                return "Выберите исходную загруженную карту этого запуска.";
            if (result == null || !result.Complete || !result.AutomaticRequirementsSatisfied)
                return "Нужен полностью оценённый вариант без нарушений обязательных автоматических требований.";
            if (result.InputVersion != capture.InputVersion || !capture.IsCurrent())
                return "Карта, разметка, профиль или реестр изменились после расчёта. Запустите поиск заново.";
            BlockoutContainerHierarchy.Find(target, out string reason); return reason;
        }
        public static MapGrowthApplyResult Apply(Scene target, MapGrowthSceneCapture capture, MapGrowthCandidateResult result)
        {
            string reason = Refusal(target, capture, result);
            if (reason != null) throw new InvalidOperationException(reason);
            var definitions = BlockoutRegistryFactory.EnsureRegistry().Definitions;
            // Все рецепты разрешаются до первой записи Undo; исключение не оставляет половину набора.
            var resolved = result.Recipes.Select(r => definitions.Single(d => d != null && d.shapeId == r.ShapeId)).ToArray();
            var created = new List<GameObject>();
            if (resolved.Length > 0 || capture.ReplacedRoots.Count > 0)
            {
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Применить вариант выращивателя");
                try
                {
                    var container = BlockoutContainerHierarchy.GetOrCreate(target);
                    var owned = MapGrowthGeneratedOwnership.Available(target).Except(capture.ReplacedRoots).ToList();
                    var set = container.GetComponent<BlockoutGeneratedSet>() ?? Undo.AddComponent<BlockoutGeneratedSet>(container.gameObject);
                    for (int i = 0; i < resolved.Length; i++)
                    {
                        var root = BlockoutRegistryFactory.CreateRecipe(resolved[i], target, result.Recipes[i], capture.AuthorCell);
                        Undo.SetTransformParent(root.transform, container.transform, "Поместить блок в контейнер"); created.Add(root);
                    }
                    // Старые корни удаляются лишь после успешного создания всех новых; весь список и геометрия входят в тот же Undo.
                    Undo.RecordObject(set, "Обновить список выращенных блоков");
                    set.Replace(owned.Concat(created)); EditorUtility.SetDirty(set);
                    foreach (var old in capture.ReplacedRoots) Undo.DestroyObjectImmediate(old);
                    Physics.SyncTransforms(); Undo.CollapseUndoOperations(group); EditorSceneManager.MarkSceneDirty(target);
                }
                catch { Undo.RevertAllDownToGroup(group); Physics.SyncTransforms(); throw; }
            }
            GameLog.Debug.Info("Выращиватель: применён вариант " + result.CandidateId + ", новых блоков " + created.Count + ". Сохранение карты — отдельное действие автора.");
            return new MapGrowthApplyResult { CreatedObjects = created.AsReadOnly(),
                CreatedIds = Array.AsReadOnly(created.Select(g => GlobalObjectId.GetGlobalObjectIdSlow(g).ToString()).ToArray()) };
        }
    }
}
