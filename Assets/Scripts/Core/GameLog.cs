using System;
using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Канал логов одной категории: сам знает свой уровень из <see cref="GameSettings" />,
    /// поэтому вызывающей стороне синглтон настроек больше не нужен.
    ///
    /// <para>
    /// Каналы не создаются на месте — готовые лежат в <see cref="GameLog" />:
    /// <code>
    /// GameLog.Match.Info("[SetManager] Сет начат.");
    /// GameLog.Player.Verbose("[PlayerController] Кадр обработан.", this);
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Уровень читается в момент вызова, а не при создании канала: правка
    /// GameSettings.asset в инспекторе действует сразу, без перезапуска игры.
    /// </para>
    /// </summary>
    public readonly struct GameLogChannel
    {
        private readonly Func<LogLevel> _resolveLevel;

        internal GameLogChannel(Func<LogLevel> resolveLevel)
        {
            _resolveLevel = resolveLevel;
        }

        /// <summary>
        /// Текущий уровень категории. У канала, созданного через <c>default</c>,
        /// резолвера нет — такой канал ведёт себя как <see cref="LogLevel.Info" />,
        /// чтобы отсутствие настройки не гасило логи молча.
        /// </summary>
        public LogLevel Level
        {
            get { return _resolveLevel != null ? _resolveLevel() : LogLevel.Info; }
        }

        /// <summary>
        /// Пропустит ли канал сообщение указанного уровня. Нужно только там, где
        /// дорога сама подготовка текста и её стоит пропустить целиком.
        /// </summary>
        public bool IsEnabled(LogLevel level)
        {
            return Level >= level;
        }

        /// <summary>Детальный поток событий. Пишется при уровне категории Verbose.</summary>
        public void Verbose(string message, UnityEngine.Object context = null)
        {
            if (Level < LogLevel.Verbose)
                return;

            Write(message, context);
        }

        /// <summary>Значимое событие. Пишется при уровне категории Info и выше.</summary>
        public void Info(string message, UnityEngine.Object context = null)
        {
            if (Level < LogLevel.Info)
                return;

            Write(message, context);
        }

        /// <summary>Проблема, не ломающая игру. Пишется при уровне категории Warnings и выше.</summary>
        public void Warning(string message, UnityEngine.Object context = null)
        {
            if (Level < LogLevel.Warnings)
                return;

            if (context != null)
                UnityEngine.Debug.LogWarning(message, context);
            else
                UnityEngine.Debug.LogWarning(message);
        }

        /// <summary>
        /// Сбой. Пишется всегда: уровень категории на ошибки не влияет, потерять их нельзя.
        /// Тождественно <see cref="GameLog.Error" /> — категория здесь только для читаемости.
        /// </summary>
        public void Error(string message, UnityEngine.Object context = null)
        {
            GameLog.Error(message, context);
        }

        private static void Write(string message, UnityEngine.Object context)
        {
            if (context != null)
                UnityEngine.Debug.Log(message, context);
            else
                UnityEngine.Debug.Log(message);
        }
    }

    /// <summary>
    /// Точка входа в логи игры. Категория выбирается каналом, а не аргументом вызова:
    ///
    /// <code>
    /// GameLog.Network.Info("[MapManager] Загрузка карты...");
    /// GameLog.Player.Warning("[PlayerController] Предупреждение");
    /// GameLog.Error("[GameNetworkManager] Критическая ошибка");
    /// </code>
    ///
    /// <para>
    /// Категории один в один повторяют уровни из <see cref="GameSettings" />:
    /// добавили поле там — добавьте канал здесь.
    /// </para>
    ///
    /// <para>
    /// Это единственное место в игровом коде, где допустим прямой <c>UnityEngine.Debug.Log*</c>.
    /// </para>
    /// </summary>
    public static class GameLog
    {
        /// <summary>Сетевые системы: GameNetworkManager, MapManager, Discovery, NetworkStateRelay.</summary>
        public static readonly GameLogChannel Network =
            new GameLogChannel(() => GameSettings.Instance.LogLevelNetwork);

        /// <summary>Игрок: PlayerController, PlayerSession, HUD игрока.</summary>
        public static readonly GameLogChannel Player =
            new GameLogChannel(() => GameSettings.Instance.LogLevelPlayer);

        /// <summary>Матч: GameplayManager, SetManager, RoundManager, режимы.</summary>
        public static readonly GameLogChannel Match =
            new GameLogChannel(() => GameSettings.Instance.LogLevelMatch);

        /// <summary>Инструменты отладки: DebugOrchestrator, ManagerBootstrap, харнесс E2E.</summary>
        public static readonly GameLogChannel Debug =
            new GameLogChannel(() => GameSettings.Instance.LogLevelDebug);

        /// <summary>Система оружия: стрельба, перезарядка, механика затвора.</summary>
        public static readonly GameLogChannel WeaponSystem =
            new GameLogChannel(() => GameSettings.Instance.LogLevelWeaponSystem);

        /// <summary>Интерфейс: меню, кнопки, HUD.</summary>
        public static readonly GameLogChannel UI =
            new GameLogChannel(() => GameSettings.Instance.LogLevelUI);

        /// <summary>Физическое пространство: калибровка, PhysicalSpaceSyncManager.</summary>
        public static readonly GameLogChannel PhysicalSpace =
            new GameLogChannel(() => GameSettings.Instance.LogLevelPhysicalSpace);

        /// <summary>Арсенал: ArsenalWallController, слоты, жетон.</summary>
        public static readonly GameLogChannel Arsenal =
            new GameLogChannel(() => GameSettings.Instance.LogLevelArsenal);

        /// <summary>Производительность: стресс-тест, рекордер кадров.</summary>
        public static readonly GameLogChannel Perf =
            new GameLogChannel(() => GameSettings.Instance.LogLevelPerf);

        /// <summary>
        /// Сбой вне какой-либо категории. Пишется всегда — независимо от настроек.
        /// Если категория известна, лучше <c>GameLog.Match.Error(...)</c>: тот же
        /// результат, но в коде видно, чья это ошибка.
        /// </summary>
        public static void Error(string message, UnityEngine.Object context = null)
        {
            if (context != null)
                UnityEngine.Debug.LogError(message, context);
            else
                UnityEngine.Debug.LogError(message);
        }
    }
}
