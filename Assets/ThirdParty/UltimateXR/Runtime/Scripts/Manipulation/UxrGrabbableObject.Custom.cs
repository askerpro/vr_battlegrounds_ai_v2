using System;
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
