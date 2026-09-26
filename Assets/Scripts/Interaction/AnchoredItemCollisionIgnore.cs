using System.Collections.Generic;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Отключает столкновения корпуса предмета с тем, что вставлено в его якоря
    /// (магазин в оружии).
    ///
    /// <para>
    /// Выпуклый коллайдер корпуса охватывает шахту магазина, и вставленный магазин оказывается
    /// внутри него. Магазин в якоре kinematic, корпус после отпускания динамический — физика
    /// выталкивает корпус из магазина каждый шаг, и брошенное оружие уходит под пол даже при
    /// падении с места (PHY-01). UltimateXR такие пары не разводит.
    /// </para>
    ///
    /// <para>
    /// Вставленный предмет определяется по иерархии — прямой потомок якоря со своим
    /// <see cref="Rigidbody" />, — а не по <c>CurrentPlacedObject</c>: стартовый магазин из
    /// префаба и тихая сетевая раскладка не поднимают событие <c>Placed</c>, а парентинг
    /// под якорь есть во всех путях (<c>UxrGrabbableObject</c> перепарентит в якорь сам).
    /// Сверка идёт перед каждым шагом физики, аллокаций нет, пока содержимое якорей не меняется.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AnchoredItemCollisionIgnore : MonoBehaviour
    {
        private Rigidbody                  _body;
        private UxrGrabbableObjectAnchor[] _anchors;
        private Rigidbody[]                _placed;

        private readonly List<Collider> _bodyColliders = new List<Collider>();
        private readonly List<Collider> _itemColliders = new List<Collider>();

        private void Awake()
        {
            _body    = GetComponent<Rigidbody>();
            _anchors = GetComponentsInChildren<UxrGrabbableObjectAnchor>(true);
            _placed  = new Rigidbody[_anchors.Length];
        }

        private void FixedUpdate()
        {
            SyncIgnoredCollisions();
        }

        /// <summary>
        /// Приводит отключённые пары столкновений в соответствие с содержимым якорей.
        /// Публичный — чтобы EditMode-тест мог вызвать сверку без игрового цикла.
        /// </summary>
        public void SyncIgnoredCollisions()
        {
            if (_anchors == null) Awake();

            for (int i = 0; i < _anchors.Length; i++)
            {
                Rigidbody current = _anchors[i] != null ? FindPlacedBody(_anchors[i].transform) : null;
                if (current == _placed[i]) continue;

                if (_placed[i] != null) SetIgnored(_placed[i], false);
                if (current != null) SetIgnored(current, true);

                _placed[i] = current;
            }
        }

        private Rigidbody FindPlacedBody(Transform anchor)
        {
            for (int i = 0; i < anchor.childCount; i++)
            {
                Rigidbody body = anchor.GetChild(i).GetComponent<Rigidbody>();
                if (body != null && body != _body) return body;
            }

            return null;
        }

        private void SetIgnored(Rigidbody item, bool ignore)
        {
            // Коллайдеры корпуса — те, что принадлежат нашему телу. Вставленный предмет со
            // своим Rigidbody забирает свои коллайдеры себе, в этот список они не попадают.
            _bodyColliders.Clear();
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c.attachedRigidbody == _body) _bodyColliders.Add(c);

            _itemColliders.Clear();
            item.GetComponentsInChildren(true, _itemColliders);

            foreach (Collider a in _bodyColliders)
                foreach (Collider b in _itemColliders)
                    Physics.IgnoreCollision(a, b, ignore);

            GameLog.WeaponSystem.Verbose($"[AnchoredItemCollisionIgnore] {name} ↔ {item.name}: ignore={ignore} ({_bodyColliders.Count}×{_itemColliders.Count})", this);
        }
    }
}
