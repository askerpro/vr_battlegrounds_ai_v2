using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Единый доступ к выбранным копиям оплаченного URP-пака; ассеты здесь не изменяются.</summary>
    public static class LobbyMilitaryCampSources
    {
        public const string Root = "Assets/Art/Lobby/Imported/MilitaryCamp";
        // Один центр окна для визуальной шторы, рамки и запретного объёма мебели.
        public static float SideWindowOffset(float side) => side < 0 ? -6.20f : 6f;
        public static GameObject Prefab(string stem) => AssetDatabase.LoadAssetAtPath<GameObject>(Find(stem, "Prefab"));
        public static Texture2D Texture(string stem) => AssetDatabase.LoadAssetAtPath<Texture2D>(Find(stem, "Texture"));
        private static string Find(string stem, string type)
        {
            if (!AssetDatabase.IsValidFolder(Root)) throw new InvalidOperationException("MilitaryCamp URP source ещё не импортирован.");
            var matches = AssetDatabase.FindAssets(stem + " t:" + type, new[] { Root }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == stem).Distinct().ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Нужен один MilitaryCamp asset: " + stem);
            return matches[0];
        }
        public static Bounds TentBounds(Bounds physicalFloor)
        {
            // Пользовательское правило: ширина по арене, торцы на 2 м внутри с каждой стороны.
            var size = physicalFloor.size;size.z -= 4;
            if (size.z <= 0) throw new InvalidOperationException("Физическая арена короче буферов шатра.");
            return new Bounds(physicalFloor.center, size);
        }
    }
}
