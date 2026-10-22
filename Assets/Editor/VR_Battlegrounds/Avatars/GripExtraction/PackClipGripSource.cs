using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Core;
using VrBattlegrounds.Editor.Gameplay;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Источник «кадр клипа пака»: рецепты и экстракторы остаются у нынешних владельцев —
    /// <see cref="HandsPackPoseImporter.Recipes" /> + <see cref="HandsPackPoseExtractor" /> (пак Hands, 13 точек) и
    /// <see cref="KinemationWeaponBuilder.PoseRecipes" /> по <see cref="KinemationWeaponBuilder.All" /> +
    /// <see cref="KinemationPoseExtractor" /> (KINEMATION, 16 точек). Адаптер только переводит кадр в <see cref="GripSample" />.
    ///
    /// <para>
    /// Селекторы: <c>all</c>, <c>hands</c>, <c>kinemation</c>, имя позы (<c>HandsPack_Scar_Grip</c>) или имя префаба
    /// оружия (<c>Scar</c>, <c>AK105</c>); регистр не важен.
    /// </para>
    /// </summary>
    public sealed class PackClipGripSource : IGripSource
    {
        public const string SourceId = "pack";
        public const string Hands = "hands";
        public const string Kinemation = "kinemation";

        private KinemationWeapon _kinemationModel;
        private string           _kinemationName;

        public string Id => SourceId;

        /// <summary>Все точки обоих паков в порядке рецептов (точки одного оружия идут подряд).</summary>
        public static IEnumerable<GripSourcePoint> AllPoints()
        {
            foreach (HandsPackPoseRecipe recipe in HandsPackPoseImporter.Recipes)
            {
                yield return FromRecipe(recipe, Hands, null);
            }

            foreach (KinemationWeaponRecipe weapon in KinemationWeaponBuilder.All)
            {
                foreach (HandsPackPoseRecipe recipe in KinemationWeaponBuilder.PoseRecipes(weapon))
                {
                    yield return FromRecipe(recipe, Kinemation, weapon);
                }
            }
        }

        public IReadOnlyList<GripSourcePoint> Select(IReadOnlyCollection<string> selectors)
        {
            if (selectors == null || selectors.Count == 0)
            {
                throw new ArgumentException("recipes: пусто; укажите all, hands, kinemation, имя позы или имя оружия.");
            }

            List<GripSourcePoint> all    = AllPoints().ToList();
            var                   result = new List<GripSourcePoint>();
            foreach (string selector in selectors)
            {
                List<GripSourcePoint> matched = all.Where(p => Matches(p, selector)).ToList();
                if (matched.Count == 0)
                {
                    throw new ArgumentException($"recipes: «{selector}» не совпал ни с одной точкой паков.");
                }

                result.AddRange(matched.Where(p => !result.Contains(p)));
            }

            return result;
        }

        public GripSample Sample(GripSourcePoint point)
        {
            HandsPackPoseExtractor.Sample sample = point.KinemationRecipe != null
                                                       ? KinemationModel(point.KinemationRecipe).GripSample(point.SourceSide)
                                                       : HandsPackPoseImporter.ExtractSample(point.PackRecipe);
            return GripSample.FromPack(sample, point.SourceSide, point.SourceKind);
        }

        public void Dispose() => ReleaseKinemationModel();

        private static bool Matches(GripSourcePoint point, string selector) =>
            Same(selector, "all") || Same(selector, point.SourceKind) || Same(selector, point.Pose) ||
            Same(selector, Path.GetFileNameWithoutExtension(point.Weapon));

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static GripSourcePoint FromRecipe(HandsPackPoseRecipe recipe, string kind, KinemationWeaponRecipe weapon) =>
            new GripSourcePoint
            {
                Pose             = recipe.Pose,
                SourceKind       = kind,
                Weapon           = recipe.Weapon,
                BodyPath         = recipe.BodyPath,
                GrabPoint        = recipe.GrabPoint,
                SourceSide       = recipe.PackSide,
                LegacyPoseFolder = recipe.PoseFolder ?? HandsPackPoseImporter.PoseFolder,
                AlignSource      = HandsPackPoseImporter.AlignSource,
                PackRecipe       = recipe,
                KinemationRecipe = weapon
            };

        /// <summary>Модель ствола KINEMATION держится, пока идут его точки (кадр тяжёлый: превью-сцена пака).</summary>
        private KinemationWeapon KinemationModel(KinemationWeaponRecipe weapon)
        {
            if (_kinemationModel != null && _kinemationName == weapon.Weapon.Name)
            {
                return _kinemationModel;
            }

            ReleaseKinemationModel();
            // Те же аргументы, что приватный KinemationWeaponBuilder.Model(k): при его изменении поправить и здесь.
            _kinemationModel = new KinemationWeapon(weapon.PackPrefab, weapon.AnimFolder, weapon.PoseClip, weapon.Weapon.Name, weapon.Weapon.BodyPart,
                                                    weapon.RestClip, weapon.Attachments);
            _kinemationName = weapon.Weapon.Name;
            return _kinemationModel;
        }

        private void ReleaseKinemationModel()
        {
            _kinemationModel?.Dispose();
            _kinemationModel = null;
            _kinemationName  = null;
        }
    }
}
