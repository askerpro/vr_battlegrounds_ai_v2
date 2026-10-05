using System;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Bots;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Core;
using BlazeAISpace;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>Владеет только акторами отдельного стенда. Сервер/реестры ведёт Editor-адаптер.</summary>
    public sealed class BotStandSession : IDisposable
    {
        private PlayerSession _target;
        private readonly System.Collections.Generic.List<PlayerSession> _ownedBots = new System.Collections.Generic.List<PlayerSession>();
        public PlayerSession Subject { get; private set; }
        public PlayerSession Target => _target;
        public System.Collections.Generic.IReadOnlyList<PlayerSession> OwnedBots => _ownedBots;
        private BotStandScenarioId _id;
        private BotStandRunOptions _options;
        private int _configuredRig;
        private bool _targetSeen;
        private Collider[] _hiddenTargetColliders;

        public void Begin(BotStandScenarioId id, WeaponInfo weapon, BotStandRunOptions options)
        {
            _id = id; _options = options;
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority)
                throw new InvalidOperationException("Стенду нужна серверная authority.");
            if (BotDirector.Instance != null && BotDirector.Instance.Bots.Count != 0)
                throw new InvalidOperationException("Есть чужие боты — стенд их не удаляет.");
            var director = BotDirector.EnsureInstance();
            Subject = director.AddBot();
            if (Subject != null) _ownedBots.Add(Subject);
            if (Subject == null || Subject.ActiveAvatar == null) throw new InvalidOperationException("Бот не создан.");
            if (options.AvatarIndex >= 0)
                AvatarManager.Instance.ChangeAvatar(null, Subject, Subject.TeamIndex, options.AvatarIndex);
            Place(Subject, id == BotStandScenarioId.T03 || id == BotStandScenarioId.T04 || id == BotStandScenarioId.T05 ? new Vector3(-3.5f, 0f, -4f) : new Vector3(0f, 0f, -5f), 0f);
            if (weapon != null) Subject.ActiveAvatar.GetComponent<BotGunner>().Arm(weapon);

            if (id == BotStandScenarioId.T02)
            {
                var other = director.AddBot();
                if(other!=null) _ownedBots.Add(other);
                if(other?.ActiveAvatar==null) throw new InvalidOperationException("Нет второго бота.");
                Place(other, new Vector3(0f, 0f, 5f), 180f);
                if (weapon != null) other.ActiveAvatar.GetComponent<BotGunner>().Arm(weapon);
                return;
            }
            if (id == BotStandScenarioId.V01) return;
            var teams = MapReferee.Instance.ActiveGameMode.Teams;
            var opponent = teams.FirstOrDefault(t => t != null &&
                (id == BotStandScenarioId.T09 ? t.teamIndex == Subject.TeamIndex : t.teamIndex != Subject.TeamIndex));
            if (opponent == null) throw new InvalidOperationException("Нет нужной команды контрольной цели.");
            _target = PlayersManager.Instance.CreateBotSession("Контрольная цель", "bot-stand-target-" + Guid.NewGuid().ToString("N"));
            SessionTeamAssigner.Apply(_target, opponent, BotSkin.PickHittable(opponent), "BotCombatStand");
            AvatarManager.Instance.SpawnAvatar(null, null, _target);
            if (_target.ActiveAvatar == null) throw new InvalidOperationException("Нет тела контрольной цели.");
            _target.ActiveAvatar.gameObject.AddComponent<BotBody>();
            Place(_target, id == BotStandScenarioId.T01 ? new Vector3(-8f, 0f, 5f) : new Vector3(0f, 0f, 5f), 180f);
            // Неуязвимость не подменяет прицел/выстрел: настоящий урон остаётся, цель выдерживает короткую серию.
            if (StateEventAuthority.IsWorldAuthority) _target.ActiveAvatar._actor.Life = 100000f;
            if(options.Profile==BotStandProfile.Capability)
            {
                _hiddenTargetColliders=_target.ActiveAvatar.GetComponentsInChildren<Collider>().Where(c=>c.enabled && c.gameObject.layer==LayerMask.NameToLayer("Player")).ToArray();
                foreach(var collider in _hiddenTargetColliders) collider.enabled=false;
            }
        }

        /// <summary>Двигает только контрольную цель. Проверяемому боту не выдаёт маршруты или состояния.</summary>
        public void Tick(float elapsed, float duration)
        {
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority) return;
            var brain = Subject?.ActiveAvatar != null ? Subject.ActiveAvatar.GetComponent<BotCombatDriver>()?.Brain : null;
            if (brain != null && _configuredRig != brain.GetInstanceID())
            {
                _configuredRig = brain.GetInstanceID();
                if (_options.Profile == BotStandProfile.Capability && (_id == BotStandScenarioId.T03 || _id == BotStandScenarioId.T04 || _id == BotStandScenarioId.T05))
                    ((CoverShooterBehaviour)brain.coverShooterBehaviour).firstSightDecision = CoverShooterBehaviour.FirstSightDecision.TakeCover;
                if(_hiddenTargetColliders!=null) { foreach(var collider in _hiddenTargetColliders) if(collider!=null) collider.enabled=true; _hiddenTargetColliders=null; Physics.SyncTransforms(); }
            }
            if (_target?.ActiveAvatar == null) return;
            float phase = elapsed / Mathf.Max(1f, duration);
            if(brain!=null && brain.enemyToAttack!=null) _targetSeen=true;
            if (_id == BotStandScenarioId.T01 && _targetSeen && elapsed >= 3f) Place(_target, new Vector3(0,0,5), 180);
            if (_id == BotStandScenarioId.T05) Place(_target, new Vector3(phase < .4f ? 0 : 7,0,5), 180);
            if (_id == BotStandScenarioId.T06) Place(_target, new Vector3(0,0,Mathf.Lerp(6,-3, Mathf.PingPong(phase*2,1))), 180);
            if (_id == BotStandScenarioId.T08) Place(_target, new Vector3(phase > .3f && phase < .7f ? -8 : 0,0,5), 180);
            if (_id == BotStandScenarioId.T09 && phase > .5f) _target.Role = GameRole.Spectator;
        }

        public static void Place(PlayerSession session, Vector3 feet, float yaw)
        {
            if (session?.ActiveAvatar == null) return;
            var body = session.ActiveAvatar.GetComponent<BotBody>();
            if (body == null) body = session.ActiveAvatar.gameObject.AddComponent<BotBody>();
            body.Place(feet, yaw);
        }

        public void Dispose()
        {
            if (!NetworkServer.active) { _ownedBots.Clear(); Subject=null; _target=null; return; }
            bool hadActors=_ownedBots.Count>0 || _target!=null;
            foreach (var bot in _ownedBots) if (bot != null) BotDirector.Instance?.RemoveBot(bot);
            _ownedBots.Clear();
            Subject = null;
            if (_target != null)
            {
                PlayersManager.Instance?.UnregisterBot(_target);
                if (_target.ActiveAvatar != null)
                {
                    AvatarTeardown.ReleaseBeforeDestroy(_target.ActiveAvatar, "стенд завершён");
                    NetworkServer.Destroy(_target.ActiveAvatar.gameObject);
                }
                NetworkServer.Destroy(_target.gameObject);
            }
            _target = null;
            if(hadActors && (BotDirector.Instance==null || BotDirector.Instance.Bots.Count==0)) BotNavMesh.Clear();
        }
    }
}
