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

        public void ForceStoreItem(UxrGrabbableObject item)
        {
            StoreItem(item);
        }

        public void Clear()
        {
            foreach (var item in _storedItems)
            {
                if (item != null) Destroy(item.gameObject);
            }
            _storedItems.Clear();
            if (_anchor != null) _anchor.UpdateGrabProxyState();
        }

        private void StoreItem(UxrGrabbableObject item)
        {
            _storedItems.Add(item);
            
            // Скрываем и делаем дочерним объектом кармана
            item.transform.SetParent(this.transform);
            
            // Сбрасываем физику
            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
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
                    bool isCompatible = false;
                    foreach (var anchor in weaponAnchors)
                    {
                        if (anchor.IsCompatibleObject(storedItem))
                        {
                            isCompatible = true;
                            break;
                        }
                    }

                    if (isCompatible)
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

            // Достаем объект
            _storedItems.Remove(itemToExtract);
            itemToExtract.gameObject.SetActive(true);
            itemToExtract.transform.SetParent(null); 
            
            // Телепортируем предмет прямо в хватающую руку
            itemToExtract.transform.position = grabber.transform.position;
            itemToExtract.transform.rotation = grabber.transform.rotation;

            return itemToExtract;
        }
    }
}
