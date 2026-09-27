using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Скрытый карман для хранения множества магазинов.
    /// Работает в связке с UxrGrabbableObjectAnchor. Перехватывает положенные в Якорь предметы
    /// и прячет их в невидимый список, освобождая Якорь для новых предметов.
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObjectAnchor))]
    public class UxrMagazinePocket : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int _capacity = 4;
        
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
            
            // Если это магазин и есть место
            if (mag != null && _storedItems.Count < _capacity)
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

        public int Capacity => _capacity;

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
                if (anchor.IsCompatibleObject(item))
                    return true;
            }

            return false;
        }

        private void StoreItem(UxrGrabbableObject item)
        {
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
            UxrGrabbableObject itemToExtract = null;

            UxrGrabber otherGrabber = avatar.GetGrabber(otherHand);
            if (otherGrabber != null && otherGrabber.GrabbedObject != null)
            {
                UxrGrabbableObjectAnchor[] weaponAnchors = otherGrabber.GrabbedObject.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true);

                foreach (UxrGrabbableObject storedItem in _storedItems)
                {
                    if (IsCompatible(storedItem, weaponAnchors))
                    {
                        itemToExtract = storedItem;
                        break;
                    }
                }
            }

            // Если ничего подходящего не нашли - достаем последний брошенный
            if (itemToExtract == null)
            {
                itemToExtract = _storedItems.Last();
            }

            Release(itemToExtract);

            // Телепортируем предмет прямо в хватающую руку
            itemToExtract.transform.position = grabber.transform.position;
            itemToExtract.transform.rotation = grabber.transform.rotation;

            return itemToExtract;
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
