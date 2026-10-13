using System;
using System.Collections.Generic;
using System.Text;
using UltimateXR.Core;
using UltimateXR.Core.Settings;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Ограничивает уровни логов UltimateXR в собранных плеерах: не выше <see cref="UxrLogLevel.Warnings"/>.
    ///
    /// <para>
    /// <b>Зачем</b> (находка 5871, этап statesync-cost задачи network-sync-hardening). В
    /// <c>Resources/UxrGlobalSettings.asset</c> у Avatar, Core, Locomotion, Manipulation и Networking стоит Debug.
    /// С ним <c>UxrManager.ExecuteStateSyncEvent</c> пишет лог со стектрейсом на каждое событие синхронизации, разбор
    /// снимка — на каждый компонент, <c>UxrGrabManager</c> — на каждый захват. На шлеме и сервере это стоимость
    /// каждого события: строка, стектрейс и запись в файл лога.
    /// </para>
    ///
    /// <para>
    /// <b>Правила.</b> Во всех плеерах (Release и Development) каждый из 10 уровней = min(уровень из ассета,
    /// Warnings). Исходные Verbose/Debug из ассета возвращаются, пока включён отладочный режим
    /// (<see cref="DebugMode.Enabled"/>, переключение подхватывается через <see cref="DebugMode.Changed"/>)
    /// или если в командной строке есть флаг <see cref="VerboseFlag"/> (без учёта регистра). Уровни ниже
    /// Warnings из ассета не повышаются. Сам ассет не меняется: SDK читает уровни при каждой проверке, поэтому
    /// достаточно поправить экземпляр в памяти.
    /// </para>
    ///
    /// <para>
    /// <b>Почему не в редакторе.</b> В редакторе <see cref="UxrGlobalSettings.Instance"/> — загруженный ассет
    /// проекта, и сеттер меняет его в памяти: изменение утекло бы в инспектор и при сохранении проекта — в
    /// сам ассет. Поэтому в редакторе политика ничего не делает, уровни берутся из ассета как есть.
    /// </para>
    /// </summary>
    public static class UxrLogLevelPolicy
    {
        /// <summary>Флаг командной строки: вернуть уровни из ассета без отладочного режима.</summary>
        public const string VerboseFlag = "-uxr-log-verbose";

        /// <summary>Потолок уровня логов UltimateXR, когда подробные логи не разрешены.</summary>
        public const UxrLogLevel Ceiling = UxrLogLevel.Warnings;

        /// <summary>Уровень категории: исходный, если подробные логи разрешены, иначе не выше <see cref="Ceiling"/>.</summary>
        public static UxrLogLevel Clamp(UxrLogLevel configured, bool verboseAllowed)
        {
            return verboseAllowed || configured <= Ceiling ? configured : Ceiling;
        }

        /// <summary>Разрешены ли подробные логи: отладочный режим или флаг <see cref="VerboseFlag"/> в аргументах.</summary>
        public static bool VerboseAllowed(bool debugMode, IReadOnlyList<string> args)
        {
            if (debugMode) return true;
            if (args == null) return false;

            for (int i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], VerboseFlag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Сеттер правит ассет проекта в памяти — см. описание класса.
            if (Application.isEditor) return;

            UxrGlobalSettings settings = UxrGlobalSettings.Instance;
            s_configured = new UxrLogLevel[Categories.Length];

            for (int i = 0; i < Categories.Length; i++)
            {
                s_configured[i] = Categories[i].Get(settings);
            }

            s_args = Environment.GetCommandLineArgs();
            Apply(DebugMode.Enabled, "запуск");

            DebugMode.Changed -= OnDebugModeChanged;
            DebugMode.Changed += OnDebugModeChanged;
        }

        private static void OnDebugModeChanged(bool enabled)
        {
            Apply(enabled, enabled ? "отладочный режим включён" : "отладочный режим выключен");
        }

        /// <summary>Ставит каждый уровень по <see cref="Clamp"/>; одна строка в лог, только если что-то изменилось.</summary>
        private static void Apply(bool debugMode, string reason)
        {
            if (s_configured == null) return;

            bool verbose = VerboseAllowed(debugMode, s_args);
            UxrGlobalSettings settings = UxrGlobalSettings.Instance;
            StringBuilder changes = null;

            for (int i = 0; i < Categories.Length; i++)
            {
                UxrLogLevel current = Categories[i].Get(settings);
                UxrLogLevel target = Clamp(s_configured[i], verbose);
                if (current == target) continue;

                Categories[i].Set(settings, target);
                changes = changes == null ? new StringBuilder() : changes.Append(", ");
                changes.Append(Categories[i].Name).Append(' ').Append(current).Append('→').Append(target);
            }

            if (changes == null) return;

            GameLog.Network.Info($"[UxrLogLevelPolicy] {reason}: {changes} " +
                                 $"(подробные логи UltimateXR {(verbose ? "разрешены" : $"ограничены уровнем {Ceiling}")}).");
        }

        private readonly struct Category
        {
            public readonly string Name;
            public readonly Func<UxrGlobalSettings, UxrLogLevel> Get;
            public readonly Action<UxrGlobalSettings, UxrLogLevel> Set;

            public Category(string name, Func<UxrGlobalSettings, UxrLogLevel> get, Action<UxrGlobalSettings, UxrLogLevel> set)
            {
                Name = name;
                Get = get;
                Set = set;
            }
        }

        /// <summary>Все 10 категорий <see cref="UxrGlobalSettings"/>.</summary>
        private static readonly Category[] Categories =
        {
            new Category("Animation", s => s.LogLevelAnimation, (s, v) => s.LogLevelAnimation = v),
            new Category("Avatar", s => s.LogLevelAvatar, (s, v) => s.LogLevelAvatar = v),
            new Category("Core", s => s.LogLevelCore, (s, v) => s.LogLevelCore = v),
            new Category("Devices", s => s.LogLevelDevices, (s, v) => s.LogLevelDevices = v),
            new Category("Locomotion", s => s.LogLevelLocomotion, (s, v) => s.LogLevelLocomotion = v),
            new Category("Manipulation", s => s.LogLevelManipulation, (s, v) => s.LogLevelManipulation = v),
            new Category("Networking", s => s.LogLevelNetworking, (s, v) => s.LogLevelNetworking = v),
            new Category("Rendering", s => s.LogLevelRendering, (s, v) => s.LogLevelRendering = v),
            new Category("UI", s => s.LogLevelUI, (s, v) => s.LogLevelUI = v),
            new Category("Weapons", s => s.LogLevelWeapons, (s, v) => s.LogLevelWeapons = v),
        };

        /// <summary>Уровни из ассета на момент запуска — источник при каждом пересчёте.</summary>
        private static UxrLogLevel[] s_configured;

        /// <summary>Аргументы командной строки (читаются один раз при запуске).</summary>
        private static string[] s_args;
    }
}
