using System;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Avatar;

namespace UltimateXR.Manipulation
{
    public partial class UxrGrabbableObject
    {
        // Static validator removed in favor of UxrGrabber.CanGrabDelegate

        /// <summary>
        ///     Создавать ли «Auto Anchor» в <c>Awake</c>. Раскрывает сериализованное поле
        ///     <c>_autoCreateStartAnchor</c>, которое в оригинале SDK приватно.
        ///
        ///     <para>
        ///     Зачем. Объект, заспавненный по сети, обязан родиться одинаково на всех машинах.
        ///     С включённым флагом <c>Awake</c> создаёт объекту лишнего родителя-якорь, чей
        ///     <c>UniqueId</c> на каждой машине свой, — и событие захвата, которое ссылается
        ///     на этот якорь, на принимающей стороне не разрешается (находка NET-16).
        ///     Гасить флаг надо строго <b>до</b> активации объекта.
        ///     </para>
        ///
        ///     <para>
        ///     Раньше то же самое делалось рефлексией из <c>ArsenalWallController</c> —
        ///     зависимость, которая при обновлении SDK ломалась молча. Разбор —
        ///     Docs/UltimateXR/sdk-patches.md, «Патч 6».
        ///     </para>
        /// </summary>
        public bool AutoCreateStartAnchor
        {
            get => _autoCreateStartAnchor;
            set => _autoCreateStartAnchor = value;
        }

        /// <summary>
        ///     Кладёт предмет в якорь на уровне учёта: без событий, без анимации перехода
        ///     и без канала синхронизации UltimateXR.
        ///
        ///     <para>
        ///     Зачем. Сетевая выдача предмета — это не действие игрока: объект уже стоит
        ///     на месте, его положение приехало спавн-сообщением Mirror, и каждая машина
        ///     обязана прийти к одному и тому же учёту сама. Публичный
        ///     <see cref="UxrGrabManager.PlaceObject" /> для этого не годится: он обёрнут
        ///     в <c>BeginSync</c> и породил бы сетевое событие на каждую выдачу — на сервере
        ///     веерную рассылку, а у клиента ещё и команду серверу «положи предмет»,
        ///     то есть подмену авторитета.
        ///     </para>
        ///
        ///     <para>
        ///     Без этого учёта <see cref="CurrentAnchor" /> у выданного по сети предмета
        ///     остаётся пустым, и при захвате <c>UxrGrabManager</c> не поднимает у якоря
        ///     событие <c>Removed</c> — слот арсенала не узнаёт, что предмет унесли
        ///     (находка NET-17).
        ///     </para>
        /// </summary>
        /// <param name="anchor">Якорь, в котором лежит предмет, или null, чтобы отвязать</param>
        /// <summary>
        ///     VR Battlegrounds patch 25: включённые предметы, у которых хотя бы одна точка хвата берётся
        ///     не кнопками по умолчанию (<see cref="UxrGrabPointInfo.UseDefaultGrabButtons" /> = false).
        ///     Нужен <c>UxrStandardAvatarController</c>: переопределение кнопок хвата возможно, только
        ///     если такой предмет в досягаемости руки, — иначе полный перебор всех предметов сцены
        ///     каждый кадр для каждой руки не нужен. Список обновляется при включении и выключении
        ///     предмета; точки хвата в рантайме не меняются.
        /// </summary>
        public static readonly System.Collections.Generic.List<UxrGrabbableObject> EnabledWithCustomGrabButtons =
                    new System.Collections.Generic.List<UxrGrabbableObject>();

