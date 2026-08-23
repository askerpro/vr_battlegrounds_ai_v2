using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Выбор точки спавна аватара — находка <b>WPN-03</b>.
    ///
    /// <para>
    /// Что доказывают тесты. <c>AvatarManager</c> спрашивал точку только у Mirror
    /// (<c>NetworkManager.GetStartPosition</c>), а на картах проекта нет ни одного
    /// <c>NetworkStartPosition</c> — то есть ответ всегда был «ничего нет», и аватар
    /// оказывался в начале координат. Единственные точки, которые на картах есть,
    /// это <see cref="TeamSpawnZone"/>, и они знают свою команду.
    /// </para>
    ///
    /// <para>
    /// Ярус A (<see cref="MirrorTestHarness"/>) нужен здесь ради уборки созданных
    /// объектов между тестами: зона, оставшаяся в сцене, портит следующий тест —
    /// <c>FindObjectsByType</c> найдёт и её. Сеть в самих проверках не участвует.
    /// </para>
    /// </summary>
    public class AvatarSpawnPointResolverTests : MirrorTestHarness
    {
        /// <summary>Зона нужной команды в заданной точке. Awake не нужен: резолвер читает только Team и transform.</summary>
        private TeamSpawnZone CreateZone(string name, TeamData team, Vector3 position)
        {
            GameObject go = CreateObject(name);
            go.transform.position = position;
            go.AddComponent<BoxCollider>();

            TeamSpawnZone zone = go.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", team);

            return zone;
        }

        private static TeamData CreateTeam(string displayName, int index)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.displayName = displayName;
            team.teamIndex = index;
            return team;
        }

        [Test]
        public void Точка_спавна_берётся_у_зоны_своей_команды()
        {
            SilenceMirrorNoise();

            TeamData red  = CreateTeam("Красные", 1);
            TeamData blue = CreateTeam("Синие", 2);

            CreateZone("ЗонаКрасных", red,  new Vector3(10f, 0f, 0f));
            CreateZone("ЗонаСиних",   blue, new Vector3(-10f, 0f, 0f));

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(blue);

            Assert.AreEqual(AvatarSpawnPointSource.TeamSpawnZone, point.Source,
                "Зона нужной команды стоит на сцене, а резолвер её не увидел.");
            Assert.AreEqual(new Vector3(-10f, 0f, 0f), point.Position,
                "Выбрана зона не той команды: точка спавна должна совпасть с зоной 'Синих'.");
        }

        [Test]
        public void Без_зоны_своей_команды_остаётся_начало_координат()
        {
            SilenceMirrorNoise();

            TeamData red  = CreateTeam("Красные", 1);
            TeamData blue = CreateTeam("Синие", 2);

            // На карте есть чужая зона и нет своей — ровно тот случай, когда художник
            // забыл поставить базу одной из команд.
            CreateZone("ЗонаКрасных", red, new Vector3(10f, 0f, 0f));

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(blue);

            Assert.AreEqual(AvatarSpawnPointSource.WorldOrigin, point.Source,
                "Чужая зона не имеет права стать точкой спавна: игрок появится в базе противника. " +
                "NetworkStartPosition на сцене теста нет, значит остаётся начало координат.");
            Assert.AreEqual(Vector3.zero, point.Position);
        }

        [Test]
        public void Без_команды_зоны_не_спрашиваются_вовсе()
        {
            SilenceMirrorNoise();

            CreateZone("ЗонаКрасных", CreateTeam("Красные", 1), new Vector3(10f, 0f, 0f));

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(null);

            Assert.AreEqual(AvatarSpawnPointSource.WorldOrigin, point.Source,
                "Команда не выбрана (подключение в лобби до выбора стороны) — брать чужую зону нельзя.");
        }

        [Test]
        public void Резолвер_переживает_карту_без_единой_зоны()
        {
            SilenceMirrorNoise();

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(CreateTeam("Красные", 1));

            Assert.AreEqual(AvatarSpawnPointSource.WorldOrigin, point.Source);
            Assert.AreEqual(Quaternion.identity, point.Rotation,
                "Запасной вариант обязан быть определённым, а не мусорным поворотом.");
        }

        [Test]
        public void Точка_спавна_наследует_поворот_зоны()
        {
            SilenceMirrorNoise();

            TeamData red = CreateTeam("Красные", 1);
            TeamSpawnZone zone = CreateZone("ЗонаКрасных", red, new Vector3(3f, 0f, 4f));
            zone.transform.rotation = Quaternion.Euler(0f, 137f, 0f);

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(red);

            Assert.AreEqual(zone.transform.rotation.eulerAngles.y, point.Rotation.eulerAngles.y, 0.01f,
                "Игрок обязан появиться лицом туда же, куда развёрнута его база, " +
                "иначе он смотрит в стену собственного спавна.");
        }
    }
}
