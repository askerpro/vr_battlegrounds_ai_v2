using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    public enum BotStandScenarioId { V01, V02, T01, T02, T03, T04, T05, T06, T07, T08, T09, T10, L01, DirectorEquip }
    public enum BotStandProfile { Production, Capability }

    [Serializable]
    public sealed class BotStandRunOptions
    {
        public string OutputDirectory;
        public BotStandProfile Profile;
        public int[] Seeds = { 101 };
        public float CaseSeconds = 10f;
        public bool Capture = true;
        public bool Rifle;
        public bool HandFit;
        public string WeaponName;
        public int AvatarIndex = -1;
        /// <summary>
        /// Только стенд: точка прицеливания ИИ — ровно центр тела цели (без разброса ИИ). Компенсация отдачи в
        /// BotWeaponAim не меняется. По умолчанию выключено.
        /// </summary>
        public bool ZeroAiAimSpread;
        /// <summary>
        /// Только стенд: у оружия ботов стенда нет отдачи — корневую отдачу SDK запрещает политика
        /// <c>IUxrFirearmRootRecoilPolicy</c> на экземпляре, <c>RecoilAccumulator</c> экземпляра выключен. Префабы не
        /// меняются. По умолчанию выключено.
        /// </summary>
        public bool NoRecoil;
        /// <summary>Только диагностика T07: после активных 30 с наблюдать условное окно полёта по публичным descriptors.</summary>
        public bool ObserveShotTail;
        /// <summary>
        /// Только стенд, мультипроцессный запуск (этап pf-network): сдвиг по X всей раскладки case — места бота и
        /// контрольной цели и её перемещений по сценарию, метры. Клиенты-игроки без команды режима (Respawn — выбор
        /// игрока, разминка без команд) появляются в начале координат (на сцене стенда нет нейтральной зоны и
        /// NetworkStartPosition) — на линии огня T07 (бот z=−5, цель z=+5). Укрытия не сдвигаются: для сценариев с
        /// укрытиями (T01, T03–T05, T08) смещение меняет сценарий. По умолчанию 0 — раскладка прежняя.
        /// </summary>
        public float LaneOffsetX;
        /// <summary>
        /// Только стенд, мультипроцессный запуск (net-round1 attempt-6, A/B): клиентские наблюдатели вешают
        /// <see cref="BotStandNoRecoilPolicy"/> на свою локальную копию оружия в руке бота — корневая отдача SDK при
        /// повторе выстрела у клиента не применяется. Сервер это поле не исполняет: значение доходит до клиентов через
        /// заявку наблюдателя (<see cref="BotStandNetObserverRequest.ClientNoRecoil"/>, пишет B3 до Play), здесь — запись
        /// в summary и сверка очереди B4 с заявкой. По умолчанию выключено.
        /// </summary>
        public bool ClientNoRecoil;

        // ── Снаряжение проверяемого бота (этап pf-equipment; исполняет BotStandEquipmentScript) ──

        /// <summary>
        /// Оружие (WeaponName/Rifle, только пистолет) выдаётся в кобуру штатной выдачей стартового пистолета
        /// (<c>PlayerLoadoutManager.ServerGiveWeapon</c>, как <c>StartingSidearmPolicy</c>); бот берёт его сам (<c>Arm(null)</c>).
        /// </summary>
        public bool HolsterStart;
        /// <summary>Без оружия (Unarmed): кобура пуста, <c>Arm(null)</c> — бот ничего не берёт, Blaze не создаётся.</summary>
        public bool Unarmed;
        /// <summary>Имя WeaponInfo покупки из <c>BotCombatStand.Weapons</c>; пусто — без покупки.</summary>
        public string PurchaseWeaponName;
        /// <summary>
        /// Когда (секунды от начала case) оплатить покупку штатной экономикой (<c>BotShopper.TryPay</c>) и отдать её боту
        /// (<c>BotGunner.OfferPurchase</c>: пистолет в руке сначала убирается в кобуру). &lt; 0 — не покупать.
        /// </summary>
        public float PurchaseAtSeconds = -1f;
        /// <summary>Когда нанести проверяемому боту смертельный урон (<c>UxrActor.ReceiveDamage</c>, штатная смерть). &lt; 0 — нет.</summary>
        public float KillAtSeconds = -1f;
        /// <summary>
        /// Когда оживить погибшего (<c>PlayerController.Respawn</c>, как раунд Elimination; в Respawn-режиме призрак сам не
        /// оживает) — новое тело получает снаряжение так же, как в начале case. &lt; 0 — нет.
        /// </summary>
        public float RespawnAtSeconds = -1f;

        public BotStandScenarioId[] Cases;
    }

    [Serializable]
    public sealed class BotStandRunHandle
    {
        public string RunId, OutputDirectory;
    }

    [Serializable]
    public sealed class BotStandRunStatus
    {
        public string RunId, CurrentCase, ReportPath, Error;
        public bool IsFinished;
        public bool? Passed;
        public int Total, Completed, Failed, NeedsReview, InvalidFixture, Unsupported;
    }

    [Serializable]
    public sealed class BotStandFrame
    {
        public int Frame, BodyId, Team, TargetTeam, Shots;
        public float Time;
        public string Name, State, Stage, Weapon, Category;
        public float[] Feet;
        public float[] TargetFeet;
        public bool Alive, Combat, NavOnMesh, CompletePath, Author;
        public bool Visible, ProtectedHead, ProtectedChest, ProtectedPelvis, InsideSolid, LegalTarget;
        public float TargetDistance, NearestBotDistance;
    }

    [Serializable]
    public sealed class BotStandCaseResult
    {
        public string Id, Status, Error;
        public int Seed;
        public float Duration;
        public BotStandProfile Profile;
        public bool Capture;
        public List<string> Findings = new List<string>();
        public Dictionary<string, object> Metrics = new Dictionary<string, object>();
        public List<BotStandFrame> Frames = new List<BotStandFrame>();
        public List<string> Images = new List<string>();
    }

    /// <summary>Ограниченный пассивный журнал настоящих решений директора и сетевого инвентаря. Игровых команд не даёт.</summary>
    public sealed class BotStandDirectorEquipJournal : IDisposable
    {
        public const int EventCapacity = 8192, InventoryCapacity = 4096, IdentityCapacity = 256, WeaponCapacity = 256;
        private readonly List<object> _events = new List<object>();
        private readonly List<object> _inventory = new List<object>();
        private readonly Dictionary<object, int> _minds = new Dictionary<object, int>();
        private readonly HashSet<uint> _baseline = new HashSet<uint>();
        private readonly HashSet<uint> _previous = new HashSet<uint>();
        private PlayerSession _subject;
        private bool _disposed;
        private int _droppedEvents, _droppedInventory, _errors, _refusals;
        private string _firstError;
        private bool _baselineCaptured;
        private string _baselineReason;

        public BotStandDirectorEquipJournal()
        {
            // Подписка предшествует AddBot: даже первый Think не теряется.
            BotDirector.EquipmentObserved += Observe;
            SampleInventory("beforeAddBot");
        }

        public void RestrictTo(PlayerSession subject) => _subject = subject;

        public static void ValidateOptions(BotStandRunOptions options)
        {
            if (options.HolsterStart || options.Unarmed || options.Rifle || !string.IsNullOrEmpty(options.WeaponName) ||
                !string.IsNullOrEmpty(options.PurchaseWeaponName) || options.PurchaseAtSeconds >= 0f ||
                options.KillAtSeconds >= 0f || options.RespawnAtSeconds >= 0f || options.ObserveShotTail ||
                options.ZeroAiAimSpread || options.NoRecoil || options.ClientNoRecoil || options.HandFit ||
                options.AvatarIndex >= 0 || options.LaneOffsetX != 0f || options.Profile != BotStandProfile.Production)
                throw new ArgumentException("DirectorEquip наблюдает штатного бота: снаряжение, тело и бой нельзя подменять опциями стенда.");
        }

        private void Observe(BotDirectorEquipmentObservation item)
        {
            if (_disposed || (_subject != null && item.Session != _subject)) return;
            if (_events.Count >= EventCapacity) { _droppedEvents++; return; }
            try
            {
                if (!_minds.TryGetValue(item.MindIdentity, out int mind))
                {
                    if (_minds.Count >= IdentityCapacity) { _droppedEvents++; return; }
                    mind = _minds.Count + 1;
                    _minds.Add(item.MindIdentity, mind);
                }
                if (item.Kind == "offer" && item.Accepted == false) _refusals++;
                _events.Add(new Dictionary<string, object>
                {
                    ["frame"] = Time.frameCount, ["time"] = Time.time, ["realtime"] = Time.realtimeSinceStartup,
                    ["kind"] = item.Kind, ["mindId"] = mind,
                    ["sessionNetId"] = item.Session != null ? item.Session.netId : 0,
                    ["sessionInstanceId"] = item.Session != null ? item.Session.GetInstanceID() : 0,
                    ["bodyId"] = item.Body != null ? item.Body.GetInstanceID() : 0,
                    ["bodyNetId"] = item.Body != null ? item.Body.netId : 0,
                    ["runKey"] = item.Snapshot.Key.ToString(), ["modeEpoch"] = item.Snapshot.ModeEpoch,
                    ["map"] = item.Snapshot.Config?.MapScene, ["mode"] = item.Snapshot.ActiveModeId,
                    ["descriptorReady"] = item.Snapshot.IsReady, ["shopToken"] = item.ShopToken,
                    ["stage"] = BotSenses.Stage().ToString(), ["alive"] = item.Body != null && item.Body.IsAlive,
                    ["contextReset"] = item.ContextReset, ["weapon"] = item.Weapon != null ? item.Weapon.name : null,
                    ["accepted"] = item.Accepted, ["armBefore"] = item.ArmBefore, ["armAfter"] = item.ArmAfter,
                    ["outcome"] = item.Kind == "buy" ? (item.Weapon != null ? "weapon" : "noPurchase") :
                        item.Kind == "offer" ? (item.Accepted == true ? "accepted" : "refused") : item.Kind
                });
            }
            catch (Exception e) { RecordError(e); }
        }

        public void SampleInventory(string reason = "sample")
        {
            if (_disposed) return;
            if (_inventory.Count >= InventoryCapacity) { _droppedInventory++; return; }
            try
            {
                if (!NetworkServer.active) return;
                bool captureBaseline = !_baselineCaptured;
                var weapons = new List<object>();
                var current = new HashSet<uint>();
                var seen = new HashSet<int>();
                int? owned = _subject != null && _subject.ActiveAvatar != null && _subject.ActiveAvatar._actor != null ? 0 : (int?)null;
                foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
                {
                    if (weapons.Count >= WeaponCapacity) { _droppedInventory++; break; }
                    if (identity == null) continue;
                    foreach (UxrFirearmWeapon firearm in identity.GetComponentsInChildren<UxrFirearmWeapon>(true))
                    {
                        if (firearm == null || !seen.Add(firearm.GetInstanceID())) continue;
                        if (weapons.Count >= WeaponCapacity) { _droppedInventory++; break; }
                        NetworkIdentity root = firearm.GetComponentInParent<NetworkIdentity>();
                        if (root == null || root.netId == 0) { _droppedInventory++; continue; }
                        uint id = root.netId;
                        current.Add(id);
                        if (captureBaseline) _baseline.Add(id);
                        UxrActor actor = firearm.Owner;
                        PlayerController owner = actor != null ? actor.GetComponentInParent<PlayerController>() : null;
                        bool ownedBySubject = _subject != null && _subject.ActiveAvatar != null && actor != null && actor == _subject.ActiveAvatar._actor;
                        if (owned.HasValue && ownedBySubject) owned++;
                        var component = firearm.GetComponent<WeaponComponent>();
                        weapons.Add(new Dictionary<string, object>
                        {
                            ["netId"] = id, ["instanceId"] = firearm.GetInstanceID(), ["name"] = firearm.name,
                            ["weapon"] = component != null && component.WeaponData != null ? component.WeaponData.name : null,
                            ["baseline"] = _baseline.Contains(id), ["appearedSincePreviousSample"] = !_previous.Contains(id),
                            ["ownerActorId"] = actor != null ? actor.GetInstanceID() : 0,
                            ["ownerBodyId"] = owner != null ? owner.GetInstanceID() : 0,
                            ["ownerSessionNetId"] = owner != null && owner.Session != null ? owner.Session.netId : 0,
                            ["ownedBySubject"] = ownedBySubject
                        });
                    }
                }
                var disappeared = new List<uint>();
                foreach (uint id in _previous) if (!current.Contains(id)) disappeared.Add(id);
                BotGunner gunner = _subject != null && _subject.ActiveAvatar != null ? _subject.ActiveAvatar.GetComponent<BotGunner>() : null;
                UxrFirearmWeapon selected = gunner != null ? gunner.SelectedFirearm : null;
                NetworkIdentity selectedRoot = selected != null ? selected.GetComponentInParent<NetworkIdentity>() : null;
                _inventory.Add(new Dictionary<string, object>
                {
                    ["frame"] = Time.frameCount, ["time"] = Time.time, ["realtime"] = Time.realtimeSinceStartup,
                    ["reason"] = reason, ["weapons"] = weapons, ["disappearedNetIds"] = disappeared,
                    ["subjectBodyId"] = _subject != null && _subject.ActiveAvatar != null ? _subject.ActiveAvatar.GetInstanceID() : 0,
                    ["selectedInstanceId"] = selected != null ? selected.GetInstanceID() : 0,
                    ["selectedNetId"] = selectedRoot != null ? selectedRoot.netId : 0,
                    ["ownedSpawnedWeaponCount"] = owned
                });
                if (captureBaseline) { _baselineCaptured = true; _baselineReason = reason; }
                _previous.Clear();
                foreach (uint id in current) _previous.Add(id);
            }
            catch (Exception e) { RecordError(e); }
        }

        private void RecordError(Exception error)
        {
            _errors++;
            if (_firstError == null) _firstError = error.GetType().Name + ": " + error.Message;
        }

        public Dictionary<string, object> Report(bool completed, string reason) => new Dictionary<string, object>
        {
            ["schema"] = "director-equip@1", ["events"] = _events, ["inventory"] = _inventory,
            ["eventCapacity"] = EventCapacity, ["inventoryCapacity"] = InventoryCapacity,
            ["weaponCapacityPerSample"] = WeaponCapacity, ["mindCapacity"] = IdentityCapacity,
            ["droppedEvents"] = _droppedEvents, ["droppedInventory"] = _droppedInventory,
            ["errors"] = _errors, ["firstError"] = _firstError, ["completed"] = completed, ["endReason"] = reason,
            ["baselineCaptured"] = _baselineCaptured, ["baselineReason"] = _baselineReason,
            ["incomplete"] = !completed || _droppedEvents != 0 || _droppedInventory != 0 || _errors != 0,
            ["refusalBranch"] = _refusals > 0 ? "observed" : "UNKNOWN/notExercised",
            ["invalidDescriptorBranch"] = "UNKNOWN/notExercised",
            ["decisionCoverage"] = _events.Count > 0 ? "observed" : "UNKNOWN/noDecisions",
            ["automaticVerdict"] = "UNKNOWN: окончание наблюдения не означает приёмку сценария или lifecycle",
            ["onePurchasedSpawn"] = "UNKNOWN: публичного provenance SpawnPurchase нет; Owner и selected не доказывают число спавнов",
            ["inventoryScope"] = "Полный sampled NetworkServer.spawned firearm inventory, включая стартовый пистолет; Owner — текущая атрибуция, не создатель",
            ["mindScope"] = "mindId присвоен по ссылочной идентичности BotMind в пределах непрерывной жизни этого журнала"
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BotDirector.EquipmentObserved -= Observe;
        }
    }
}
