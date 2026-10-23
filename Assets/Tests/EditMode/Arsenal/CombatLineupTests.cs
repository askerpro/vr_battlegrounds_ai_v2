using System.Linq;
using NUnit.Framework;
using UnityEditor;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Боевой состав карт (решения пользователя 2026-10-22/23, задача arsenal-combat-lineup, принято в шлеме): на игровых
    /// картах — ограниченный набор стволов с разным геймплеем, в Lobby — весь арсенал для разминки.
    /// </summary>
    public sealed class CombatLineupTests
    {
        private const string GameplayPreset = "Assets/Data/Weapons/CurrentGameplayArsenal.asset";
        private const string LobbyPreset = "Assets/Data/Weapons/FullDemoArsenal.asset";
        private const string MapsFolder = "Assets/Data/Maps";

        private static readonly string[] CombatLineup = { "TR15", "Herrington", "MKR9", "SRM12", "SniperRifleAuto", "Viper", "Revolver" };

        private static string[] Ids(string presetPath)
        {
            var preset = AssetDatabase.LoadAssetAtPath<ArsenalPreset>(presetPath);
            Assert.IsNotNull(preset, "Нет пресета " + presetPath);
            return preset.Entries.Select(e => e.Weapon != null ? e.Weapon.WeaponId : "<пусто>").ToArray();
        }

        [Test]
        public void На_картах_только_боевой_состав()
        {
            CollectionAssert.AreEquivalent(CombatLineup, Ids(GameplayPreset));
        }

        [Test]
        public void Mk14_и_старый_SniperRifle_не_на_картах()
        {
            string[] ids = Ids(GameplayPreset);
            CollectionAssert.DoesNotContain(ids, "Mk14", "Mk14 убран из боевого состава (не нравится вид)");
            CollectionAssert.DoesNotContain(ids, "SniperRifle");
        }

        [Test]
        public void В_Lobby_весь_арсенал_с_AX50_Auto_вместо_SniperRifle()
        {
            string[] lobby = Ids(LobbyPreset);
            CollectionAssert.IsSubsetOf(CombatLineup, lobby, "в Lobby есть каждый ствол боевого состава");
            CollectionAssert.DoesNotContain(lobby, "SniperRifle", "болтовой SniperRifle заменён AX-50 Auto");
            Assert.Greater(lobby.Length, CombatLineup.Length, "в Lobby стволов больше, чем на картах — для разминки");
        }

        [Test]
        public void Игровые_карты_берут_боевой_пресет_Lobby_полный()
        {
            var gameplay = AssetDatabase.LoadAssetAtPath<ArsenalPreset>(GameplayPreset);
            var lobby = AssetDatabase.LoadAssetAtPath<ArsenalPreset>(LobbyPreset);
            var maps = AssetDatabase.FindAssets("t:MapData", new[] { MapsFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<MapData>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            Assert.IsNotEmpty(maps);
            foreach (MapData map in maps)
            {
                ArsenalPreset expected = map.sceneName == "Lobby" ? lobby : gameplay;
                Assert.AreSame(expected, map.arsenalPreset, $"{map.name} ({map.sceneName}): пресет арсенала");
            }
        }
    }
}
