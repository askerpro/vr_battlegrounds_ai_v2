using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
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
        /// Рука достала магазин из кармана. Магазины хранятся вне якоря, поэтому событие якоря
        /// <c>Removed</c> при этом не приходит — слушать нужно это (звук доставания, <see cref="AnchorSound" />).
        /// </summary>
        public event Action<UxrGrabber, UxrGrabbableObject> ItemExtracted;

        /// <summary>
        /// Предмет покинул карман — всё равно как: достали своей рукой или захват пришёл
        /// по сети. Сервер по нему снимает магазин с учёта кармана.
        /// </summary>
        public event Action<UxrGrabbableObject> ItemReleased;

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
                StoreItem(e.GrabbableObject);
                
                // Мгновенно снимаем объект с Якоря, чтобы Якорь снова стал пустым и мог принимать следующие магазины
                UxrGrabManager.Instance.RemoveObjectFromAnchor(e.GrabbableObject, true);
                
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
            // лежит здесь выключенным — его нужно достать, иначе в чужой руке окажется
            // невидимый предмет.
            item.Grabbing -= OnStoredItemGrabbing;
            item.Grabbing += OnStoredItemGrabbing;

            // Скрываем и делаем дочерним объектом кармана
            item.transform.SetParent(this.transform);
            
            // Сбрасываем физику
            Rigidbody rb = item.GetComponent<Rigidbody>();
            // У кинематического тела скорости нет — Unity на запись ругается предупреждением.
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            
            item.gameObject.SetActive(false);
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

        /// <summary>Возвращает спрятанный предмет в мир: список, видимость, родитель.</summary>
        private void Release(UxrGrabbableObject item)
        {
            item.Grabbing -= OnStoredItemGrabbing;
            _storedItems.Remove(item);
            item.gameObject.SetActive(true);
            item.transform.SetParent(null);

            ItemReleased?.Invoke(item);
        }

        /// <summary>
        /// Спрятанный магазин схватили в обход <see cref="ExtractMagazine"/> — захват
        /// пришёл по сети с машины владельца кармана.
        /// </summary>
        private void OnStoredItemGrabbing(object sender, UxrManipulationEventArgs e)
        {
            UxrGrabbableObject item = e.GrabbableObject;
            if (item == null || !_storedItems.Contains(item)) return;

            Release(item);

            if (_anchor != null) _anchor.UpdateGrabProxyState();
        }
    }
}
