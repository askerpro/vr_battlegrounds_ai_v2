using System;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor.Android;
using UnityEditor.Build;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>
    /// Запуск тестовой Quest-сборки без пробуждения контроллеров: объявляет в Android-манифесте
    /// необязательную поддержку отслеживания рук.
    ///
    /// <para>
    /// VR-приложению (<c>com.oculus.intent.category.VR</c>) без объявленных рук Horizon OS при спящих
    /// контроллерах показывает системный <c>LaunchCheckControllerRequiredDialogActivity</c> вместо
    /// приложения — <c>adb am start</c> для e2e/перф-прогонов упирается в диалог. Oculus XR Plugin записи
    /// рук не пишет. Процессор дописывает в <c>/manifest</c>
    /// <c>&lt;uses-feature android:name="oculus.software.handtracking" android:required="false"/&gt;</c> и
    /// <c>&lt;uses-permission android:name="com.oculus.permission.HAND_TRACKING"/&gt;</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Только Test-сборка профиля Quest.</b> <see cref="GameBuilder" /> взводит <see cref="Arm" /> вокруг
    /// <c>BuildPlayer</c> и снимает <see cref="Disarm" /> в том же finally, что и
    /// <see cref="QuestManifestValidator" />. Не взведён (Prod, планшет, ручные и чужие сборки) — файл
    /// манифеста не читается и не перезаписывается: прод-манифест остаётся байт в байт прежним.
    /// </para>
    ///
    /// <para>
    /// Порядок обработчиков: <c>OculusManifest</c> (10000) → этот процессор (10001) →
    /// <see cref="QuestManifestValidator" /> (10002), который проверяет уже дополненный манифест: в Test записи
    /// рук обязательны, в Prod их быть не должно. Те же правила GameBuilder применяет к готовому APK (aapt).
    /// </para>
    /// </summary>
    public sealed class QuestControllerFreeManifest : IPostGenerateGradleAndroidProject
    {
        /// <summary>Возможность «отслеживание рук» Horizon OS.</summary>
        public const string HandTrackingFeature = "oculus.software.handtracking";

        /// <summary>Разрешение на отслеживание рук.</summary>
        public const string HandTrackingPermission = "com.oculus.permission.HAND_TRACKING";

        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ManifestRelativePath = "src/main/AndroidManifest.xml";

        private static bool _armed;

        /// <summary>После OculusManifest (10000), до QuestManifestValidator (10002).</summary>
        public int callbackOrder => 10001;

        /// <summary>
        /// Взводит процессор на одну сборку Android-профиля. Действует только Quest + Test; для остальных
        /// сочетаний остаётся невзведённым.
        /// </summary>
        public static void Arm(BuildProfile profile, BuildConfig config)
        {
            if (profile == BuildProfile.Server) throw new ArgumentException("манифест есть только у Android-профилей", nameof(profile));

            _armed = profile == BuildProfile.Quest && config == BuildConfig.Test;
        }

        /// <summary>Снимает процессор после сборки.</summary>
        public static void Disarm() => _armed = false;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!_armed) return;

            string manifestPath = Path.Combine(path, ManifestRelativePath);

            if (!File.Exists(manifestPath))
                Fail($"нет манифеста {manifestPath} — записи рук не добавлены");

            var document = new XmlDocument();
            document.Load(manifestPath);

            string error = Patch(document, out bool changed);
            if (error != null) Fail($"{error}. Манифест: {manifestPath}");

            // Повторный прогон по уже дополненному манифесту ничего не меняет и файл не пишет.
            if (changed) document.Save(manifestPath);
        }

        /// <summary>
        /// Дописывает в <c>/manifest</c> записи рук, которых ещё нет. Возвращает ошибку или null;
        /// <paramref name="changed" /> — добавлено ли что-то. Идемпотентно: существующие записи не дублирует.
        /// Уже объявленные обязательными руки (<c>android:required</c> не <c>false</c>, в том числе без
        /// атрибута — по умолчанию Android это <c>true</c>) не исправляет, а возвращает ошибку: обязательные
        /// руки отрежут шлемы и режимы без отслеживания рук — это продуктовое решение, не тестовой сборки.
        /// </summary>
        public static string Patch(XmlDocument document, out bool changed)
        {
            changed = false;

            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "manifest") return "в документе нет корневого <manifest>";

            bool? required = ReadHandTrackingRequired(document);
            if (required == true)
            {
                return $"в манифесте уже есть <uses-feature android:name=\"{HandTrackingFeature}\"> с обязательными руками " +
                       "(android:required не false) — руки стали бы обязательными для установки; это продуктовое решение, " +
                       "тестовая сборка его не меняет";
            }

            if (required == null)
            {
                XmlElement feature = document.CreateElement("uses-feature");
                SetAndroidAttribute(document, feature, "name", HandTrackingFeature);
                SetAndroidAttribute(document, feature, "required", "false");
                InsertBeforeApplication(root, feature);
                changed = true;
            }

            if (!HasHandTrackingPermission(document))
            {
                XmlElement permission = document.CreateElement("uses-permission");
                SetAndroidAttribute(document, permission, "name", HandTrackingPermission);
                InsertBeforeApplication(root, permission);
                changed = true;
            }

            return null;
        }

        /// <summary>
        /// Объявлены ли руки в <c>/manifest/uses-feature</c>: null — записи нет, false — только
        /// <c>android:required="false"</c>, true — хотя бы одна обязательная (нет атрибута или не <c>false</c>).
        /// </summary>
        public static bool? ReadHandTrackingRequired(XmlDocument document)
        {
            XmlElement[] features = AndroidElements(document, "/manifest/uses-feature", HandTrackingFeature);
            if (features.Length == 0) return null;

            return features.Any(e => !string.Equals(e.GetAttribute("required", AndroidNs).Trim(), "false",
                                                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Есть ли <c>/manifest/uses-permission</c> с <see cref="HandTrackingPermission" />.</summary>
        public static bool HasHandTrackingPermission(XmlDocument document) =>
            AndroidElements(document, "/manifest/uses-permission", HandTrackingPermission).Length > 0;

        private static XmlElement[] AndroidElements(XmlDocument document, string xpath, string name) =>
            document.SelectNodes(xpath)
                    ?.Cast<XmlNode>()
                    .OfType<XmlElement>()
                    .Where(e => e.GetAttribute("name", AndroidNs) == name)
                    .ToArray() ?? Array.Empty<XmlElement>();

        private static void SetAndroidAttribute(XmlDocument document, XmlElement element, string localName, string value)
        {
            XmlAttribute attribute = document.CreateAttribute("android", localName, AndroidNs);
            attribute.Value = value;
            element.Attributes.Append(attribute);
        }

        /// <summary>Записи уровня манифеста — перед <c>&lt;application&gt;</c>, как их пишет Android Studio.</summary>
        private static void InsertBeforeApplication(XmlElement root, XmlElement child)
        {
            XmlNode application = root.ChildNodes.Cast<XmlNode>()
                                      .FirstOrDefault(n => n.NodeType == XmlNodeType.Element && n.Name == "application");

            if (application != null) root.InsertBefore(child, application);
            else root.AppendChild(child);
        }

        private static void Fail(string message) =>
            throw new BuildFailedException(GameBuilder.ResultMarker + " Quest Test, записи рук в манифесте: " + message);
    }
}
