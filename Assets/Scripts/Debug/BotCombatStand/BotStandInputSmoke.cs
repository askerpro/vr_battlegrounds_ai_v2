using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Player.Input;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Демо bot-input итерации 1 (контракт tasks/bots-input/contracts/bot-input-design.md): один бот на вводе устройства
    /// на сервере стенда. Скрипт подаёт пилоту (<see cref="AvatarPilot"/>) намерения, как человек со шлемом: идёт головой
    /// (корень стоит), подводит правую кисть к пистолету в кобуре и жмёт Grip, наводит ствол на контрольную цель и жмёт
    /// курок. Хват и выстрел делает код человека (UltimateXR patch 89); скрипт в SDK ничего не вызывает и только читает
    /// состояние (снап рукояти, дуло, события выстрела и урона). Итог — журнал фаз и <c>summary.json</c>.
    /// </summary>
    public sealed class BotStandInputSmoke : MonoBehaviour
    {
        private const float WalkSpeed = 0.8f;
        private const float ReachTolerance = 0.02f;
        private const float AimToleranceDeg = 1f;
        private const float TargetDistance = 10f;
        private const float HoldForward = 0.40f, HoldDown = 0.12f, HoldSide = 0.12f;
        private const float AimGain = 0.7f;

        /// <summary>Текущий прогон; null — не запускался.</summary>
        public static BotStandInputSmoke Current { get; private set; }

        public string Stage { get; private set; } = "Starting";
        public bool Finished { get; private set; }
        public bool Passed { get; private set; }
        public string Error { get; private set; }
        public string ReportPath { get; private set; }
        public readonly List<string> Journal = new List<string>();
        public readonly Dictionary<string, object> Checks = new Dictionary<string, object>();

        private string _output;
        private string _weaponName;
        private AvatarPilot _pilot;
        private PlayerSession _bot;
        private PlayerSession _target;
        private AvatarPilot _targetPilot;
        private UxrFirearmWeapon _firearm;
        private int _shots;
        private int _hits;
        private float _damage;
        private Quaternion _aimRotation;
        private bool _aiming;

        /// <summary>Запускает демо на сервере стенда. Возвращает null или причину отказа.</summary>
        public static string Begin(string outputDirectory, string weaponName = null)
        {
            if (!Application.isPlaying || !NetworkServer.active || !StateEventAuthority.IsWorldAuthority)
                return "нужен Play Mode серверного стенда (автор мира)";
            if (BotCombatStand.Instance == null) return "на сцене нет BotCombatStand";
            if (Current != null && !Current.Finished) return "демо уже идёт";
            if (BotDirector.Instance != null && BotDirector.Instance.Bots.Count > 0) return "есть чужие боты — запуск запрещён";

            var go = new GameObject("BotStandInputSmoke");
            Current = go.AddComponent<BotStandInputSmoke>();
            Current._output = outputDirectory;
            Current._weaponName = weaponName;
            Current.StartCoroutine(Current.Run());
            return null;
        }

        private IEnumerator Run()
        {
            IEnumerator body = Body();
            while (true)
            {
                object step;
                try
                {
                    if (!body.MoveNext()) break;
                    step = body.Current;
                }
                catch (Exception e)
                {
                    Fail(e.Message);
                    break;
                }
                yield return step;
            }

            Finished = true;
            WriteReport();
        }

        private IEnumerator Body()
        {
            // ── Бот на вводе ──
            Phase("Spawn");
            _pilot = new AvatarPilot();
            BotDirector director = BotDirector.EnsureInstance();
            _bot = director.AddBot(new BotAddOptions { AutoEquip = false, InputSource = _pilot });
            if (_bot == null || _bot.ActiveAvatar == null) throw new InvalidOperationException("бот не создан");
            PlayerController avatar = _bot.ActiveAvatar;
            UxrAvatar uxr = avatar.GetComponent<UxrAvatar>();
            BotInputKit kit = avatar.GetComponent<BotInputKit>();
            if (kit == null || kit.Device == null)
                throw new InvalidOperationException("ввод не подан: " + (kit != null ? kit.Refusal : "нет BotInputKit"));
            float until = Time.time + 10f;
            while (kit.Device.AppliedFrames < 3 && Time.time < until) yield return null;
            Check("device", kit.Device.AppliedFrames >= 3, $"кадров применено {kit.Device.AppliedFrames}, InputDriven={uxr.InputDriven}, режим {uxr.AvatarMode}");
            Vector3 rootStart = uxr.transform.position;
            Vector3 headStart = _pilot.HeadPosition;
            Quaternion rootRotationStart = uxr.transform.rotation;

            // ── Оружие в кобуру (стартовая выдача, как у человека) ──
            Phase("Holster");
            // Стартовый пистолет человека — WeaponRegistry.DefaultSidearm (Viper); иначе первый пистолет стенда.
            WeaponRegistry registry = WeaponRegistry.Instance;
            WeaponInfo weapon = string.IsNullOrEmpty(_weaponName) && registry != null && registry.DefaultSidearm != null
                ? registry.DefaultSidearm
                : BotCombatStand.Instance.Weapons?.FirstOrDefault(w => w != null && w.WeaponPrefab != null &&
                    w.Category == WeaponCategory.Pistol && (string.IsNullOrEmpty(_weaponName) || w.name == _weaponName));
            PlayerLoadoutManager loadout = avatar.GetComponent<PlayerLoadoutManager>();
            if (weapon == null || loadout == null || !loadout.ServerGiveWeapon(weapon))
                throw new InvalidOperationException("пистолет не лёг в кобуру: " + (weapon != null ? weapon.name : "нет пистолета в Weapons стенда"));
            yield return null;
            UxrGrabbableObjectAnchor holster = null;
            foreach (UxrGrabbableObjectAnchor anchor in uxr.GetComponentsInChildren<UxrGrabbableObjectAnchor>())
            {
                UxrGrabbableObject placed = anchor != null ? anchor.CurrentPlacedObject : null;
                if (placed == null || placed.GetComponent<UxrFirearmWeapon>() == null) continue;
                _firearm = placed.GetComponent<UxrFirearmWeapon>();
                holster = anchor;
                break;
            }
            if (_firearm == null || !BotGunner.TryResolveGrips(_firearm, out UxrGrabbableObject grip, out int primary, out _))
                throw new InvalidOperationException("в кобуре нет огнестрела с рукоятью спуска");
            Note($"в кобуре {_firearm.name} (якорь {holster.name}); учёт: {Readiness()}");
            _firearm.ProjectileShot += OnShot;

            yield return new WaitForSeconds(1f);
            Note($"через 1 с в кобуре, учёт: {Readiness()}");
            // ── Курс — к центру арены стенда: корень бота стоит у края зоны спавна лицом к стене (прогон 2: голова вошла
            // в южную стену, WallPass). Сначала поворот головы на курс, потом цель на 10 м по нему. ──
            Phase("Target");
            // Раскладка стенда — вокруг начала координат (полоса T07: бот z=−5, цель z=+5).
            Vector3 toCenter = Vector3.ProjectOnPlane(Vector3.zero - headStart, Vector3.up);
            if (toCenter.sqrMagnitude < 1f) toCenter = Vector3.ProjectOnPlane(-uxr.transform.forward, Vector3.up);
            float courseYaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
            Quaternion course = Quaternion.Euler(0f, courseYaw, 0f);
            until = Time.time + 3f;
            while (!_pilot.TurnTowards(course * Vector3.forward, Time.deltaTime) && Time.time < until) yield return null;
            Vector3 targetFeet = SpawnTarget(_bot, headStart, courseYaw);
            yield return null;
            // Цель — тоже бот на вводе: доходит головой до своей точки напротив и разворачивается к проверяемому.
            until = Time.time + 20f;
            while (Time.time < until && _targetPilot.Avatar != null &&
                   !_targetPilot.StepTowards(targetFeet, AvatarPilot.MaxWalkSpeed, Time.deltaTime, 0.1f))
                yield return null;
            until = Time.time + 3f;
            while (Time.time < until && !_targetPilot.TurnTowards(headStart - _targetPilot.HeadPosition, Time.deltaTime)) yield return null;
            Note($"цель на месте: до точки {(_targetPilot.FloorPosition - targetFeet).magnitude:0.00} м");
            if (Physics.SphereCast(_pilot.HeadPosition, 0.15f, course * Vector3.forward, out RaycastHit block, 3.5f, ~0, QueryTriggerInteraction.Ignore) &&
                !block.collider.transform.IsChildOf(uxr.transform))
                Note($"на пути головы препятствие {block.collider.name} в {block.distance:0.00} м — WallPass остановит бота, как человека");
            else Note($"путь головы 3,5 м по курсу {courseYaw:0}° свободен");

            // ── Ходьба головой по свободной полосе к цели: 2 м, осмотр (голова на 90° вправо и обратно), ещё 1 м ──
            // Вбок не идём: голова в препятствии стенда — штраф WallPass, как у человека (прогон 1: смерть по таймауту).
            Phase("Walk");
            Vector3 floor = _pilot.FloorPosition;
            Vector3 p1 = floor + course * Vector3.forward * 2f;
            Vector3 p2 = p1 + course * Vector3.forward * 1f;
            yield return Walk(p1);
            until = Time.time + 3f;
            while (!_pilot.TurnTowards(course * Vector3.right, Time.deltaTime) && Time.time < until) yield return null;
            until = Time.time + 3f;
            while (!_pilot.TurnTowards(course * Vector3.forward, Time.deltaTime) && Time.time < until) yield return null;
            yield return Walk(p2);
            Vector3 targetPoint = TargetAim();
            until = Time.time + 3f;
            while (!_pilot.TurnTowards(targetPoint - _pilot.HeadPosition, Time.deltaTime) && Time.time < until) yield return null;
            float headPath = (_pilot.HeadPosition - headStart).magnitude;
            float rootShift = (uxr.transform.position - rootStart).magnitude;
            float cameraToPilot = uxr.CameraTransform != null ? (uxr.CameraTransform.position - _pilot.HeadPosition).magnitude : float.NaN;
            Check("walk", headPath > 2f && rootShift < 0.001f && Quaternion.Angle(uxr.transform.rotation, rootRotationStart) < 0.01f,
                  $"голова сместилась на {headPath:0.00} м, корень сдвинулся на {rootShift * 1000f:0.0} мм, камера ↔ пилот {cameraToPilot * 100f:0.0} см");

            // ── Кисть к рукояти в кобуре, Grip ──
            Phase("Reach");
            UxrGrabber grabber = uxr.GetGrabber(UxrHandSide.Right);
            int stable = 0;
            until = Time.time + 5f;
            while (Time.time < until && stable < 3)
            {
                // Кобура едет с корпусом (BodyIK) — цель пересчитывается каждый кадр.
                if (grip.ComputeRequiredGrabberTransform(grabber, primary, out Vector3 gp, out Quaternion gr, false))
                {
                    _pilot.ReachGrabber(UxrHandSide.Right, gp, gr);
                    _pilot.LookAt(gp, Time.deltaTime);
                }
                yield return null;
                stable = _pilot.GrabberAtTarget(UxrHandSide.Right, ReachTolerance) ? stable + 1 : 0;
            }
            float reachGap = grip.ComputeRequiredGrabberTransform(grabber, primary, out Vector3 snap, out _, false)
                ? (grabber.transform.position - snap).magnitude : float.NaN;
            Note($"кисть у рукояти: зазор {reachGap * 1000f:0.0} мм");
            string candidate = UxrGrabManager.Instance.TryGetGrabCandidate(grabber, out UxrGrabbableObject cItem, out int cPoint)
                ? $"{cItem.name}#{cPoint}" : "нет (кандидаты считаются для локального игрока)";
            Note($"кандидат SDK перед Grip: {candidate}");

            Phase("Grip");
            _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, true);
            until = Time.time + 1.5f;
            while (grabber.GrabbedObject != grip && Time.time < until)
            {
                if (grip.ComputeRequiredGrabberTransform(grabber, primary, out Vector3 gp, out Quaternion gr, false))
                    _pilot.ReachGrabber(UxrHandSide.Right, gp, gr);
                yield return null;
            }
            Check("grab", grabber.GrabbedObject == grip, $"в правой руке {(grabber.GrabbedObject != null ? grabber.GrabbedObject.name : "ничего")}");
            for (int i = 0; i < 10; i++) yield return null;
            Note($"после хвата, учёт: {Readiness()}");
            if (grabber.GrabbedObject != grip) yield break;

            // ── Наведение и курок ──
            Phase("Aim");
            UxrProjectileSource source = _firearm.GetComponent<UxrProjectileSource>();
            int shotIndex = _firearm.GetTriggerShotIndex(0);
            Transform muzzle = source != null && source.ShotTypes != null && shotIndex >= 0 && shotIndex < source.ShotTypes.Count
                ? source.ShotTypes[shotIndex].ShotSource : null;
            if (muzzle == null) throw new InvalidOperationException("у спуска 0 нет ShotSource");
            _target.ActiveAvatar._actor.DamageReceived += OnDamage;

            int presses = 0;
            for (int attempt = 0; attempt < 5 && _hits == 0; attempt++)
            {
                stable = 0;
                until = Time.time + 4f;
                float error = 180f;
                while (Time.time < until && stable < 3)
                {
                    targetPoint = TargetAim();
                    _pilot.LookAt(targetPoint, Time.deltaTime);
                    error = Aim(grabber, muzzle, targetPoint);
                    yield return null;
                    stable = error <= AimToleranceDeg ? stable + 1 : 0;
                }
                Note($"попытка {attempt + 1}: ошибка дула {error:0.00}°; учёт: {Readiness()}");

                // Как человек: курок отпущен ≥ 2 кадров, нажат 3, отпущен.
                for (int i = 0; i < 2; i++) { Aim(grabber, muzzle, TargetAim()); yield return null; }
                _pilot.Press(UxrHandSide.Right, UxrInputButtons.Trigger, true);
                for (int i = 0; i < 3; i++) { Aim(grabber, muzzle, TargetAim()); yield return null; }
                _pilot.Press(UxrHandSide.Right, UxrInputButtons.Trigger, false);
                presses++;
                float pause = Time.time + 0.8f;
                while (Time.time < pause) { Aim(grabber, muzzle, TargetAim()); yield return null; }
            }
            Check("fire", _shots > 0, $"нажатий {presses}, выстрелов SDK {_shots}");
            Check("hit", _hits > 0, $"попаданий в цель {_hits}, урон {_damage:0.0}");

            // Показ: бот держит прицел ещё 5 с.
            Phase("Show");
            float show = Time.time + 5f;
            while (Time.time < show) { Aim(grabber, muzzle, TargetAim()); yield return null; }

            Passed = Checks.Values.All(v => v is Dictionary<string, object> d && (bool)d["passed"]);
            Phase(Passed ? "Passed" : "Failed");
        }

        // ── Шаги ─────────────────────────────────────────────────────────────

        private IEnumerator Walk(Vector3 floorTarget)
        {
            float until = Time.time + 10f;
            while (!_pilot.StepTowards(floorTarget, WalkSpeed, Time.deltaTime) && Time.time < until)
            {
                _pilot.LookAt(null, Time.deltaTime);
                yield return null;
            }
        }

        /// <summary>
        /// Кадр наведения (модель ScriptedGunner скриптового клиента): точка удержания перед головой, поворот грабера,
        /// при котором дуло смотрит в цель; дальше — по фактическому дулу. Возвращает ошибку дула, градусы.
        /// </summary>
        private float Aim(UxrGrabber grabber, Transform muzzle, Vector3 target)
        {
            Transform hand = grabber.transform;
            Quaternion yaw = Quaternion.AngleAxis(_pilot.Yaw, Vector3.up);
            Vector3 hold = _pilot.HeadPosition + yaw * new Vector3(HoldSide, -HoldDown, HoldForward);

            Quaternion handInverse = Quaternion.Inverse(hand.rotation);
            Vector3 muzzleInHand = handInverse * (muzzle.position - hand.position);
            Quaternion muzzleInHandRotation = handInverse * muzzle.rotation;

            if (!_aiming)
            {
                _aimRotation = hand.rotation;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 muzzlePosition = hold + _aimRotation * muzzleInHand;
                    Vector3 muzzleForward = _aimRotation * muzzleInHandRotation * Vector3.forward;
                    _aimRotation = Quaternion.FromToRotation(muzzleForward, target - muzzlePosition) * _aimRotation;
                }
                _aiming = true;
            }
            else
            {
                Quaternion correction = Quaternion.FromToRotation(muzzle.forward, target - muzzle.position);
                _aimRotation = Quaternion.Slerp(Quaternion.identity, correction, AimGain) * _aimRotation;
            }

            _pilot.ReachGrabber(UxrHandSide.Right, hold, _aimRotation);
            return Vector3.Angle(muzzle.forward, target - muzzle.position);
        }

        /// <summary>Точка прицеливания — центр торса цели (под её головой).</summary>
        private Vector3 TargetAim()
        {
            PlayerController target = _target != null ? _target.ActiveAvatar : null;
            UxrAvatar avatar = target != null ? target.GetComponent<UxrAvatar>() : null;
            Transform head = avatar != null ? avatar.CameraTransform : null;
            return head != null ? head.position - Vector3.up * 0.45f : _pilot.HeadPosition + Quaternion.Euler(0f, _pilot.Yaw, 0f) * Vector3.forward * TargetDistance;
        }

        /// <summary>
        /// Контрольная цель — второй бот на вводе (свой пилот, команда противника, Life=100000). Возвращает точку пола в
        /// 10 м по курсу проверяемого, куда цель дойдёт головой.
        /// </summary>
        private Vector3 SpawnTarget(PlayerSession subject, Vector3 head, float yaw)
        {
            var teams = MapReferee.Instance.ActiveGameMode.Teams;
            var opponent = teams.FirstOrDefault(t => t != null && t.teamIndex != subject.TeamIndex) ?? teams.FirstOrDefault(t => t != null);
            if (opponent == null) throw new InvalidOperationException("нет команды для цели");
            _targetPilot = new AvatarPilot();
            _target = BotDirector.Instance.AddBot(new BotAddOptions { AutoEquip = false, InputSource = _targetPilot });
            if (_target == null || _target.ActiveAvatar == null) throw new InvalidOperationException("нет тела цели");
            if (_target.TeamIndex != opponent.teamIndex)
                SessionTeamAssigner.Apply(_target, opponent, BotSkin.PickHittable(opponent), "BotStandInputSmoke");
            if (_target.ActiveAvatar == null) throw new InvalidOperationException("нет тела цели после смены команды");
            _target.ActiveAvatar._actor.Life = 100000f;
            Vector3 feet = new Vector3(head.x, _target.ActiveAvatar.transform.position.y, head.z) + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * TargetDistance;
            Note($"цель {_target.ActiveAvatar.name} идёт в {TargetDistance} м по курсу");
            return feet;
        }

        // ── Наблюдение ───────────────────────────────────────────────────────

        private void OnShot(int trigger) => _shots++;

        private void OnDamage(object sender, UxrDamageEventArgs e)
        {
            UxrActor shooter = _bot != null && _bot.ActiveAvatar != null ? _bot.ActiveAvatar._actor : null;
            if (e == null || shooter == null || e.ActorSource != shooter) return;
            _hits++;
            _damage += e.Damage;
        }

        private void Phase(string name)
        {
            Stage = name;
            Note("фаза " + name);
        }

        private void Note(string text)
        {
            string line = $"{Time.time:0.00} {text}";
            Journal.Add(line);
            GameLog.Debug.Info("[BotStandInputSmoke] " + text);
        }

        private void Check(string name, bool passed, string detail)
        {
            Checks[name] = new Dictionary<string, object> { ["passed"] = passed, ["detail"] = detail };
            Note($"{(passed ? "PASS" : "FAIL")} {name}: {detail}");
        }

        private void Fail(string error)
        {
            Error = error;
            Passed = false;
            Stage = "Failed";
            Note("ошибка: " + error);
            GameLog.Error("[BotStandInputSmoke] " + error);
        }

        private void WriteReport()
        {
            if (string.IsNullOrEmpty(_output)) return;
            try
            {
                Directory.CreateDirectory(_output);
                ReportPath = Path.Combine(_output, "summary.json");
                File.WriteAllText(ReportPath, JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["passed"] = Passed, ["stage"] = Stage, ["error"] = Error, ["checks"] = Checks, ["journal"] = Journal
                }, Formatting.Indented));
            }
            catch (Exception e)
            {
                GameLog.Error("[BotStandInputSmoke] отчёт не записан: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            if (_firearm != null) _firearm.ProjectileShot -= OnShot;
            if (_target != null && _target.ActiveAvatar != null) _target.ActiveAvatar._actor.DamageReceived -= OnDamage;
            if (Current == this) Current = null;
        }
    }
}
