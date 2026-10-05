using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Единая политика контейнера для кисти и Apply выращивателя.</summary>
    public static class BlockoutContainerHierarchy
    {
        public static BlockoutSceneContainer Find(Scene scene, out string reason)
        {
            reason = null;
            if (!scene.IsValid() || !scene.isLoaded) { reason = "Сцена блокаута не загружена."; return null; }
            var containers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BlockoutSceneContainer>(true)).ToArray();
            if (containers.Length > 1) { reason = "В сцене несколько контейнеров блокаута. Оставьте один перед новым размещением."; return null; }
            var container = containers.FirstOrDefault();
            if (container != null && (container.transform.parent != null || container.transform.position.sqrMagnitude > .00000001f
                || Quaternion.Angle(container.transform.rotation, Quaternion.identity) > .001f || (container.transform.localScale - Vector3.one).sqrMagnitude > .00000001f))
            { reason = "Контейнер блокаута должен быть корневым, с позицией 0, поворотом 0 и масштабом 1. Существующие объекты не перемещены."; return null; }
            return container;
        }
        public static BlockoutSceneContainer GetOrCreate(Scene scene)
        {
            var container = Find(scene, out string reason);
            if (reason != null) throw new InvalidOperationException(reason);
            if (container != null) return container;
            var root = new GameObject("Блокаут"); SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Создать контейнер блокаута");
            return Undo.AddComponent<BlockoutSceneContainer>(root);
        }
    }
}
