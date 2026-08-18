using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.SpawnZones
{
    /// <summary>
    /// Границы зоны спавна и её устойчивость к недоехавшей сессии — находка VR-05, задача T-16.
    ///
    /// Что доказывают тесты. Уменьшенный <c>Bounds</c> зоны считался один раз в <c>Awake</c>
    /// из мирового AABB коллайдера. Отсюда два отказа: зона, которую подвинули после старта,
    /// проверяла игрока по старому месту, а у повёрнутой зоны AABB заметно больше самой зоны —
    /// точка за гранью считалась внутренней, хотя комментарий обещал точность 5–12 см.
    ///
    /// Третий тест — про <c>Session</c>: между спавном аватара и спавном его сессии
    /// ссылка пуста, и обход игроков в зоне падал с NullReferenceException.
    ///
    /// Почему ярус A (<see cref="MirrorTestHarness"/>): нужны уборка созданных объектов,
    /// ручной вызов <c>Awake</c> (в EditMode Unity его не зовёт) и запись в приватные поля.
    /// Сеть здесь не участвует.
    /// </summary>
    public class TeamSpawnZoneBoundsTests : MirrorTestHarness
    {
        /// <summary>Зона с коробкой заданного размера. Awake — руками, как везде в EditMode.</summary>
        private TeamSpawnZone CreateZone(Vector3 size)
        {
            GameObject go = CreateObject("SpawnZone");

            // RequireComponent добавит BoxCollider сам, но нам нужен он же для настройки размера.
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;

            TeamSpawnZone zone = go.AddComponent<TeamSpawnZone>();

            // Без команды Awake уходит в ветку с предупреждением, а GetTeamPlayersInZone
            // возвращает пустой список независимо от содержимого зоны.
            SetPrivateField(zone, "_team", ScriptableObject.CreateInstance<TeamData>());

            InvokeLifecycleMethod(zone, "Awake");
            return zone;
        }

        /// <summary>Аватар без сессии: <c>SessionNetId</c> нулевой, значит <c>Session</c> вернёт null.</summary>
        private PlayerController CreateAvatarWithoutSession(string name)
        {
            GameObject go = CreateNetworkObject(name);
            go.AddComponent<UxrActor>(); // обязателен по RequireComponent у PlayerController
            PlayerController avatar = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            return avatar;
        }

        /// <summary>Кладёт игрока в приватное множество «внутри зоны» — так же, как это делает OnTriggerStay.</summary>
        private static void PutPlayerInZone(TeamSpawnZone zone, PlayerController player)
        {
            FieldInfo field = typeof(TeamSpawnZone).GetField(
                "_playersInZone", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "В TeamSpawnZone больше нет поля _playersInZone — тест устарел.");

            ((HashSet<PlayerController>)field.GetValue(zone)).Add(player);
        }

        // ── Устаревшие границы ──────────────────────────────────────────────

        [Test]
        public void Зона_проверяет_положение_по_текущему_месту_а_не_по_месту_из_Awake()
        {
            SilenceMirrorNoise();

            TeamSpawnZone zone = CreateZone(new Vector3(4f, 3f, 4f));

            Vector3 head = new Vector3(10f, 0f, 0f);
            Assert.IsFalse(zone.IsHeadCenterInZone(head),
                "Контроль: до переезда зона стоит в начале координат и точка в 10 метрах от неё внешняя.");

            zone.transform.position = head;

            Assert.IsTrue(zone.IsHeadCenterInZone(head),
                "Зону подвинули, и голова игрока теперь ровно в её центре, а зона считает его снаружи. " +
                "Это VR-05: границы посчитаны один раз в Awake и живут по старому месту.");
        }

        [Test]
        public void Зона_следит_за_изменением_размера_коллайдера()
        {
            SilenceMirrorNoise();

            TeamSpawnZone zone = CreateZone(new Vector3(4f, 3f, 4f));

            Vector3 head = new Vector3(0f, 0f, 3f);
            Assert.IsFalse(zone.IsHeadCenterInZone(head),
                "Контроль: точка в 3 метрах от центра не влезает в зону глубиной 4 метра.");

            zone.GetComponent<BoxCollider>().size = new Vector3(4f, 3f, 10f);

            Assert.IsTrue(zone.IsHeadCenterInZone(head),
                "Зону растянули, а проверка идёт по размеру из Awake.");
        }

        // ── Поворот ─────────────────────────────────────────────────────────

        [Test]
        public void Повёрнутая_зона_не_считает_внутренней_точку_за_своей_гранью()
        {
            SilenceMirrorNoise();

            // Узкая коробка: 4 × 3 × 1, развёрнутая на 45° вокруг Y.
            TeamSpawnZone zone = CreateZone(new Vector3(4f, 3f, 1f));
            zone.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            // Точка (1.5, 0, 1.5) в осях самой зоны лежит на локальном (0, 0, 2.12) —
            // то есть в двух метрах от коробки, у которой по этой оси всего полметра.
            // Но в мировой AABB повёрнутой коробки (полуразмер ~1.77 по X и Z) она попадает,
            // поэтому старая проверка через Bounds объявляла её внутренней.
            Assert.IsFalse(zone.IsHeadCenterInZone(new Vector3(1.5f, 0f, 1.5f)),
                "Точка снаружи повёрнутой зоны засчитана как «голова целиком внутри». " +
                "Bounds не знает про поворот, и обещанная точность 5–12 см превращается в метры.");

            // Обратная сторона: своя же локальная точка внутри коробки обязана считаться внутренней,
            // иначе «починка» свелась бы к тому, что внутрь не попадает никто.
            Assert.IsTrue(zone.IsHeadCenterInZone(zone.transform.TransformPoint(new Vector3(1.5f, 0f, 0f))),
                "Точка внутри повёрнутой зоны объявлена внешней — проверка стала бесполезно строгой.");
        }

        [Test]
        public void Центр_зоны_считается_внутренним()
        {
            SilenceMirrorNoise();

            TeamSpawnZone zone = CreateZone(new Vector3(4f, 3f, 4f));
            zone.transform.position = new Vector3(-7f, 2f, 13f);
            zone.transform.rotation = Quaternion.Euler(0f, 137f, 0f);

            Assert.IsTrue(zone.IsHeadCenterInZone(zone.transform.position),
                "Голова в самом центре зоны — самая внутренняя точка, какая бывает.");
        }

        [Test]
        public void Зона_меньше_головы_никого_не_вмещает()
        {
            SilenceMirrorNoise();

            TeamSpawnZone zone = CreateZone(new Vector3(0.05f, 0.05f, 0.05f));

            Assert.IsFalse(zone.IsHeadCenterInZone(zone.transform.position),
                "Зона меньше головы, но кто-то в ней целиком поместился.");
        }

        // ── Аватар без сессии ───────────────────────────────────────────────

        [Test]
        public void Список_игроков_команды_не_падает_на_аватаре_без_сессии()
        {
            SilenceMirrorNoise();

            TeamSpawnZone zone = CreateZone(new Vector3(4f, 3f, 4f));
            PlayerController avatar = CreateAvatarWithoutSession("AvatarWithoutSession");

            Assert.IsNull(avatar.Session,
                "Контроль: аватар не привязан к сессии, иначе тест проверяет не тот путь.");

            PutPlayerInZone(zone, avatar);

            List<PlayerController> inZone = null;
            Assert.DoesNotThrow(() => inZone = zone.GetTeamPlayersInZone(),
                "Аватар в зоне ещё не получил сессию, и обход игроков падает с NRE. " +
                "Окно между спавном аватара и спавном сессии — обычное дело при переподключении.");

            Assert.AreEqual(0, inZone.Count,
                "Игрок без сессии не принадлежит команде зоны и в список попадать не должен.");
        }
    }
}
