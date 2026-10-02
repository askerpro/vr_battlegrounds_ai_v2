using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Сохраняет реальные карточки в префабах слотов; игра использует эти же объекты.</summary>
    public static class ArsenalCardPrefabBuilder
    {
        private const string MaterialPath = "Assets/Art/ArsenalBoundary/WeaponCardBacking.mat";

        [MenuItem("Tools/VR Battlegrounds/Arsenal/Add Cards To Slot Prefabs")]
        public static void Build()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Arsenal/Slots" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var slot in root.GetComponentsInChildren<ArsenalSlotController>(true)) Ensure(slot, true);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }

        public static ArsenalPriceTag Ensure(ArsenalSlotController slot, bool configureDefault = false)
        {
            if (configureDefault || !slot.HasCustomCardPresentation)
            {
                foreach (var board in slot.GetComponentsInChildren<Transform>(true))
                {
                    if (board.name != "PegboardSection") continue;
                    bool underside = Vector3.Dot(board.TransformDirection(Vector3.back), Vector3.up) < -.5f;
                    Vector3 point = board.TransformPoint(new Vector3(.3f, .25f, underside ? .54f : -.54f));
                    slot.ConfigureCardPresentation(slot.transform.InverseTransformPoint(point),new Vector2(.15f,.16f),.16f,
                        Quaternion.Inverse(slot.transform.rotation) * board.rotation *
                        (underside ? Quaternion.Euler(180f,0f,0f) : Quaternion.identity));
                    break;
                }
            }
            var card = ArsenalPriceTag.Create(slot);
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
                "Assets/HIVEMIND/TheSewers/URP/Art/Textures/T_Cardboard_01a_ALB.png"));
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
