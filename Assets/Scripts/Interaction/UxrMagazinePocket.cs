using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Скрытый карман для хранения множества магазинов.
    /// Работает в связке с UxrGrabbableObjectAnchor. Перехватывает положенные в Якорь предметы
    /// и прячет их в невидимый список, освобождая Якорь для новых предметов.
    ///
    /// <para>
    /// <b>Вместимость — по типу</b> (решение пользователя): не больше <see cref="PerTypeLimit"/> магазинов одного
    /// типа, а типов — сколько угодно. Тип — тег магазина (<c>UxrGrabbableObject.Tag</c>), тот же, по которому
    /// гнездо оружия решает совместимость. Четвёртый магазин типа якорь не принимает (валидатор размещения).
    /// Раньше общая вместимость 4 делилась жадно, и второй ствол оставался без магазинов.
    /// </para>
    ///
    /// <para>
    /// <b>Прячет родитель, а не сам предмет</b> (этап grab-authority/pocket-active-hide). Спрятанный предмет
    /// лежит под собственным выключенным контейнером кармана; его <c>activeSelf</c> карман не трогает. Mirror
    /// спавнит опоздавшему только объекты с <c>activeSelf</c> = true (<c>SpawnObserversForConnection</c>):
    /// прежнее <c>SetActive(false)</c> оставляло опоздавшего без магазинов в кармане и без магазина,
    /// который достанут позже. Локально всё как раньше: предмет неактивен в иерархии — не кандидат на
    /// хват, не «лежащий» (<see cref="LooseItems.IsLoose"/>), без Update.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObjectAnchor))]
    public class UxrMagazinePocket : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Сколько магазинов одного типа держит карман. Типов — сколько угодно.")]
        [SerializeField] private int _perTypeLimit = 3;
        
        private List<UxrGrabbableObject> _storedItems = new List<UxrGrabbableObject>();
        private UxrGrabbableObjectAnchor _anchor;

        /// <summary>
        /// Выключенный контейнер спрятанных предметов. Это вид, а не сетевой объект: создаётся на каждой
        /// машине сам, без NetworkIdentity и состояния UltimateXR, в сцену не сохраняется.
        /// </summary>
        private Transform _storage;
        private bool _scaleWarned;

        /// <summary>
        /// Рука достала магазин из кармана. Магазины хранятся вне якоря, поэтому событие якоря
        /// <c>Removed</c> при этом не приходит — слушать нужно это (звук доставания, <see cref="AnchorSound" />).
        /// </summary>
        public event Action<UxrGrabber, UxrGrabbableObject> ItemExtracted;

        /// <summary>
        /// Предмет покинул карман — всё равно как: достали своей рукой или захват пришёл
        /// по сети. Сервер по нему снимает магазин с учёта кармана.
        /// </summary>
        public event Action<UxrGrabbableObject> ItemReleased;

        /// <summary>
        /// Предмет спрятан в карман — всё равно как: выдан сервером или положен рукой (в том числе
        /// пришедшим по сети размещением). Сервер по нему ставит магазин на учёт кармана.
        /// </summary>
        public event Action<UxrGrabbableObject> ItemStored;

        private void Awake()
        {
            _anchor = GetComponent<UxrGrabbableObjectAnchor>();
        }

        private void OnEnable()
        {
            if (_anchor != null)
            {
                _anchor.Placed += OnAnchorPlaced;
                _anchor.AddPlacingValidator(CanStore);
                _anchor.ProxyGrabResolving += OnProxyGrabResolving;
                _anchor.ProxyGrabbableQuery += OnProxyGrabbableQuery;
                _anchor.UpdateGrabProxyState();
            }
        }

        private void OnDisable()
        {
            if (_anchor != null)
            {
                _anchor.Placed -= OnAnchorPlaced;
                _anchor.RemovePlacingValidator(CanStore);
                _anchor.ProxyGrabResolving -= OnProxyGrabResolving;
                _anchor.ProxyGrabbableQuery -= OnProxyGrabbableQuery;
                _anchor.UpdateGrabProxyState();
            }
        }

        // Вычисляем, стоит ли вообще делать Proxy хватаемым (чтобы он не светился, когда карман пуст)
        private void OnProxyGrabbableQuery(object sender, UxrProxyGrabbableEventArgs e)
        {
            e.IsGrabbable = HasItems();
        }

        // Мы подписываемся на стандартный EventHandler якоря. Если у нас есть магазин, мы подставляем его в Target.
        private void OnProxyGrabResolving(object sender, UxrProxyResolveEventArgs e)
        {
            UxrGrabbableObject mag = ExtractMagazine(e.Grabber);
            if (mag != null)
            {
                e.Target = mag;
                
                // После извлечения обновляем статус: возможно магазин был последним
                _anchor.UpdateGrabProxyState();
                ItemExtracted?.Invoke(e.Grabber, mag);
            }
        }

        private void OnAnchorPlaced(object sender, UxrManipulationEventArgs e)
        {
            if (e.GrabbableObject == null) return;
            
            UxrFirearmMag mag = e.GrabbableObject.GetComponentInParent<UxrFirearmMag>();
            
            // Если это магазин и для его типа есть место
            if ((mag != null || e.GrabbableObject.GetComponent<Cartridge>() != null) && CanStore(e.GrabbableObject))
            {
                // Мгновенно снимаем объект с Якоря, чтобы Якорь снова стал пустым и мог принимать следующие магазины.
                // Сначала снять, потом прятать: снятие переподвешивает предмет к родителю якоря (кость таза),
                // и спрятанный раньше магазин остался бы видимым под телом, а не в контейнере.
                UxrGrabManager.Instance.RemoveObjectFromAnchor(e.GrabbableObject, true);

                StoreItem(e.GrabbableObject);

                // Сообщаем якорю, что теперь у нас есть предметы, и прокси можно хватать
                _anchor.UpdateGrabProxyState();
            }
            else
            {
                // Если карман полон или это не магазин, Якорь ведет себя как обычно (объект останется висеть на нем)
            }
        }

        /// <summary>
        /// Кладёт выданный магазин в карман. Повторный вызов с тем же предметом безвреден:
        /// на хосте выдачу применяют и сервер, и клиент одного процесса.
        /// </summary>
        public void ForceStoreItem(UxrGrabbableObject item)
        {
            if (item == null || _storedItems.Contains(item)) return;
            if (item.GetComponent<Cartridge>() != null && !CanStore(item)) return;

            StoreItem(item);

            if (_anchor != null) _anchor.UpdateGrabProxyState();
        }

        /// <summary>Сколько магазинов сейчас в кармане.</summary>
        public int Count
        {
            get
            {
                _storedItems.RemoveAll(item => item == null);
                return _storedItems.Count;
            }
        }

        /// <summary>Спрятан ли предмет в этом кармане.</summary>
        public bool Contains(UxrGrabbableObject item) => item != null && _storedItems.Contains(item);

        /// <summary>Сколько магазинов одного типа держит карман.</summary>
        public int PerTypeLimit => _perTypeLimit;

        /// <summary>Тип магазина — его тег; без тега — имя префаба.</summary>
        public static string TypeOf(UxrGrabbableObject magazine)
        {
            if (magazine == null) return string.Empty;
            if (!string.IsNullOrEmpty(magazine.Tag)) return magazine.Tag;
            return magazine.name.Replace("(Clone)", string.Empty).Trim();
        }

        /// <summary>Сколько магазинов этого типа уже в кармане.</summary>
        public int CountOfType(string type)
        {
            _storedItems.RemoveAll(item => item == null);
            int count = 0;
            foreach (UxrGrabbableObject item in _storedItems)
                if (TypeOf(item) == type) count++;
            return count;
        }

        /// <summary>
        /// Примет ли карман предмет: магазин — пока его типа меньше <see cref="PerTypeLimit"/>; не магазин —
        /// как обычный якорь. Валидатор размещения якоря.
        /// </summary>
        public bool CanStore(UxrGrabbableObject item)
        {
            if (item != null && item.TryGetComponent<UxrFirearmMag>(out var store) && store.IsFixedAmmoStore) return false;
            var shell = item != null ? item.GetComponent<Cartridge>() : null;
            // Гильза ручного заряжания — такой же боеприпас: тот же предел на тип, израсходованную не хранить.
            if (shell != null && !shell.IsAvailable) return false;
            if (item == null || shell == null && item.GetComponentInParent<UxrFirearmMag>() == null) return true;
            return CountOfType(TypeOf(item)) < _perTypeLimit * (shell != null ? shell.MagazineEquivalent : 1);
        }

        /// <summary>Спрятанные магазины, от старых к новым.</summary>
        public IReadOnlyList<UxrGrabbableObject> StoredItems
        {
            get
            {
                _storedItems.RemoveAll(item => item == null);
                return _storedItems;
            }
        }

        /// <summary>
        /// Встаёт ли магазин в это оружие. Совместимость решает якорь магазина оружия
        /// (<c>IsCompatibleObject</c>) — тот же критерий, что при извлечении из кармана.
        /// </summary>
        public static bool Fits(UxrGrabbableObject magazine, UxrGrabbableObject weapon)
        {
            if (magazine == null || weapon == null) return false;

            return IsCompatible(magazine, weapon.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true));
        }

        private static bool IsCompatible(UxrGrabbableObject item, UxrGrabbableObjectAnchor[] weaponAnchors)
        {
            foreach (UxrGrabbableObjectAnchor anchor in weaponAnchors)
            {
                // Только тег: валидаторы размещения — разрешение «положить сейчас» (у CartridgeIntake —
                // «держал этот игрок», «есть место»), а не вопрос, подходит ли предмет к оружию.
                if (item != null && anchor.IsCompatibleObjectTag(item.Tag))
                    return true;
            }

            return false;
        }

        private void StoreItem(UxrGrabbableObject item)
        {
            if (item.TryGetComponent<Cartridge>(out var shell) && !shell.IsAvailable ||
                item.TryGetComponent<UxrFirearmMag>(out var fixedStore) && fixedStore.IsFixedAmmoStore) return;
            _storedItems.Add(item);

            // Достаёт магазин из кармана только машина владельца (прокси-захват решается
            // локально), а остальным приходит уже захват конкретного магазина. У них он
            // лежит здесь в выключенном контейнере — его нужно достать, иначе в чужой руке
            // окажется невидимый предмет.
            item.Grabbing -= OnStoredItemTaken;
            item.Grabbing += OnStoredItemTaken;

            // Размещение спрятанного предмета в обход ExtractMagazine (например, в гнездо оружия по сети)
            // тоже выводит его из кармана: иначе он окажется в мире и при этом останется на учёте кармана.
            item.Placing -= OnStoredItemTaken;
            item.Placing += OnStoredItemTaken;

            // Сбрасываем физику до ухода в выключенную иерархию: там тело выпадает из симуляции.
            Rigidbody rb = item.GetComponent<Rigidbody>();
            // У кинематического тела скорости нет — Unity на запись ругается предупреждением.
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Прячем родителем: activeSelf предмета остаётся true, и Mirror спавнит его опоздавшему.
            item.transform.SetParent(Storage, true);

            ItemStored?.Invoke(item);
        }

        /// <summary>
        /// Выключенный контейнер спрятанных предметов; создаётся по требованию.
        ///
        /// <para>
        /// Висит прямо под корнем аватара, а не под костью таза, на которой карман. Кость масштабирует
        /// калибровка роста (<c>AvatarCalibrationApplier</c> — опора «Dummy Forward» над скелетом), и
        /// спрятанный магазин уменьшался вместе с телом, а при выходе из кармана сохранял искажённый
        /// масштаб. Корень аватара калибровка не трогает. Кроме того, Mirror отдаёт опоздавшему
        /// <c>localScale</c> предмета относительно родителя, а тот у опоздавшего рождается без родителя.
        /// Поэтому мировой масштаб контейнера держим единичным; предмет переподвешивается с сохранением
        /// мирового масштаба (у корней префабов магазинов он свой, например 0.7) — и не искажается.
        /// </para>
        /// </summary>
        private Transform Storage
        {
            get
            {
                if (_storage == null)
                {
                    var root = new GameObject($"{name} Storage");

                    // Служебный объект вида: ни в сцену редактора, ни в билд (как NetworkUxrIdentity.Dormitory).
                    root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                    root.SetActive(false);
                    _storage = root.transform;

                    UxrAvatar avatar = GetComponentInParent<UxrAvatar>(true);
                    _storage.SetParent(avatar != null ? avatar.transform : transform.root, false);
                }

                KeepUnitWorldScale(_storage);
                return _storage;
            }
        }

        private void KeepUnitWorldScale(Transform container)
        {
            Transform parent = container.parent;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            if ((parentScale - Vector3.one).sqrMagnitude <= 1e-8f)
            {
                container.localScale = Vector3.one;
                return;
            }

            // Корень аватара не масштабируется — иначе масштаб опоздавшему уйдёт неверным. Выравниваем и сообщаем.
            container.localScale = new Vector3(SafeInverse(parentScale.x), SafeInverse(parentScale.y), SafeInverse(parentScale.z));
            if (!_scaleWarned)
            {
                _scaleWarned = true;
                GameLog.Player.Warning($"[Pocket] {name}: мировой масштаб родителя контейнера кармана {parentScale} ≠ 1 — " +
                                       "контейнер выровнен обратным масштабом.", this);
            }
        }

        private static float SafeInverse(float value) => Mathf.Abs(value) > 1e-6f ? 1f / value : 1f;

        private void OnDestroy()
        {
            // Контейнер живёт вместе с карманом — как прежде спрятанные предметы, дочерние карману.
            if (_storage != null) Destroy(_storage.gameObject);
        }

        /// <summary>
        /// Есть ли спрятанные объекты в кармане? Вызывается из прокси-логики UxrGrabbableObjectAnchor.
        /// </summary>
        public bool HasItems()
        {
            _storedItems.RemoveAll(item => item == null);
            return _storedItems.Count > 0;
        }

        /// <summary>
        /// Интеллектуально извлекает подходящий магазин и возвращает его.
        /// Не вызывает GrabObject сам, а отдает эту обязанность вызвавшему (например, Якорю).
        /// </summary>
        public UxrGrabbableObject ExtractMagazine(UxrGrabber grabber)
        {
            _storedItems.RemoveAll(item => item == null);
            if (_storedItems.Count == 0 || grabber == null) return null;

            UxrAvatar avatar = grabber.Avatar;
            UxrHandSide otherHand = grabber.Side == UxrHandSide.Left ? UxrHandSide.Right : UxrHandSide.Left;
            UxrGrabber otherGrabber = avatar.GetGrabber(otherHand);
            UxrGrabbableObjectAnchor[] weaponAnchors = MagazineSlotsOf(otherGrabber != null ? otherGrabber.GrabbedObject : null);

            int index = ChooseMagazine(_storedItems.Count, i => IsCompatible(_storedItems[i], weaponAnchors), weaponAnchors.Length > 0);
            if (index < 0) return null;
            UxrGrabbableObject itemToExtract = _storedItems[index];

            Release(itemToExtract);

            // Телепортируем предмет прямо в хватающую руку
            itemToExtract.transform.position = grabber.transform.position;
            itemToExtract.transform.rotation = grabber.transform.rotation;

            return itemToExtract;
        }

        /// <summary>
        /// Какой магазин отдать руке (индекс в кармане, −1 — никакой). Другая рука держит оружие с гнездом
        /// магазина — только подходящий к нему; такого нет — ничего: чужой магазин в ладони хуже пустой руки
        /// (в руке пистолет — карман отдавал магазин дробовика из кобуры). Оружия в другой руке нет —
        /// последний положенный.
        /// </summary>
        public static int ChooseMagazine(int count, Func<int, bool> fitsHeldWeapon, bool holdingWeapon)
        {
            for (int i = 0; i < count; i++)
                if (fitsHeldWeapon(i)) return i;
            if (holdingWeapon) return -1;
            return count > 0 ? count - 1 : -1;
        }

        /// <summary>
        /// Гнёзда магазина оружия, которое держит рука. Рука может держать деталь (помпу, затвор) — оружие
        /// тогда находится через родителя.
        /// </summary>
        private static UxrGrabbableObjectAnchor[] MagazineSlotsOf(UxrGrabbableObject held)
        {
            if (held == null) return new UxrGrabbableObjectAnchor[0];
            UxrFirearmWeapon weapon = held.GetComponentInParent<UxrFirearmWeapon>();
            Component root = weapon != null ? (Component)weapon : held;
            return root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true);
        }

        /// <summary>
        /// Возвращает спрятанный предмет в мир: список, родитель. Видимость возвращает уход из
        /// выключенного контейнера — свой <c>activeSelf</c> предмет не терял.
        /// </summary>
        private void Release(UxrGrabbableObject item)
        {
            item.Grabbing -= OnStoredItemTaken;
            item.Placing -= OnStoredItemTaken;
            _storedItems.Remove(item);
            item.transform.SetParent(null, true);

            GameLog.Player.Verbose($"[Pocket] {item.name} покинул карман {name}: патронов {RoundsOf(item)}, " +
                                   $"мировой масштаб {item.transform.lossyScale}.", this);

            ItemReleased?.Invoke(item);
        }

        /// <summary>Патроны магазина для лога; не магазин — «-».</summary>
        public static string RoundsOf(UxrGrabbableObject item)
        {
            return item != null && item.TryGetComponent(out UxrFirearmMag mag) ? mag.Rounds.ToString() : "-";
        }

        /// <summary>
        /// Спрятанный магазин схватили (или разместили) в обход <see cref="ExtractMagazine"/> — захват
        /// пришёл по сети с машины владельца кармана.
        /// </summary>
        private void OnStoredItemTaken(object sender, UxrManipulationEventArgs e)
        {
            UxrGrabbableObject item = e.GrabbableObject;
            if (item == null || !_storedItems.Contains(item)) return;

            Release(item);

            if (_anchor != null) _anchor.UpdateGrabProxyState();
        }
    }
}
