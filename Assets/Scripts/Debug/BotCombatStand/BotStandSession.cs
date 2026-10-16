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
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;

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
        /// <summary>Сценарий снаряжения проверяемого бота (этап pf-equipment): начало, покупка, смерть, возрождение.</summary>
        public BotStandEquipmentScript Equipment { get; private set; }
        public BotStandDirectorEquipJournal DirectorEquip { get; private set; }
        private BotStandScenarioId _id;
        private BotStandRunOptions _options;
        private int _configuredRig;
        private bool _targetSeen;
        private Collider[] _hiddenTargetColliders;
        private readonly List<IDisposable> _readinessHolds = new List<IDisposable>();
        private readonly List<object> _readinessRows = new List<object>();
        private readonly Dictionary<PlayerSession, PlayerController> _readinessBodies = new Dictionary<PlayerSession, PlayerController>();
        private int _readyCycles;
        private int _readinessStartFrame;
        private float _readinessStartRealtime;
        private string _readinessReason;
        private bool _readinessSubscribed;
        public bool FixtureReady => _readyCycles >= 3;

        public void Begin(BotStandScenarioId id, WeaponInfo weapon, BotStandRunOptions options, WeaponInfo purchase = null)
        {
            _id = id; _options = options;
            bool directorEquip = id == BotStandScenarioId.DirectorEquip;
            if (directorEquip) BotStandDirectorEquipJournal.ValidateOptions(options);
            else BotStandEquipmentScript.Validate(options, weapon, purchase);
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority)
                throw new InvalidOperationException("Стенду нужна серверная authority.");
            if (BotDirector.Instance != null && BotDirector.Instance.Bots.Count != 0)
                throw new InvalidOperationException("Есть чужие боты — стенд их не удаляет.");
            var director = BotDirector.EnsureInstance();
            if (directorEquip)
            {
                Equipment = null;
                DirectorEquip = new BotStandDirectorEquipJournal();
            }
            Subject = director.AddBot(new BotAddOptions { AutoEquip = directorEquip });
            if (Subject != null) _ownedBots.Add(Subject);
            if (Subject == null || Subject.ActiveAvatar == null) throw new InvalidOperationException("Бот не создан.");
            if (directorEquip)
            {
                DirectorEquip.RestrictTo(Subject);
                DirectorEquip.SampleInventory("afterAddBot");
                return; // Штатный директор владеет телом, закупкой и вооружением; контрольной цели нет.
            }
            if (options.AvatarIndex >= 0)
                AvatarManager.Instance.ChangeAvatar(null, Subject, Subject.TeamIndex, options.AvatarIndex);
            Place(Subject, Lane(id == BotStandScenarioId.T03 || id == BotStandScenarioId.T04 || id == BotStandScenarioId.T05 ? new Vector3(-3.5f, 0f, -4f) : new Vector3(0f, 0f, -5f)), 0f);
            Equipment = new BotStandEquipmentScript(options, weapon, purchase);
            Equipment.Equip(Subject, "начало case");

            if (id == BotStandScenarioId.T02)
            {
                var other = director.AddBot(new BotAddOptions { AutoEquip = false });
                if(other!=null) _ownedBots.Add(other);
                if(other?.ActiveAvatar==null) throw new InvalidOperationException("Нет второго бота.");
                Place(other, Lane(new Vector3(0f, 0f, 5f)), 180f);
                if (weapon != null) other.ActiveAvatar.GetComponent<BotGunner>().Arm(weapon);
                return;
            }
            if (id == BotStandScenarioId.V01) return;
            var teams = MapReferee.Instance.ActiveGameMode.Teams;
            var opponent = teams.FirstOrDefault(t => t != null &&
                (id == BotStandScenarioId.T09 ? t.teamIndex == Subject.TeamIndex : t.teamIndex != Subject.TeamIndex));
            if (opponent == null) throw new InvalidOperationException("Нет нужной команды контрольной цели.");
            // Цель — тоже игрок без шлема (калибровка роста как у бота) с пассивным комплектом: драйвер держит корень,
            // камеру на росте 1,65 и кисти в покое; без хвата, Blaze и выстрелов. Директор её не ведёт (не его бот).
            _target = PlayersManager.Instance.CreateBotSession("Контрольная цель", "bot-stand-target-" + Guid.NewGuid().ToString("N"),
                BotPlayerFlowSettings.Calibration);
            SessionTeamAssigner.Apply(_target, opponent, BotSkin.PickHittable(opponent), "BotCombatStand");
            AvatarManager.Instance.SpawnAvatar(null, null, _target);
            if (_target.ActiveAvatar == null) throw new InvalidOperationException("Нет тела контрольной цели.");
            if (BotKit.Attach(_target.ActiveAvatar, BotKitKind.Passive) == null) throw new InvalidOperationException("Нет комплекта контрольной цели.");
            Place(_target, Lane(id == BotStandScenarioId.T01 ? new Vector3(-8f, 0f, 5f) : new Vector3(0f, 0f, 5f)), 180f);
            // Неуязвимость не подменяет прицел/выстрел: настоящий урон остаётся, цель выдерживает короткую серию.
            if (StateEventAuthority.IsWorldAuthority) _target.ActiveAvatar._actor.Life = 100000f;
            if(options.Profile==BotStandProfile.Capability)
            {
                _hiddenTargetColliders=_target.ActiveAvatar.GetComponentsInChildren<Collider>().Where(c=>c.enabled && c.gameObject.layer==LayerMask.NameToLayer("Player")).ToArray();
                foreach(var collider in _hiddenTargetColliders) collider.enabled=false;
            }
        }

        /// <summary>До первого замера удерживает огонь у владельца намерений; позу пишет только штатный драйвер.</summary>
        public void HoldForReadiness(BotStandCaseResult result)
        {
            _readinessStartFrame = Time.frameCount;
            _readinessStartRealtime = Time.realtimeSinceStartup;
            foreach (var bot in _ownedBots)
            {
                _readinessHolds.Add(BotDirector.Instance.OpenShotWindow(bot, Time.time));
                _readinessBodies.Add(bot, bot.ActiveAvatar);
            }
            if (_target != null) _readinessBodies.Add(_target, _target.ActiveAvatar);
            result.Metrics["fixtureReadiness"] = new Dictionary<string, object>
            {
                ["startedFrame"] = _readinessStartFrame, ["startedGameTime"] = Time.time,
                ["startedRealtime"] = _readinessStartRealtime, ["observations"] = _readinessRows,
                ["requiredConsecutiveCycles"] = 3, ["timeoutRealtimeSeconds"] = 15f,
                ["scope"] = "precase; первая готовая поза и ещё два последующих полных цикла UXR"
            };
            UxrManager.AvatarsUpdated += ObserveReadiness;
            _readinessSubscribed = true;
            ObserveReadiness(); // Исходная, ещё не применённая поза сохраняется отдельно, цикл не засчитывается.
        }

        public bool ReadinessTimedOut => Time.realtimeSinceStartup - _readinessStartRealtime >= 15f;

        private void ObserveReadiness()
        {
            if (FixtureReady) return;
            bool ready = Time.frameCount > _readinessStartFrame;
            _readinessReason = ready ? null : "Ожидание первого полностью последующего UXR цикла";
            foreach (var pair in _readinessBodies)
            {
                PlayerController player = pair.Key != null ? pair.Key.ActiveAvatar : null;
                BotBody body = player != null ? player.GetComponent<BotBody>() : null;
                BotAvatarDriver driver = player != null ? player.GetComponent<BotAvatarDriver>() : null;
                UxrAvatar avatar = player != null ? player.GetComponent<UxrAvatar>() : null;
                Transform camera = avatar != null ? avatar.CameraTransform : null;
                float height = camera != null && player != null ? camera.position.y - player.transform.position.y : float.NaN;
                float expectedHeight = pair.Key != null && pair.Key.Calibration.HasEyeHeight ? pair.Key.Calibration.EyeHeight : BotPlayerFlowSettings.EyeHeight;
                float rootError = player != null && body != null ? Vector3.Distance(player.transform.position, body.Feet) : float.NaN;
                bool valid = player != null && player == pair.Value && player.gameObject.activeInHierarchy && player.IsAlive &&
                    body != null && camera != null && camera.gameObject.activeInHierarchy && driver != null &&
                    driver.AppliedFrame == Time.frameCount && rootError <= .001f && Mathf.Abs(height - expectedHeight) <= .001f;
                bool stopped = !_ownedBots.Contains(pair.Key) || (BotDirector.Instance != null && !BotDirector.Instance.IsShotAllowed(player));
                var gunner = player != null ? player.GetComponent<BotGunner>() : null;
                int shotsBeforeActive = gunner != null ? gunner.ShotsFired : 0;
                ready &= valid && stopped && shotsBeforeActive == 0;
                if (!valid) _readinessReason = "Тело/драйвер/корень/высота камеры ещё не готовы";
                if (!stopped) _readinessReason = "Director не подтвердил удержание огня";
                if (shotsBeforeActive != 0) _readinessReason = "Есть выстрел до начала активной фазы";
                _readinessRows.Add(new Dictionary<string, object>
                {
                    ["frame"] = Time.frameCount, ["gameTime"] = Time.time, ["realtime"] = Time.realtimeSinceStartup,
                    ["session"] = pair.Key != null ? pair.Key.PlayerName : null, ["bodyId"] = player != null ? player.GetInstanceID() : 0,
                    ["root"] = player != null ? new[] { player.transform.position.x, player.transform.position.y, player.transform.position.z } : null,
                    ["feet"] = body != null ? new[] { body.Feet.x, body.Feet.y, body.Feet.z } : null,
                    ["height"] = float.IsNaN(height) ? null : (object)height, ["expectedHeight"] = expectedHeight,
                    ["rootErrorM"] = float.IsNaN(rootError) ? null : (object)rootError,
                    ["driverAppliedFrame"] = driver != null ? driver.AppliedFrame : -1,
                    ["ready"] = valid, ["fireHeld"] = stopped, ["shotsBeforeActive"] = shotsBeforeActive, ["reason"] = _readinessReason
                });
            }
            _readyCycles = ready ? _readyCycles + 1 : 0;
        }

        public void CompleteReadiness(BotStandCaseResult result, bool cancelled)
        {
            if (_readinessSubscribed) UxrManager.AvatarsUpdated -= ObserveReadiness;
            _readinessSubscribed = false;
            var metrics = (Dictionary<string, object>)result.Metrics["fixtureReadiness"];
            metrics["ready"] = FixtureReady;
            metrics["completedFrame"] = Time.frameCount;
            metrics["completedGameTime"] = Time.time;
            metrics["completedRealtime"] = Time.realtimeSinceStartup;
            metrics["consecutiveCycles"] = _readyCycles;
            metrics["endReason"] = cancelled ? "cancelled" : FixtureReady ? "ready" : "timeout: " + _readinessReason;
            if (!FixtureReady || cancelled)
            {
                result.Status = "InvalidFixture";
                result.Error = cancelled ? "Прогон отменён до готовности fixture." : "Timeout готовности fixture: " + _readinessReason;
            }
        }

        public void ReleaseReadinessFire()
        {
            foreach (var hold in _readinessHolds) hold.Dispose();
            _readinessHolds.Clear();
        }

        /// <summary>
        /// Двигает только контрольную цель. Проверяемому боту не выдаёт маршруты или состояния; снаряжение по времени —
        /// <see cref="BotStandEquipmentScript"/> (штатные серверные пути).
        /// </summary>
        public void Tick(float elapsed, float duration)
        {
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority) return;
            DirectorEquip?.SampleInventory();
            // Снаряжение проверяемого бота по времени (покупка, смерть, возрождение) — штатными серверными путями.
            Equipment?.Tick(Subject, elapsed);
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
            if (_id == BotStandScenarioId.T01 && _targetSeen && elapsed >= 3f) Place(_target, Lane(new Vector3(0,0,5)), 180);
            if (_id == BotStandScenarioId.T05) Place(_target, Lane(new Vector3(phase < .4f ? 0 : 7,0,5)), 180);
            if (_id == BotStandScenarioId.T06) Place(_target, Lane(new Vector3(0,0,Mathf.Lerp(6,-3, Mathf.PingPong(phase*2,1)))), 180);
            if (_id == BotStandScenarioId.T08) Place(_target, Lane(new Vector3(phase > .3f && phase < .7f ? -8 : 0,0,5)), 180);
            if (_id == BotStandScenarioId.T09 && phase > .5f) _target.Role = GameRole.Spectator;
        }

        /// <summary>Точка раскладки case со сдвигом <see cref="BotStandRunOptions.LaneOffsetX"/> (мультипроцессный стенд).</summary>
        private Vector3 Lane(Vector3 feet) => _options != null ? feet + new Vector3(_options.LaneOffsetX, 0f, 0f) : feet;

        /// <summary>
        /// Ставит тело с комплектом бота (<see cref="BotKit"/>): ноги и курс. Без комплекта — ошибка раскладки (InvalidFixture):
        /// компоненты бота добавляет только BotKit.
        /// </summary>
        public static void Place(PlayerSession session, Vector3 feet, float yaw)
        {
            if (session?.ActiveAvatar == null) return;
            var body = session.ActiveAvatar.GetComponent<BotBody>();
            if (body == null) throw new InvalidOperationException("У тела " + session.PlayerName + " нет комплекта бота (BotBody).");
            body.Place(feet, yaw);
        }

        public void Dispose()
        {
            DirectorEquip?.Dispose();
            if (_readinessSubscribed) UxrManager.AvatarsUpdated -= ObserveReadiness;
            _readinessSubscribed = false;
            ReleaseReadinessFire();
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

        public void WriteDirectorMetrics(BotStandCaseResult result, bool completed, string reason)
        {
            if (DirectorEquip == null) return;
            DirectorEquip.SampleInventory("final");
            DirectorEquip.Dispose();
            result.Metrics["directorEquip"] = DirectorEquip.Report(completed, reason);
        }
    }
}