        private bool HasCustomGrabButtons()
        {
            for (int i = 0; i < GrabPointCount; ++i)
            {
                UxrGrabPointInfo info = GetGrabPoint(i);

                if (info != null && !info.UseDefaultGrabButtons)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        protected override void OnEnable()
        {
            base.OnEnable();

            if (HasCustomGrabButtons() && !EnabledWithCustomGrabButtons.Contains(this))
            {
                EnabledWithCustomGrabButtons.Add(this);
            }
        }

        private void UnregisterCustomGrabButtons()
        {
            EnabledWithCustomGrabButtons.Remove(this);
        }

        /// <summary>
        ///     VR Battlegrounds patch 28: запас грубой отсечки на ход подвижных деталей (затвор, помпа) и
        ///     погрешность — proximity-трансформы точек могут сдвинуться относительно корня предмета.
        /// </summary>
        private const float CoarseGrabRangeMargin = 0.15f;

        private struct CoarseGrabReach
        {
            public UxrAvatar   Avatar;
            public UxrHandSide Side;
            public float       Reach; // < 0 — отсечка для этой пары невозможна
        }

        private readonly System.Collections.Generic.List<CoarseGrabReach> _coarseGrabReaches = new System.Collections.Generic.List<CoarseGrabReach>(2);
        private int _coarseGrabMode; // 0 — не вычислен, 1 — отсечка работает, 2 — не применима

        /// <summary>
        ///     VR Battlegrounds patch 28: грубая отсечка «рука заведомо дальше любой точки хвата». По неравенству
        ///     треугольника: если расстояние от захватчика до корня предмета больше, чем
        ///     (самый дальний proximity-трансформ руки) + max(смещение proximity-трансформа точки от корня +
        ///     MaxDistanceGrab) + запас, ни одна точка не пройдёт проверку расстояния в
        ///     <see cref="CanBeGrabbedByGrabber" /> — результат тот же, что у полного расчёта, но за одно
        ///     сравнение вместо чтения трансформов, поиска позы хвата аватара и угла на каждую точку.
        ///     Не применяется к предметам с <see cref="UxrGrabPointShape" />, BoxConstrained-точками или
        ///     proximity-трансформом вне иерархии предмета — там расстояние считается иначе.
        /// </summary>
        internal bool IsOutsideCoarseGrabRange(UxrGrabber grabber)
        {
            if (grabber == null || grabber.Avatar == null)
            {
                return false;
            }

            if (_coarseGrabMode == 0)
            {
                _coarseGrabMode = ComputeCoarseGrabMode();
            }

            if (_coarseGrabMode != 1)
            {
                return false;
            }

            float objectReach = GetCoarseObjectReach(grabber);

            if (objectReach < 0.0f)
            {
                return false;
            }

            float range = grabber.CoarseProximityReach + objectReach + CoarseGrabRangeMargin;
            return (grabber.transform.position - transform.position).sqrMagnitude > range * range;
        }

        private int ComputeCoarseGrabMode()
        {
            for (int i = 0; i < GrabPointCount; ++i)
            {
                UxrGrabPointInfo info = GetGrabPoint(i);

                if (info == null || GetGrabPointShape(i) != null || info.GrabProximityMode != UxrGrabProximityMode.UseProximity)
                {
                    return 2;
                }
            }

            return 1;
        }

        private float GetCoarseObjectReach(UxrGrabber grabber)
        {
            for (int i = _coarseGrabReaches.Count - 1; i >= 0; --i)
            {
                CoarseGrabReach cached = _coarseGrabReaches[i];

                if (cached.Avatar == null)
                {
                    _coarseGrabReaches.RemoveAt(i); // аватар уничтожен
                    continue;
                }

                if (cached.Avatar == grabber.Avatar && cached.Side == grabber.Side)
                {
                    return cached.Reach;
                }
            }

            float reach = 0.0f;

            for (int i = 0; i < GrabPointCount; ++i)
            {
                UnityEngine.Transform proximity = GetGrabPointGrabProximityTransform(grabber, i);

                if (proximity == null || (proximity != transform && !proximity.IsChildOf(transform)))
                {
                    reach = -1.0f;
                    break;
                }

                reach = UnityEngine.Mathf.Max(reach, UnityEngine.Vector3.Distance(proximity.position, transform.position) + UnityEngine.Mathf.Max(0.0f, GetGrabPoint(i).MaxDistanceGrab));
            }

            _coarseGrabReaches.Add(new CoarseGrabReach { Avatar = grabber.Avatar, Side = grabber.Side, Reach = reach });
            return reach;
        }

        public void SetNetworkAnchor(UxrGrabbableObjectAnchor anchor)
        {
            if (CurrentAnchor == anchor)
            {
                return;
            }

            if (CurrentAnchor != null && CurrentAnchor.CurrentPlacedObject == this)
            {
                CurrentAnchor.CurrentPlacedObject = null;
            }

            CurrentAnchor = anchor;

            if (anchor != null)
            {
                anchor.CurrentPlacedObject = this;
            }
        }
    }
}
