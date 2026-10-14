using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Выдача магазинов в карман своего игрока (<see cref="UxrMagazinePocket"/>).
    ///
    /// <para>
    /// <b>Решает сервер, и только он.</b> Что у игрока в руках и в кобурах, сервер видит
    /// сам — захваты и установку в якоря реплицирует UltimateXR. Поэтому клиент ничего
    /// не запрашивает: команду можно было бы подделать, а ответ на неё пришлось бы ждать.
    /// </para>
    ///
    /// <para>
    /// <b>Содержимое кармана — состояние, а не сообщение.</b> Список <c>netId</c> магазинов
    /// в кармане живёт в <c>SyncList</c>, и каждая машина прячет их в карман этого аватара
    /// сама, в том числе подключившаяся позже. Прежняя версия клала магазин в карман только
    /// владельцу (<c>TargetRpc</c>): у остальных он висел там, где его заспавнил сервер,
    /// а захват из кармана приходил им захватом именно этого объекта. Тот же вывод,
    /// что у слотов арсенала (NET-23).
    /// </para>
    ///
    /// <para>
    /// <b>Убирает магазины тоже сервер</b> — <c>NetworkServer.Destroy</c>. Прежний
    /// <c>Clear()</c> уничтожал сетевые объекты локально на клиенте, и машины расходились.
    /// </para>
    ///
    /// <para>
    /// <b>Когда и сколько — решает не менеджер.</b> Он не знает ни режимов, ни фаз, ни
    /// сцен: у него две серверные операции — «досыпь по N к каждому оружию»
    /// (<see cref="ServerEnsureMagazines"/>) и «очисти» (<see cref="ServerClearMagazines"/>).
    /// Зовут их политики режима или сцены, найдя менеджеры через <see cref="ServerInstances"/>.
    /// Сколько чего выдать и что выкинуть, считает <see cref="MagazineRefillPlanner"/>.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrAvatar))]
    public class PlayerLoadoutManager : NetworkBehaviour
    {
        private static readonly List<PlayerLoadoutManager> ServerInstancesList = new List<PlayerLoadoutManager>();

        /// <summary>Менеджеры всех игроков на этом сервере.</summary>
        public static IReadOnlyList<PlayerLoadoutManager> ServerInstances => ServerInstancesList;

        /// <summary>
        /// <c>netId</c> магазинов, лежащих в кармане. Пишет только сервер; добавляет — единственным путём,
        /// по <see cref="UxrMagazinePocket.ItemStored"/> (выдача, возврат рукой, гильзы ручного заряжания),
        /// снимает — по <see cref="UxrMagazinePocket.ItemReleased"/> и при уничтожении.
        /// </summary>
        private readonly SyncList<uint> _pocketMagazines = new SyncList<uint>();

        /// <summary>Сколько ждать спавна магазина из списка, прежде чем предупредить.</summary>
        private const float PendingBindingWarningSeconds = 5f;

        /// <summary>Магазины из списка, которые у клиента ещё не заспавнились: netId → когда начали ждать.</summary>
        private readonly Dictionary<uint, float> _pendingBindings = new Dictionary<uint, float>();
        private readonly HashSet<uint> _warnedBindings = new HashSet<uint>();
        private readonly List<uint> _resolvedBindings = new List<uint>();

        private UxrAvatar _avatar;
        private UxrMagazinePocket _pocket;

        /// <summary>Карман магазинов этого аватара или null, если его нет в префабе.</summary>
        public UxrMagazinePocket Pocket
        {
            get
            {
                EnsureReferences();
                return _pocket;
            }
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void EnsureReferences()
        {
            if (_avatar == null) _avatar = GetComponent<UxrAvatar>();
            if (_pocket == null) _pocket = GetComponentInChildren<UxrMagazinePocket>(true);
        }

        // ── Сервер ─────────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (!ServerInstancesList.Contains(this))
                ServerInstancesList.Add(this);

            if (Pocket != null)
            {
                Pocket.ItemStored -= ServerHandleItemStored;
                Pocket.ItemStored += ServerHandleItemStored;
                Pocket.ItemReleased -= ServerHandleItemReleased;
                Pocket.ItemReleased += ServerHandleItemReleased;
            }
        }

        public override void OnStopServer()
        {
            UnsubscribeServer();
            base.OnStopServer();
        }

        private void OnDisable()
        {
            // Статический список переживает объект — снимаемся и здесь.
            UnsubscribeServer();
        }

        private void UnsubscribeServer()
        {
            ServerInstancesList.Remove(this);

            if (_pocket != null)
            {
                _pocket.ItemStored -= ServerHandleItemStored;
                _pocket.ItemReleased -= ServerHandleItemReleased;
            }
        }

        /// <summary>
        /// Роняет оружие из кобур и убирает магазины из кармана: оружие выпадает на месте и
        /// становится ничьим (его можно подобрать, в конце раунда его уберёт общая уборка),
        /// магазины из кармана исчезают.
        ///
        /// <para>
        /// <b>Единственный путь сброса снаряжения</b>, и зовёт его только
        /// <see cref="VrBattlegrounds.Player.Avatars.AvatarTeardown"/> на сервере: перед уничтожением
        /// аватара (<c>ReleaseBeforeDestroy</c> — смена тела погибшего на призрака, отключение, уборка
        /// бота или стенда) и для мёртвого тела без замены (<c>ReleaseDeadBodyOnServer</c>, стратегия
        /// без призрака). При смерти оба вызова идут из <see cref="VrBattlegrounds.Player.Avatars.AvatarManager"/>
        /// на верхнем уровне — после <c>EndSync</c> смерти (задача death-body-defer, этап death-equipment-drop).
        /// </para>
        ///
        /// <para>
        /// <b>Не звать изнутри синхронизации UltimateXR</b> (обработчики <c>PlayerDied</c>,
        /// <c>UxrActor.DieInternal</c>). <c>RemoveObjectFromAnchor</c> открывает свой <c>BeginSync</c>;
        /// вложенное событие при <c>UseTopLevelStateChangesOnly</c> отдельно клиентам не уходит, а клиент
        /// свой серверный обработчик пропускает — у клиентов оружие оставалось в кобуре мёртвого тела.
        /// Прежняя подписка на <c>PlayerDied</c> снята именно поэтому.
        /// </para>
        ///
        /// <para>
        /// Снятие с якоря делает только сервер — оно синхронизируется каналом состояния UltimateXR, и
        /// на клиентах тело предмета становится динамическим так же, как у сервера. Кобуры висят на
        /// скелете, а не внутри модели, которую прячет наблюдатель: без сброса призрак ходил бы с
        /// видимым оружием на спине и получал его обратно при респауне.
        /// </para>
        /// </summary>
        /// <param name="reason">Почему снимается снаряжение — только для лога.</param>
        [Server]
        public void ServerDropEquipment(string reason)
        {
            if (!UxrGrabManager.HasInstance) return;

            int dropped = 0;

            foreach (UxrGrabbableObjectAnchor anchor in GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                // Только карманы аватара: гнездо магазина у оружия в кобуре тоже в иерархии.
                if (!AnchorRole.IsAvatarPocket(anchor)) continue;

                UxrGrabbableObject item = anchor.CurrentPlacedObject;
                if (item == null || item.GetComponent<WeaponComponent>() == null) continue;

                UxrGrabManager.Instance.RemoveObjectFromAnchor(item, true, true);
                dropped++;
            }

            ServerClearMagazines();

            GameLog.Player.Info($"[Loadout] {name}: {reason} — выронено оружия из кобур: {dropped}, магазины убраны.", this);
        }

        /// <summary>
        /// Досыпает в карман магазины к экипированному оружию и убирает лишние,
        /// если не хватает места.
        /// </summary>
        /// <param name="perWeapon">
        /// Сколько магазинов держать к каждому оружию; 0 — сколько задано в
        /// <see cref="WeaponInfo.MaxMagazineCount"/>.
        /// </param>
        [Server]
        public void ServerEnsureMagazines(int perWeapon)
        {
            // После Closing (принята загрузка следующей карты) правила режима карман не трогают:
            // серия уже изъяла снаряжение, новая выдача уехала бы на следующую карту.
            if (Pocket == null || !MapRunAdmission.CanActivateActiveMap) return;

            List<WeaponComponent> weapons = CollectEquippedWeapons();
            if (weapons.Count == 0) return;

            IReadOnlyList<UxrGrabbableObject> stored = Pocket.StoredItems;

            MagazineRefillPlanner.Plan plan = MagazineRefillPlanner.Compute(
                weapons,
                stored,
                (magazine, weapon) => UxrMagazinePocket.Fits(magazine, weapon.GetComponent<UxrGrabbableObject>()),
                weapon => weapon.WeaponData.MagazinePrefab == null ? 0
                    : System.Math.Min(Pocket.PerTypeLimit, perWeapon > 0 ? perWeapon : weapon.WeaponData.MaxMagazineCount)
                      * weapon.WeaponData.AmmoItemsPerMagazine, // ручное заряжание: патронов на столько же магазинов
                // Общей вместимости нет — предел на тип магазина (UxrMagazinePocket.PerTypeLimit).
                int.MaxValue);

            // Выкидываем до выдачи: индексы плана указывают в текущий список кармана.
            var discard = new List<UxrGrabbableObject>();
            foreach (int index in plan.Discard) discard.Add(stored[index]);
            foreach (UxrGrabbableObject magazine in discard) ServerDestroyMagazine(magazine);

            foreach (int index in plan.SpawnFor)
                ServerSpawnMagazine(weapons[index].WeaponData);

            if (plan.SpawnFor.Count > 0 || discard.Count > 0)
            {
                GameLog.Arsenal.Verbose(
                    $"[Loadout] {name}: выдано магазинов {plan.SpawnFor.Count}, убрано {discard.Count}.", this);
            }
        }

        /// <summary>
        /// Кладёт новый ствол в свободную кобуру второго оружия (<c>Anchor_Hip_R</c>) — так выдаётся
        /// стартовый пистолет (T-45). Спавн — через <see cref="NetworkUxrIdentity"/>, укладка —
        /// <c>UxrGrabManager.PlaceObject</c> автором мира (сервером): канал состояния UltimateXR
        /// повторяет её на клиентах, как возврат жетона на крючок.
        /// </summary>
        /// <returns>false — нет свободной кобуры, префаба или менеджера захвата.</returns>
        [Server]
        public bool ServerGiveWeapon(WeaponInfo info)
        {
            if (info == null || info.WeaponPrefab == null || !UxrGrabManager.HasInstance ||
                !MapRunAdmission.CanActivateActiveMap) return false;

            UxrGrabbableObjectAnchor holster = FindFreePocket(AnchorRoleKind.Secondary);
            if (holster == null) return false;

            GameObject weapon = MapRunAdmission.CreateActiveMapItem(info.WeaponPrefab);
            if (weapon == null) return false;

            weapon.transform.SetPositionAndRotation(holster.transform.position, holster.transform.rotation);
            WeaponComponent component = weapon.GetComponent<WeaponComponent>();
            if (component == null) component = weapon.AddComponent<WeaponComponent>();
            component.Init(info);

            weapon.SetActive(true);
            NetworkUxrIdentity.SpawnServerObject(weapon);

            UxrGrabbableObject grabbable = weapon.GetComponent<UxrGrabbableObject>();
            if (grabbable == null || !UxrGrabManager.Instance.PlaceObject(grabbable, holster, UxrPlacementOptions.None, true))
            {
                GameLog.Player.Warning($"[Loadout] {name}: '{info.DisplayName}' не лёг в кобуру '{holster.name}'.", this);
                NetworkServer.Destroy(weapon);
                return false;
            }

            GameLog.Player.Info($"[Loadout] {name}: выдан '{info.DisplayName}' в '{holster.name}'.", this);
            return true;
        }

        /// <summary>Есть ли у игрока (руки, кобуры) оружие этой категории.</summary>
        public bool HasWeaponOfCategory(WeaponCategory category)
        {
            foreach (WeaponComponent weapon in CollectEquippedWeapons())
                if (weapon.WeaponData != null && weapon.WeaponData.Category == category) return true;
            return false;
        }

        /// <summary>Свободный карман аватара заданной роли или null.</summary>
        private UxrGrabbableObjectAnchor FindFreePocket(AnchorRoleKind role)
        {
            foreach (UxrGrabbableObjectAnchor anchor in GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                if (anchor.CurrentPlacedObject == null && AnchorRole.Get(anchor) == role) return anchor;
            }

            return null;
        }

        /// <summary>Убирает из кармана все магазины — на всех машинах.</summary>
        [Server]
        public void ServerClearMagazines()
        {
            if (Pocket == null) return;

            foreach (UxrGrabbableObject magazine in new List<UxrGrabbableObject>(Pocket.StoredItems))
                ServerDestroyMagazine(magazine);

            _pocketMagazines.Clear();
        }

        [Server]
        private void ServerSpawnMagazine(WeaponInfo info)
        {
            // Создание и спавн ведёт сетевой слой: он гасит «Auto Anchor» до Awake
            // и выравнивает UniqueId по netId. Без выравнивания захват магазина
            // не применится на другой машине — это NET-16, см. NetworkUxrIdentity.
            GameObject magazine = MapRunAdmission.CreateActiveMapItem(info.MagazinePrefab);
            if (magazine == null) return;

            // Клиенты увидят объект там, где он был при спавне, — до того, как спрячут
            // его в карман. Пусть это будет сам карман.
            magazine.transform.SetPositionAndRotation(Pocket.transform.position, Pocket.transform.rotation);
            magazine.SetActive(true);

            NetworkUxrIdentity.SpawnServerObject(magazine);

            NetworkIdentity identity = magazine.GetComponent<NetworkIdentity>();
            UxrGrabbableObject grabbable = magazine.GetComponent<UxrGrabbableObject>();

            if (identity == null || identity.netId == 0 || grabbable == null)
            {
                GameLog.Arsenal.Warning($"[Loadout] Магазин '{info.MagazinePrefab.name}' не сетевой или не хватаемый — в карман не кладётся.", this);
                return;
            }

            // На учёт кармана магазин ставит ServerHandleItemStored — по событию самого кармана.
            Pocket.ForceStoreItem(grabbable);
        }

        [Server]
        private void ServerDestroyMagazine(UxrGrabbableObject magazine)
        {
            if (magazine == null) return;

            NetworkIdentity identity = magazine.GetComponent<NetworkIdentity>();
            if (identity != null) _pocketMagazines.Remove(identity.netId);

            if (identity != null && identity.netId != 0)
                NetworkServer.Destroy(magazine.gameObject);
            else
                Destroy(magazine.gameObject);
        }

        /// <summary>
        /// Магазин лёг в карман на сервере — ставим его на учёт. Единственный путь записи в
        /// <see cref="_pocketMagazines"/>: так учитываются и выданные сервером магазины, и возвращённые
        /// в карман рукой (размещение в якорь кармана реплицирует UltimateXR), и гильзы ручного заряжания.
        /// </summary>
        private void ServerHandleItemStored(UxrGrabbableObject item)
        {
            if (!isServer || item == null) return;

            NetworkIdentity identity = item.GetComponent<NetworkIdentity>();
            if (identity == null || identity.netId == 0) return;

            if (!_pocketMagazines.Contains(identity.netId))
                _pocketMagazines.Add(identity.netId);
        }

        /// <summary>Магазин покинул карман на сервере — снимаем его с учёта.</summary>
        private void ServerHandleItemReleased(UxrGrabbableObject item)
        {
            if (!isServer || item == null) return;

            NetworkIdentity identity = item.GetComponent<NetworkIdentity>();
            if (identity != null) _pocketMagazines.Remove(identity.netId);
        }

        /// <summary>
        /// Оружие в руках и в якорях аватара (кобуры), по одному на тип. Оружием считается
        /// предмет с <see cref="WeaponComponent"/> — его ставит стена арсенала при выдаче.
        /// </summary>
        public List<WeaponComponent> CollectEquippedWeapons()
        {
            EnsureReferences();

            var result = new List<WeaponComponent>();
            if (_avatar == null) return result;

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                UxrGrabber grabber = _avatar.GetGrabber(side);
                if (grabber != null) AddWeapon(grabber.GrabbedObject, result);
            }

            foreach (UxrGrabbableObjectAnchor anchor in GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                AddWeapon(anchor.CurrentPlacedObject, result);

            return result;
        }

        private static void AddWeapon(UxrGrabbableObject grabbed, List<WeaponComponent> result)
        {
            if (grabbed == null) return;

            // В руке может оказаться часть оружия — затвор или цевьё.
            WeaponComponent weapon = grabbed.GetComponentInParent<WeaponComponent>();
            if (weapon == null || weapon.WeaponData == null) return;

            foreach (WeaponComponent known in result)
            {
                if (known.WeaponData == weapon.WeaponData) return;
            }

            result.Add(weapon);
        }

        // ── Клиент ─────────────────────────────────────────────

        public override void OnStartClient()
        {
            base.OnStartClient();

            // На хосте магазины прячет сервер — второй раз не надо.
            if (isServer) return;

            _pocketMagazines.OnAdd += HandleMagazineAdded;

            foreach (uint netId in _pocketMagazines)
                AddPendingBinding(netId);

            // Магазин с netId меньше нашего уже запущен — прячем сразу; остальные пачки опоздавшего
            // (запуск идёт по порядку netId) — в ближайшем Update.
            ResolvePendingBindings();
        }

        public override void OnStopClient()
        {
            if (!isServer)
                _pocketMagazines.OnAdd -= HandleMagazineAdded;

            base.OnStopClient();
        }

        private void HandleMagazineAdded(int index)
        {
            AddPendingBinding(_pocketMagazines[index]);

            // Спавн нового магазина приходит раньше дельты списка — обычно его можно спрятать сразу.
            ResolvePendingBindings();
        }

        private void AddPendingBinding(uint netId)
        {
            if (!_pendingBindings.ContainsKey(netId))
                _pendingBindings.Add(netId, Time.unscaledTime);
        }

        private void Update()
        {
            if (_pendingBindings.Count > 0)
                ResolvePendingBindings();
        }

        /// <summary>
        /// Прячет в карман магазины, которые уже приехали к клиенту. Порядок спавн-сообщений
        /// и дельт <c>SyncList</c> Mirror не согласовывает — ждём явно, как стена арсенала.
        /// </summary>
        private void ResolvePendingBindings()
        {
            if (Pocket == null) return;

            _resolvedBindings.Clear();
            int stored = 0;

            foreach (KeyValuePair<uint, float> pending in _pendingBindings)
            {
                uint netId = pending.Key;

                if (!_pocketMagazines.Contains(netId))
                {
                    // Магазин уже достали или убрали — прятать нечего.
                    _resolvedBindings.Add(netId);
                    continue;
                }

                // Только запущенный у клиента объект (isClient ставит BootstrapIdentity после ApplySpawnPayload).
                // При опоздании Mirror применяет спавн пачки по порядку netId: магазин уже в spawned, но ещё
                // выключен и без Awake. Спрятанный в выключенный контейнер, он так и не проснётся — NetworkBehaviours
                // остаются null, OnStartClient падает и Mirror рвёт соединение (стенд, тикет 584).
                if (!NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) || identity == null ||
                    !identity.isClient)
                {
                    WarnIfBindingStuck(netId, pending.Value);
                    continue;
                }

                UxrGrabbableObject grabbable = identity.GetComponent<UxrGrabbableObject>();
                if (grabbable != null && !Pocket.Contains(grabbable))
                {
                    Pocket.ForceStoreItem(grabbable);
                    stored++;
                    GameLog.Player.Verbose(
                        $"[Loadout] {name}: магазин netId={netId} ({grabbable.name}) привязан к карману, " +
                        $"патронов {UxrMagazinePocket.RoundsOf(grabbable)}, мировой масштаб {grabbable.transform.lossyScale}.", this);
                }

                _resolvedBindings.Add(netId);
            }

            foreach (uint netId in _resolvedBindings)
            {
                _pendingBindings.Remove(netId);
                _warnedBindings.Remove(netId);
            }

            _resolvedBindings.Clear();

            if (stored > 0)
                GameLog.Player.Info($"[Loadout] {name}: привязано к карману магазинов {stored}, в кармане {Pocket.Count}.", this);
        }

        /// <summary>
        /// Магазин из списка кармана долго не спавнится у клиента — однократное предупреждение. Так выглядел
        /// дефект скрытия через <c>SetActive(false)</c>: Mirror не спавнит опоздавшему выключенные объекты.
        /// </summary>
        private void WarnIfBindingStuck(uint netId, float since)
        {
            if (Time.unscaledTime - since < PendingBindingWarningSeconds || !_warnedBindings.Add(netId)) return;

            GameLog.Network.Warning(
                $"[Loadout] {name}: магазин netId={netId} из кармана не заспавнился у клиента за " +
                $"{PendingBindingWarningSeconds:0} с — в карман не спрятан.", this);
        }
    }
}
