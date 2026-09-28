using System;
using UnityEditor;
using UnityEditor.Build;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>Конфигурация сборки: для проверки на шлеме или для релиза.</summary>
    public enum BuildConfig
    {
        /// <summary>
        /// Тест на шлеме: Development, полные стектрейсы (файл и строка), быстрая компиляция IL2CPP,
        /// минимальный stripping.
        /// </summary>
        Test,

        /// <summary>
        /// Релиз: без Development, IL2CPP Master и оптимизация под скорость, стектрейс только
        /// с именем метода, stripping Low. Stripping выше минимального может вырезать то, что
        /// UltimateXR и Mirror достают рефлексией, — прод-сборку прогонять на шлеме целиком.
        /// </summary>
        Prod
    }

    /// <summary>
    /// Настройки IL2CPP под <see cref="BuildConfig" /> на время одной сборки; <see cref="Dispose" />
    /// возвращает прежние. Player Settings на диске не меняются.
    ///
    /// <para>
    /// Для цели на Mono (Dedicated Server) IL2CPP-настройки ни на что не влияют; от конфигурации
    /// у неё остаётся только флаг Development.
    /// </para>
    /// </summary>
    public sealed class BuildConfigScope : IDisposable
    {
        private readonly NamedBuildTarget _target;
        private readonly Il2CppCodeGeneration _codeGeneration;
        private readonly Il2CppCompilerConfiguration _compiler;
        private readonly Il2CppStacktraceInformation _stacktrace;
        private readonly ManagedStrippingLevel _stripping;

        /// <summary>Флаги сборки под конфигурацию.</summary>
        public BuildOptions Options { get; }

        public BuildConfigScope(NamedBuildTarget target, BuildConfig config)
        {
            _target = target;
            _codeGeneration = PlayerSettings.GetIl2CppCodeGeneration(target);
            _compiler = PlayerSettings.GetIl2CppCompilerConfiguration(target);
            _stacktrace = PlayerSettings.GetIl2CppStacktraceInformation(target);
            _stripping = PlayerSettings.GetManagedStrippingLevel(target);

            if (config == BuildConfig.Test)
            {
                PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
                PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
                PlayerSettings.SetIl2CppStacktraceInformation(target, Il2CppStacktraceInformation.MethodFileLineNumber);
                PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Minimal);
                Options = BuildOptions.Development;
            }
            else
            {
                PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSpeed);
                PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Master);
                PlayerSettings.SetIl2CppStacktraceInformation(target, Il2CppStacktraceInformation.MethodOnly);
                PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Low);
                Options = BuildOptions.None;
            }
        }

        /// <summary>Одна строка для итога сборки.</summary>
        public static string Describe(BuildConfig config) =>
            config == BuildConfig.Test
                ? "тест (Development, IL2CPP Release + быстрая сборка, стектрейс с файлом и строкой, stripping Minimal)"
                : "прод (IL2CPP Master + скорость, стектрейс с именем метода, stripping Low)";

        public void Dispose()
        {
            PlayerSettings.SetIl2CppCodeGeneration(_target, _codeGeneration);
            PlayerSettings.SetIl2CppCompilerConfiguration(_target, _compiler);
            PlayerSettings.SetIl2CppStacktraceInformation(_target, _stacktrace);
            PlayerSettings.SetManagedStrippingLevel(_target, _stripping);
        }
    }
}
