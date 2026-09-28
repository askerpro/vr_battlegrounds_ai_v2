using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>
    /// Настройки XR Plug-in Management на время одной сборки; <see cref="Dispose" /> возвращает
    /// всё как было.
    ///
    /// <para>
    /// Quest и планшет — оба Android, а XR Management хранит лоадеры по группе платформ. Quest
    /// нужен <c>OculusLoader</c> (без него приложение стартует плоским окном), планшету — ни
    /// одного: с лоадером Oculus в манифест попадает VR-категория. Серверу XR не нужен вовсе —
    /// у Standalone стоит Oculus для Link, а headless-сервер пытался бы поднять шлем.
    /// </para>
    ///
    /// <para>
    /// <b>Почему не <c>XRPackageMetadataStore.AssignLoader</c>.</b> Он зовёт
    /// <c>AssetDatabase.SaveAssets()</c> — сохраняет всё грязное в памяти, включая id UltimateXR,
    /// выданные и не сохранённые (known-issues #11). Здесь лоадеры меняются только в памяти:
    /// процессоры сборки читают настройки оттуда, а файл остаётся нетронутым.
    /// </para>
    /// </summary>
    public sealed class XrBuildSettingsScope : IDisposable
    {
        private const string OculusLoaderType = "Unity.XR.Oculus.OculusLoader";

        private readonly struct Snapshot
        {
            public readonly XRGeneralSettings Settings;
            public readonly bool InitOnStart;
            public readonly List<XRLoader> Loaders;

            public Snapshot(XRGeneralSettings settings)
            {
                Settings = settings;
                InitOnStart = settings.InitManagerOnStart;
                Loaders = settings.Manager != null ? settings.Manager.activeLoaders.ToList() : null;
            }
        }

        private readonly List<Snapshot> _snapshots = new List<Snapshot>();

        /// <summary>Выставляет XR под профиль. Бросает, если Quest нечем запустить в VR.</summary>
        public XrBuildSettingsScope(BuildProfile profile)
        {
            switch (profile)
            {
                case BuildProfile.Server:
                    Apply(BuildTargetGroup.Standalone, initOnStart: false, loaders: null);
                    break;

                case BuildProfile.Quest:
                    XRLoader oculus = FindLoader(OculusLoaderType) ??
                                      throw new InvalidOperationException("не найден ассет OculusLoader (Assets/XR/Loaders)");
                    Apply(BuildTargetGroup.Android, initOnStart: true, loaders: new List<XRLoader> { oculus });
                    break;

                case BuildProfile.Tablet:
                    Apply(BuildTargetGroup.Android, initOnStart: false, loaders: new List<XRLoader>());
                    break;
            }
        }

        /// <summary>
        /// Заводит группе Android настройки XR, если их нет. Это единственное, что пишется на диск:
        /// API XR Management сохраняет их сам, через <c>SaveAssets</c>, — поэтому звать строго до
        /// сверки id. Возвращает true, если настройки созданы.
        /// </summary>
        public static bool EnsureAndroidSettings()
        {
            if (XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android)?.Manager != null)
                return false;

            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) ||
                perTarget == null)
                throw new InvalidOperationException("нет XRGeneralSettingsPerBuildTarget — XR Plug-in Management не настроен");

            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            // По умолчанию Android без XR: лоадер ставит только сборка Quest и на время сборки.
            XRGeneralSettings android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            android.InitManagerOnStart = false;
            EditorUtility.SetDirty(android);
            AssetDatabase.SaveAssetIfDirty(perTarget);
            return true;
        }

        /// <summary>Стоит ли у группы лоадер Oculus — проверка перед сборкой Quest.</summary>
        public static bool HasOculusLoader(BuildTargetGroup group)
        {
            XRManagerSettings manager = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group)?.Manager;
            return manager != null && manager.activeLoaders.Any(l => l != null && l.GetType().FullName == OculusLoaderType);
        }

        public void Dispose()
        {
            foreach (Snapshot s in _snapshots)
            {
                if (s.Settings == null) continue;

                s.Settings.InitManagerOnStart = s.InitOnStart;
                if (s.Loaders != null && s.Settings.Manager != null) s.Settings.Manager.TrySetLoaders(s.Loaders);
            }

            _snapshots.Clear();
        }

        private void Apply(BuildTargetGroup group, bool initOnStart, List<XRLoader> loaders)
        {
            XRGeneralSettings settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);

            if (settings == null)
            {
                if (initOnStart) throw new InvalidOperationException($"нет настроек XR для {group}");
                return;
            }

            _snapshots.Add(new Snapshot(settings));
            settings.InitManagerOnStart = initOnStart;

            if (loaders == null) return;

            if (settings.Manager == null)
            {
                if (loaders.Count > 0) throw new InvalidOperationException($"у настроек XR {group} нет XRManagerSettings");
                return;
            }

            if (!settings.Manager.TrySetLoaders(loaders))
                throw new InvalidOperationException($"XR Management не принял список лоадеров для {group}");
        }

        private static XRLoader FindLoader(string typeName)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:XRLoader"))
            {
                var loader = AssetDatabase.LoadAssetAtPath<XRLoader>(AssetDatabase.GUIDToAssetPath(guid));
                if (loader != null && loader.GetType().FullName == typeName) return loader;
            }

            return null;
        }
    }
}
