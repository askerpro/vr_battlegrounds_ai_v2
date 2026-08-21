using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Дирижёр инициализации постоянных менеджеров.
    ///
    /// <para>
    /// Задача — убрать угадывание. Раньше каждая система сама решала, готов ли сосед:
    /// <c>DebugOrchestrator</c> пробовал запустить матч «ещё раз через кадр», а
    /// <c>MapManager</c> ждал игрока пять секунд по таймеру. Оба работали, оба тихо
    /// ломались при изменении порядка загрузки.
    /// </para>
    ///
    /// <para>
    /// Что делает этот класс. Держит объявленный состав постоянных менеджеров
    /// (порядок — в <see cref="ManagerOrder" />), проверяет после всех <c>Awake</c>,
    /// что состав на месте, и один раз поднимает сигнал <see cref="Ready" />.
    /// Подписка на него — замена опросу «а появился ли уже сосед».
    /// </para>
    ///
    /// <para>
    /// Чего он <b>не</b> делает: не создаёт менеджеры. Они лежат готовыми компонентами
    /// на префабе <c>--- MANAGERS ---</c>, и создаёт их Unity при загрузке сцены
    /// <c>Offline</c>. Порядок их <c>Awake</c> задаётся атрибутами
    /// <c>[DefaultExecutionOrder]</c> со значениями из <see cref="ManagerOrder" />;
    /// здесь — только объявление состава и проверка, что он соблюдён.
    /// </para>
    ///
    /// <para>
    /// Точка входа — <see cref="PersistentRoot" />: <c>Awake</c> объявляет состав,
    /// <c>Start</c> его проверяет. Между ними Unity успевает вызвать <c>Awake</c>
    /// всех менеджеров: первый <c>Start</c> в сцене гарантированно позже последнего
    /// <c>Awake</c>, и на этом гарантия построена.
    /// </para>
    /// </summary>
    public static class ManagerBootstrap
    {
        /// <summary>
        /// Описание одного менеджера в объявленном составе.
        /// <see cref="Type" /> обязан иметь статическое свойство <c>Instance</c> —
        /// именно по нему проверяется, что менеджер поднялся.
        /// </summary>
        private readonly struct ManagerSlot
        {
            public readonly int Order;
            public readonly Type Type;
            public readonly bool Required;
            public readonly string Lifetime;

            public ManagerSlot(int order, Type type, bool required, string lifetime)
            {
                Order = order;
                Type = type;
                Required = required;
                Lifetime = lifetime;
            }
        }

        /// <summary>
        /// Объявленный состав постоянных менеджеров, сверху вниз в порядке инициализации.
        ///
        /// <para>
        /// <c>Required</c> означает «без него игра не работает» — отсутствие пишется
        /// как <c>Error</c>. Необязательные пишутся как <c>Warning</c>: их отсутствие
        /// выключает подсистему, но не ломает запуск.
        /// </para>
        /// </summary>
        private static readonly ManagerSlot[] PersistentRoster =
        {
            new ManagerSlot(ManagerOrder.PlayersManager, typeof(PlayersManager), true,
                "префаб «--- MANAGERS ---», от Offline до конца процесса"),

            new ManagerSlot(ManagerOrder.SessionRecoveryManager, typeof(SessionRecoveryManager), false,
                "нигде не размещён — восстановление сессий при переподключении не работает"),

            new ManagerSlot(ManagerOrder.AvatarManager, typeof(Player.Avatars.AvatarManager), true,
                "префаб «--- MANAGERS ---», от Offline до конца процесса"),

            new ManagerSlot(ManagerOrder.MapManager, typeof(MapManager), true,
                "префаб «--- MANAGERS ---», от Offline до конца процесса"),

            new ManagerSlot(ManagerOrder.PhysicalSpaceSyncManager, typeof(PhysicalSpaceUtils.PhysicalSpaceSyncManager), true,
                "корень префаба «--- MANAGERS ---», от Offline до конца процесса")
        };

        /// <summary>
        /// Постоянные менеджеры проверены и готовы к работе. Взводится один раз
        /// за процесс и больше не сбрасывается: <see cref="PersistentRoot" /> переживает
        /// смену сцен, а вместе с ним и весь объявленный состав.
        /// </summary>
        public static bool IsReady { get; private set; }

        private static event Action ReadyInternal;

        /// <summary>
        /// Постоянные менеджеры готовы. Подписка вместо опроса «а появился ли сосед».
        ///
        /// <para>
        /// Опоздавший подписчик не теряет событие: если готовность уже наступила,
        /// обработчик вызывается сразу при подписке. Иначе система, стартовавшая
        /// на кадр позже, снова начала бы угадывать — ровно то, от чего уходим.
        /// </para>
        /// </summary>
        public static event Action Ready
        {
            add
            {
                if (IsReady) value?.Invoke();
                else ReadyInternal += value;
            }
            remove { ReadyInternal -= value; }
        }

        /// <summary>
        /// Объявляет состав. Зовётся из <c>PersistentRoot.Awake</c> — то есть раньше
        /// <c>Awake</c> самих менеджеров, поэтому проверять их <c>Instance</c> здесь
        /// ещё нечем. Смысл вызова в другом: записать в лог порядок, по которому всё
        /// дальше и должно происходить, чтобы при разборе лога не гадать.
        /// </summary>
        internal static void Declare()
        {
            if (!GameLog.Debug.IsEnabled(LogLevel.Verbose)) return;

            List<string> lines = new List<string>();
            foreach (ManagerSlot slot in PersistentRoster)
                lines.Add($"  {slot.Order,6}  {slot.Type.Name} — {slot.Lifetime}");

            GameLog.Debug.Verbose(
                "[ManagerBootstrap] Объявленный порядок постоянных менеджеров:\n" +
                string.Join("\n", lines.ToArray()));
        }

        /// <summary>
        /// Проверяет состав и поднимает <see cref="Ready" />. Зовётся из
        /// <c>PersistentRoot.Start</c>: к этому моменту <c>Awake</c> всех менеджеров
        /// сцены уже отработал.
        ///
        /// <para>
        /// Повторный вызов безвреден и ничего не делает: <see cref="PersistentRoot" />
        /// переживает смену сцен, но при загрузке новой сцены её собственная копия
        /// префаба может успеть вызвать <c>Start</c> до того, как дубликат уничтожится.
        /// </para>
        /// </summary>
        internal static void Verify()
        {
            if (IsReady) return;

            List<string> missing = new List<string>();

            foreach (ManagerSlot slot in PersistentRoster)
            {
                if (ResolveInstance(slot.Type) != null) continue;

                missing.Add(slot.Type.Name);

                string message =
                    $"[ManagerBootstrap] {slot.Type.Name}.Instance пуст после инициализации. " +
                    $"Ожидаемое место жизни: {slot.Lifetime}. " +
                    "Состав объявлен в ManagerBootstrap.PersistentRoster, таблица времён жизни — " +
                    "Docs/session-architecture.md.";

                if (slot.Required) GameLog.Error(message);
                else GameLog.Debug.Warning(message);
            }

            IsReady = true;

            GameLog.Debug.Info(missing.Count == 0
                ? $"[ManagerBootstrap] Постоянные менеджеры готовы: {PersistentRoster.Length} из {PersistentRoster.Length}."
                : $"[ManagerBootstrap] Постоянные менеджеры готовы частично, нет: {string.Join(", ", missing.ToArray())}.");

            Action handlers = ReadyInternal;
            ReadyInternal = null;
            handlers?.Invoke();
        }

        /// <summary>
        /// Читает статическое свойство <c>Instance</c> объявленного типа.
        ///
        /// <para>
        /// Через рефлексию, потому что общего интерфейса у менеджеров нет, а вводить
        /// его значило бы менять их API — что задача T-17 прямо запрещает. Цена —
        /// один вызов на менеджер за весь запуск процесса.
        /// </para>
        ///
        /// <para>
        /// Сравнение приводится к <see cref="UnityEngine.Object" />: у уничтоженного
        /// объекта ссылка не <c>null</c> по правилам C#, но <c>null</c> по правилам Unity,
        /// и различать эти два случая здесь незачем — оба означают «менеджера нет».
        /// </para>
        /// </summary>
        private static UnityEngine.Object ResolveInstance(Type managerType)
        {
            PropertyInfo property = managerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            if (property == null)
            {
                GameLog.Error(
                    $"[ManagerBootstrap] У типа {managerType.FullName} нет статического свойства Instance. " +
                    "Состав в ManagerBootstrap.PersistentRoster рассчитан на синглтоны — " +
                    "либо тип попал в список по ошибке, либо свойство переименовали.");
                return null;
            }

            UnityEngine.Object instance = property.GetValue(null) as UnityEngine.Object;
            return instance != null ? instance : null;
        }
    }
}
