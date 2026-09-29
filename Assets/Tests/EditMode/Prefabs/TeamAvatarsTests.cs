using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Раздача аватаров командам — только из реестра (<see cref="RegisteredAvatars"/>).
    ///
    /// <para>
    /// У каждой команды матча (команды режимов, кроме разминки) ровно один аватар — он же по умолчанию,
    /// и у разных команд он разного вида. Цветной вариант зарегистрированного аватара
    /// (<c>TeamColorVariantBuilder</c>) меняет только материалы. Конкретных аватаров тест не знает.
    /// </para>
    /// </summary>
    public class TeamAvatarsTests
    {
        private const string StrategyPath = "Assets/Data/Player/Avatars/TeamAvatarStrategy.asset";
        private const string ManagersPrefabPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";

        /// <summary>Команды матча: команды всех режимов (у разминки команд нет).</summary>
        public static IEnumerable<TeamData> MatchTeams() =>
            AssetDatabase.FindAssets("t:GameModeData")
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<GameModeData>)
                         .Where(m => m != null && m.teams != null)
                         .SelectMany(m => m.teams)
                         .Where(t => t != null)
                         .Distinct()
                         .OrderBy(t => t.teamIndex);

        /// <summary>Пары «цветной вариант → зарегистрированный родитель» внутри реестра.</summary>
        public static IEnumerable<string> RegisteredVariants() =>
            RegisteredAvatars.Prefabs()
                             .Where(p => RegisteredAvatars.Contains(PrefabUtility.GetCorrespondingObjectFromSource(p)))
                             .Select(AssetDatabase.GetAssetPath)
                             .OrderBy(p => p);

        [Test]
        public void В_реестре_есть_аватары_с_префабом_и_именем()
        {
            List<AvatarData> data = RegisteredAvatars.Data().ToList();
            Assert.That(data, Is.Not.Empty, "Реестр аватаров пуст");
            Assert.That(data.Where(a => a.prefab == null).Select(a => a.name), Is.Empty, "AvatarData без префаба");
            Assert.That(data.Where(a => string.IsNullOrEmpty(a.displayName)).Select(a => a.name), Is.Empty, "AvatarData без имени");
        }

        [Test]
        public void Команды_матча_найдены()
        {
            Assert.That(MatchTeams().Count(), Is.GreaterThanOrEqualTo(2), "У режимов матча меньше двух команд — сравнивать нечего");
        }

        [TestCaseSource(nameof(MatchTeams))]
        public void У_команды_матча_один_зарегистрированный_аватар(TeamData team)
        {
            Assert.That(team.avatars.Count, Is.EqualTo(1), $"{team.displayName}: аватаров {team.avatars.Count}, нужен один.");
            Assert.That(team.avatars[0], Is.Not.Null, $"{team.displayName}: пустой слот аватара.");
            Assert.That(RegisteredAvatars.Data(), Does.Contain(team.avatars[0]), $"{team.displayName}: аватар не из реестра.");
        }

        [Test]
        public void У_команд_матча_разный_вид()
        {
            var looks = MatchTeams().Select(t => (team: t.displayName, look: Look(t.GetAvatarPrefab(0)))).ToList();

            for (int i = 0; i < looks.Count; i++)
            for (int j = i + 1; j < looks.Count; j++)
            {
                Assert.That(looks[i].look.SetEquals(looks[j].look), Is.False,
                            $"У команд '{looks[i].team}' и '{looks[j].team}' одинаковые материалы тела — в бою их не различить.");
            }
        }

        /// <summary>
        /// Вариант зарегистрированного аватара, сам зарегистрированный, — это цвет: переопределены только
        /// материалы, и шейдер каждого слота тот же, что у родителя (на Quest — только URP Lit).
        /// </summary>
        [TestCaseSource(nameof(RegisteredVariants))]
        public void Цветной_вариант_меняет_только_материалы(string path)
        {
            GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject parent = PrefabUtility.GetCorrespondingObjectFromSource(variant);

            Assert.That(PrefabUtility.GetAddedGameObjects(variant), Is.Empty, "Вариант добавил объекты");
            Assert.That(PrefabUtility.GetAddedComponents(variant), Is.Empty, "Вариант добавил компоненты");
            Assert.That(PrefabUtility.GetRemovedComponents(variant), Is.Empty, "Вариант убрал компоненты");

            var problems = new List<string>();
            Renderer[] own = variant.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in own)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                if (source == null) continue;

                Material[] mine = renderer.sharedMaterials, theirs = source.sharedMaterials;
                if (mine.Length != theirs.Length) { problems.Add($"{renderer.name}: другое число материалов"); continue; }

                for (int i = 0; i < mine.Length; i++)
                {
                    if (mine[i] == theirs[i]) continue;
                    if (mine[i] == null || theirs[i] == null || mine[i].shader != theirs[i].shader)
                        problems.Add($"{renderer.name}[{i}]: шейдер не как у {parent.name}");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(Look(variant).SetEquals(Look(parent)), Is.False, "Вариант не отличается от родителя материалами");
        }

        [Test]
        public void Запасной_префаб_спавна_зарегистрирован()
        {
            var strategy = AssetDatabase.LoadAssetAtPath<TeamAvatarStrategy>(StrategyPath);
            Assert.That(strategy, Is.Not.Null, "Нет " + StrategyPath);

            var fallback = new SerializedObject(strategy).FindProperty("fallbackPrefab").objectReferenceValue as GameObject;
            Assert.That(RegisteredAvatars.Contains(fallback), $"Запасной префаб '{(fallback ? fallback.name : "null")}' не из реестра.");
        }

        [Test]
        public void Все_зарегистрированные_аватары_в_spawnPrefabs()
        {
            NetworkManager manager = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefabPath).GetComponentInChildren<NetworkManager>(true);
            Assert.That(manager, Is.Not.Null);

            string[] missing = RegisteredAvatars.Prefabs().Where(p => !manager.spawnPrefabs.Contains(p)).Select(p => p.name).ToArray();
            Assert.That(missing, Is.Empty, "Клиент не заспавнит аватар без записи в spawnPrefabs.");
        }

        /// <summary>Вид аватара — набор материалов его тела (без моделей кистей UltimateXR).</summary>
        private static HashSet<Material> Look(GameObject avatar)
        {
            Assert.That(avatar, Is.Not.Null);
            return new HashSet<Material>(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                               .Where(s => s.GetComponentInParent<UltimateXR.Avatar.UxrHandIntegration>(true) == null)
                                               .SelectMany(s => s.sharedMaterials)
                                               .Where(m => m != null));
        }
    }
}
