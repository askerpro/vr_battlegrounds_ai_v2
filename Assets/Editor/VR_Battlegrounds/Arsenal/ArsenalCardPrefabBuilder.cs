using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    ///     Материал подложки карточки в префабах слотов. Позу и размер карточки задаёт раскладка оружия при сборке
    ///     слота; здесь — только внешний вид общей карточки шаблона.
    /// </summary>
    public static class ArsenalCardPrefabBuilder
    {
        private const string MaterialPath = "Assets/Art/ArsenalBoundary/WeaponCardBacking.mat";

        public static void Build()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Arsenal/Slots" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var slot in root.GetComponentsInChildren<ArsenalSlotController>(true)) Ensure(slot);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            // Prefab уже сохранён адресно; native backing — единственный дополнительный output.
            var backing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (backing != null) AssetDatabase.SaveAssetIfDirty(backing);
        }

        public static ArsenalPriceTag Ensure(ArsenalSlotController slot)
        {
            var card = slot.GetComponentInChildren<ArsenalPriceTag>(true);
            if (card == null) throw new InvalidOperationException("В префабе слота нет карточки WeaponCard: " + slot.name);
            var renderer = card.transform.Find("CardBacking").GetComponent<Renderer>();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/ArsenalBoundary"))
                    throw new InvalidOperationException("Не найдена папка Assets/Art/ArsenalBoundary.");
                material = new Material(renderer.sharedMaterial) { name = "WeaponCardBacking" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            renderer.sharedMaterial = material;
            // Чистый участок картонного атласа без рваных краёв и чужих надписей.
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/env_packs/TheSewers/URP/Art/Textures/T_Cardboard_01a_ALB.png"));
            material.SetTextureScale("_BaseMap", new Vector2(.24f,.22f));
            material.SetTextureOffset("_BaseMap", new Vector2(.72f,.74f));
            material.SetColor("_BaseColor", new Color(.94f,.93f,.85f));
            material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", .08f);
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            if (slot.WeaponData != null) card.Show(slot.WeaponData, true);
            else card.Show("Оружие", 0, true);
            EditorUtility.SetDirty(card);
            return card;
        }
    }
}
