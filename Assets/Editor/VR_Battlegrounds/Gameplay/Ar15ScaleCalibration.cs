using System;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Размер AR15 из контакта рукоятки с TR15. Только корни оружия и внешнего магазина.</summary>
    public static class Ar15ScaleCalibration
    {
        public const float RootScale = 0.70f;
        public const string WeaponPath = "Assets/Prefabs/Weapons/AR15/AR15.prefab";
        public const string MagazinePath = "Assets/Prefabs/Weapons/AR15/AR15_Magazine.prefab";

        public static string[] InputPaths() => new[] { WeaponPath, MagazinePath };
        public static string[] OutputPaths() => new[] { WeaponPath, MagazinePath };

        /// <summary>Read-only: проверяет оба existing prefab assets без создания сцены или Save.</summary>
        public static string Preflight()
        {
            foreach (string path in InputPaths())
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long rootId) || rootId == 0)
                    throw new InvalidOperationException("Нет persistent prefab root: " + path);
                Validate(asset, path == WeaponPath);
            }
            return "AR15: weapon/external magazine rootScale=.70; installed reciprocal localScale=1";
        }

        public static void Apply()
        {
            WeaponModelPreviewScope.CheckEditor();
            Preflight();
            // Оба входа проверяем до первого Save. Повтор после частичного отказа задаёт тот же абсолютный размер.
            foreach (string path in new[] { MagazinePath, WeaponPath })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new InvalidOperationException("Нет префаба калибровки AR15: " + path);
                var root = PrefabUtility.LoadPrefabContents(path);
                try { Validate(root, path == WeaponPath); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            ApplyRoot(MagazinePath, false);
            ApplyRoot(WeaponPath, true);
        }

        private static void ApplyRoot(string path, bool weapon)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long rootId))
                throw new InvalidOperationException("Нет GUID/root fileID: " + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Validate(root, weapon);
                Vector3 target = Vector3.one * RootScale;
                if ((root.transform.localScale - target).sqrMagnitude < 1e-10f) return;
                root.transform.localScale = target;
                if (weapon && (root.GetComponentInChildren<UxrFirearmMag>(true).transform.lossyScale - target).sqrMagnitude > 1e-8f)
                    throw new InvalidOperationException("AR15: размер стартового магазина не совпал с внешним магазином");
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                    throw new InvalidOperationException("AR15: сохранение префаба не удалось: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(saved, out string afterGuid, out long afterId) ||
                guid != afterGuid || rootId != afterId)
                throw new InvalidOperationException("AR15 calibration нарушила GUID/root fileID: " + path);
        }

        private static void Validate(GameObject root, bool weapon)
        {
            Vector3 scale = root.transform.localScale;
            if (scale.x <= 0 || Mathf.Abs(scale.x - scale.y) > 1e-5f || Mathf.Abs(scale.y - scale.z) > 1e-5f)
                throw new InvalidOperationException("AR15: исходный корень должен иметь положительный равномерный масштаб");
            if (!weapon) return;
            var installed = root.GetComponentsInChildren<UxrFirearmMag>(true);
            if (installed.Length != 1 || (installed[0].transform.localScale - Vector3.one).sqrMagnitude > 1e-8f ||
                (installed[0].transform.lossyScale - scale).sqrMagnitude > 1e-8f)
                throw new InvalidOperationException("AR15: стартовый магазин должен иметь reciprocal localScale=1 без дополнительного масштаба родителей");
        }
    }
}
