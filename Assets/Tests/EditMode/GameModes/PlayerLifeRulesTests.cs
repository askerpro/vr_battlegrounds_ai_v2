using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Урон по игроку — правило режима (<see cref="GameMode.PlayersTakeDamage"/>).
    ///
    /// <para>
    /// Что доказывает. В разминке (и в лобби) смерти нет: урон по игроку отменяется на
    /// <c>UxrActor.DamageReceiving</c>, и решает это режим, а не проверка «мы в лобби»
    /// в игроке. В Elimination урон проходит как раньше.
    /// </para>
    /// </summary>
    public class PlayerDamageRuleTests : MirrorTestHarness
    {
        private UxrActor CreateAvatar()
        {
            GameObject go = CreateNetworkObject("Avatar");
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            actor.Life = 100f;
            return actor;
        }

        private void ActivateMode<T>() where T : GameMode
        {
            MapReferee manager = CreateNetworkComponent<MapReferee>("MapReferee");
            InvokeLifecycleMethod(manager, "Awake");
            InvokePrivateMethod(manager, "RegisterActiveGameMode", CreateNetworkComponent<T>(typeof(T).Name));
        }

        [Test]
        public void В_разминке_урон_по_игроку_не_проходит()
        {
            SilenceMirrorNoise();

            UxrActor actor = CreateAvatar();
            ActivateMode<WarmupMode>();

            actor.ReceiveDamage(30f);
            actor.ReceiveDamage(500f);

            Assert.AreEqual(100f, actor.Life, 0.01f, "В разминке урон по игроку прошёл — а смерти в разминке нет.");
            Assert.IsFalse(actor.IsDead);
        }

        [Test]
        public void В_Elimination_урон_проходит()
        {
            SilenceMirrorNoise();

            UxrActor actor = CreateAvatar();
            ActivateMode<EliminationMode>();

            actor.ReceiveDamage(30f);

            Assert.AreEqual(70f, actor.Life, 0.01f, "В матче урон по игроку не прошёл.");
        }
    }

    /// <summary>
    /// Респавн восстанавливает состояние и никогда не двигает игрока.
    ///
    /// <para>
    /// Что доказывает. Игрок физически стоит в зале, его место задано калибровкой.
    /// Раньше <c>PlayerController.Respawn(spawnPoint)</c> рассылал <c>RpcOnRespawned</c>,
    /// и владелец переносил аватар на точку зоны — картинка расклеивалась с телом.
    /// Ярус B (хост): только так <c>ClientRpc</c> реально исполняется в тесте, и старое
    /// перемещение было видно.
    /// </para>
    /// </summary>
    public class RespawnKeepsPositionTests : MirrorTestHarness
    {
        protected override bool NeedsLocalClient => true;

        private readonly List<TeamData> _teams = new List<TeamData>();

        [TearDown]
        public void DropTeams()
        {
            foreach (TeamData t in _teams) if (t != null) Object.DestroyImmediate(t);
            _teams.Clear();
        }

        [Test]
        public void Респавн_в_зоне_не_меняет_позицию_аватара()
        {
            SilenceMirrorNoise();

            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.teamIndex = 911;
            team.displayName = "A";
            _teams.Add(team);

            PlayersManager players = CreateManager<PlayersManager>("PlayersManager");

            EliminationMode mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(mode);
            mode.Initialize(new[] { team });

            GameObject zoneObject = CreateObject("Zone");
            zoneObject.transform.position = new Vector3(10f, 0f, 0f);
            TeamSpawnZone zone = zoneObject.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", team);

            PlayerSession session = CreateNetworkComponent<PlayerSession>("Session");
            SpawnOnServer(session);
            session.TeamIndex = team.teamIndex;
            players.RegisterSession(new Mirror.NetworkConnectionToClient(77), session);

            GameObject avatarObject = CreateNetworkObject("Avatar");
            UxrActor actor = avatarObject.AddComponent<UxrActor>();
            PlayerController player = avatarObject.AddComponent<PlayerController>();
            EnableNetworking(avatarObject);
            player._actor = actor;
            Vector3 physicalPlace = new Vector3(1f, 0f, 2f);
            avatarObject.transform.position = physicalPlace;
            SpawnOnServer(player);
            PumpNetwork();

            session.ActiveAvatar = player;
            actor.Life = 0f;

            mode.PrepareNextRound();

            // Игрок сам дошёл до зоны — поднимаем событие входа так же, как триггер.
            var entered = (System.Delegate)typeof(TeamSpawnZone)
                .GetField("PlayerEntered", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(zone);
            Assert.IsNotNull(entered, "Контроль: отложенный респавн завёл подписку на вход в зону.");
            entered.DynamicInvoke(zone, player);
            PumpNetwork();

            Assert.IsTrue(player.IsAlive, "Контроль: игрок возродился.");
            Assert.AreEqual(physicalPlace, avatarObject.transform.position,
                "Респавн переместил аватар. Игрок стоит в зале, его место задано калибровкой — " +
                "респавн восстанавливает только состояние.");
        }
    }
}
