using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>
    /// Проверка Android-манифеста сборки <see cref="GameBuilder" /> сразу после генерации Gradle-проекта —
    /// до Gradle, за минуты до готового APK.
    ///
    /// <para>
    /// <c>OculusManifest</c> (Oculus XR Plugin, callbackOrder 10000) дописывает в манифест
    /// <c>com.oculus.intent.category.VR</c> и <c>com.oculus.supportedDevices</c>, только если видит у
    /// Android лоадер Oculus. Не увидел — молча выходит, и Quest-APK открывается на шлеме плоским окном.
    /// Этот процессор идёт последним и роняет сборку с диагностикой настроек XR.
    /// </para>
    ///
    /// <para>
    /// Порядок обработчиков: <c>OculusManifest</c> (10000) → <see cref="QuestControllerFreeManifest" /> (10001,
    /// записи рук в Test-сборке Quest) → этот процессор (10002). Он проверяет уже дополненный манифест: в Test
    /// записи рук обязательны (без них Horizon OS при спящих контроллерах покажет диалог «нужен контроллер»),
    /// в Prod их быть не должно.
    /// </para>
    ///
    /// <para>
    /// <b>OculusManifest есть только в editor-сборке под Android.</b> Класс обёрнут в <c>#if UNITY_ANDROID</c>:
    /// в редакторе, скомпилированном под Standalone, его нет вовсе, а переключение платформы внутри одного
    /// вызова editor-скрипты не перекомпилирует (2026-10-12, аренда 516: worker на Standalone, APK плоский).
    /// Поэтому <see cref="GameBuilder" /> до сборки Quest проверяет <see cref="OculusManifestCompiled" />,
    /// а этот процессор — вторая линия.
    /// </para>
    ///
    /// <para>
    /// Работает только во время сборки GameBuilder: профиль и конфигурацию взводит <see cref="Arm" /> вокруг
    /// <c>BuildPlayer</c> и снимает <see cref="Disarm" />. Ручные сборки и чужие пайплайны не трогает.
    /// Те же правила GameBuilder применяет к готовому APK через aapt, передавая профиль и конфигурацию явно
    /// (<see cref="Evaluate" />); профиль и конфигурация хранятся здесь только между Arm и Disarm.
    /// </para>
    /// </summary>
    public sealed class QuestManifestValidator : IPostGenerateGradleAndroidProject
    {
        /// <summary>Категория запуска в VR: без неё Horizon OS открывает приложение плоским окном.</summary>
        public const string VrCategory = "com.oculus.intent.category.VR";

        /// <summary>Мета-данные со списком шлемов через «|».</summary>
        public const string SupportedDevicesKey = "com.oculus.supportedDevices";

        private const string ManifestRelativePath = "src/main/AndroidManifest.xml";

        /// <summary>Процессор манифеста Oculus XR Plugin (internal, сборка Unity.XR.Oculus.Editor).</summary>
        private const string OculusManifestType = "UnityEditor.XR.Oculus.OculusManifest";

        /// <summary>
        /// Имена Quest 3 в supportedDevices: Oculus XR Plugin пишет кодовое <c>eureka</c>
        /// (TargetQuest3 в OculusSettings), Meta XR SDK — <c>quest3</c>.
        /// </summary>
        private static readonly string[] Quest3Names = { "eureka", "quest3" };

        private static BuildProfile? _expected;
        private static BuildConfig _expectedConfig;
        private static readonly List<string> Warnings = new List<string>();

        /// <summary>Ошибка последней проверки; пусто — не было.</summary>
        public static string LastError { get; private set; } = string.Empty;

        /// <summary>Что нашлось в манифесте.</summary>
        public readonly struct ManifestFacts
        {
            public readonly bool HasVrCategory;

            /// <summary>Значение <see cref="SupportedDevicesKey" />; null — ключа нет.</summary>
            public readonly string SupportedDevices;

            /// <summary>
            /// <c>uses-feature</c> <see cref="QuestControllerFreeManifest.HandTrackingFeature" />: null — нет,
            /// false — необязательные руки, true — хотя бы одна запись с обязательными руками.
            /// </summary>
            public readonly bool? HandTrackingRequired;

            /// <summary>Есть ли <c>uses-permission</c> <see cref="QuestControllerFreeManifest.HandTrackingPermission" />.</summary>
            public readonly bool HasHandTrackingPermission;

            public ManifestFacts(bool hasVrCategory, string supportedDevices,
                                 bool? handTrackingRequired = null, bool hasHandTrackingPermission = false)
            {
                HasVrCategory = hasVrCategory;
                SupportedDevices = supportedDevices;
                HandTrackingRequired = handTrackingRequired;
                HasHandTrackingPermission = hasHandTrackingPermission;
            }

            /// <summary>Есть ли хоть одна запись рук.</summary>
            public bool HasAnyHandTracking => HandTrackingRequired != null || HasHandTrackingPermission;

            public override string ToString() =>
                $"VR-категория {(HasVrCategory ? "есть" : "нет")}, supportedDevices {SupportedDevices ?? "нет"}, " +
                $"руки: uses-feature {(HandTrackingRequired == null ? "нет" : HandTrackingRequired.Value ? "required=true" : "required=false")}, " +
                $"разрешение {(HasHandTrackingPermission ? "есть" : "нет")}";
        }

        /// <summary>После OculusManifest (10000) и QuestControllerFreeManifest (10001): проверяет итоговый манифест.</summary>
        public int callbackOrder => 10002;

        /// <summary>
        /// Скомпилированы ли editor-скрипты под Android: есть ли в домене <c>OculusManifest</c> (он под
        /// <c>#if UNITY_ANDROID</c>). Нет — Quest-сборка вышла бы без VR-категории, плоской.
        /// </summary>
        public static bool OculusManifestCompiled() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType(OculusManifestType, false) != null);

        /// <summary>Включает проверку на одну сборку профиля Android в конфигурации <paramref name="config" />.</summary>
        public static void Arm(BuildProfile profile, BuildConfig config)
        {
            if (profile == BuildProfile.Server) throw new ArgumentException("манифест есть только у Android-профилей", nameof(profile));

            _expected = profile;
            _expectedConfig = config;
            Warnings.Clear();
            LastError = string.Empty;
        }

        /// <summary>Выключает проверку; предупреждения остаются до <see cref="TakeWarnings" />.</summary>
        public static void Disarm() => _expected = null;

        /// <summary>Предупреждения последней проверки для <c>GameBuilder.Result.Lines</c>; буфер очищается.</summary>
        public static List<string> TakeWarnings()
        {
            var taken = new List<string>(Warnings);
            Warnings.Clear();
            return taken;
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (_expected == null) return;

            BuildProfile profile = _expected.Value;
            BuildConfig config = _expectedConfig;
            string manifestPath = Path.Combine(path, ManifestRelativePath);

            if (!File.Exists(manifestPath))
                Fail($"{profile}: нет манифеста {manifestPath} — проверка VR-категории невозможна");

            var document = new XmlDocument();
            document.Load(manifestPath);

            ManifestFacts facts = ReadXml(document);
            string error = Evaluate(profile, config, facts, Warnings);

            if (error != null)
            {
                Fail($"{profile} ({config}): {error} ({facts}). Манифест: {manifestPath}. " +
                     $"{XrBuildSettingsScope.Describe(BuildTargetGroup.Android)}; " +
                     $"OculusManifest в editor-сборке: {(OculusManifestCompiled() ? "есть" : "нет — редактор скомпилирован не под Android")}");
            }
        }

        /// <summary>
        /// Правила манифеста профиля. Возвращает ошибку или null; предупреждения дописывает в
        /// <paramref name="warnings" />. Quest: обязательны VR-категория и supportedDevices, без Quest 3 —
        /// предупреждение (список шлемов — зона performance); записи рук — по <paramref name="config" />
        /// (<see cref="EvaluateHandTracking" />). Планшет: VR-категории быть не должно.
        /// </summary>
        public static string Evaluate(BuildProfile profile, BuildConfig config, ManifestFacts facts, List<string> warnings)
        {
            switch (profile)
            {
                case BuildProfile.Quest:
                    if (!facts.HasVrCategory)
                        return $"в манифесте нет {VrCategory} — Oculus XR не увидел лоадер, шлем откроет приложение плоским окном";

                    if (facts.SupportedDevices == null)
                        return $"в манифесте нет {SupportedDevicesKey} — Oculus XR не дописал свои ключи";

                    string hands = EvaluateHandTracking(config, facts);
                    if (hands != null) return hands;

                    string[] devices = facts.SupportedDevices.Split('|').Select(d => d.Trim()).ToArray();
                    if (!devices.Any(d => Quest3Names.Contains(d, StringComparer.OrdinalIgnoreCase)))
                    {
                        string warning = $"{SupportedDevicesKey} = {facts.SupportedDevices}: Quest 3 не указан " +
                                         "(TargetQuest3 в Assets/XR/Settings/OculusSettings.asset = 0) — зона performance";
                        if (!warnings.Contains(warning)) warnings.Add(warning);
                    }

                    return null;

                case BuildProfile.Tablet:
                    return facts.HasVrCategory ? $"в манифесте планшета есть {VrCategory} — лоадер Oculus попал в сборку без XR" : null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Записи рук Quest (<see cref="QuestControllerFreeManifest" />). Test: обязательны необязательные руки
        /// (<c>required="false"</c>) и разрешение — иначе шлем при спящих контроллерах покажет диалог «нужен
        /// контроллер» и <c>am start</c> не запустит приложение. Prod: записей рук быть не должно.
        /// </summary>
        public static string EvaluateHandTracking(BuildConfig config, ManifestFacts facts)
        {
            string feature = QuestControllerFreeManifest.HandTrackingFeature;
            string permission = QuestControllerFreeManifest.HandTrackingPermission;

            if (config == BuildConfig.Prod)
            {
                return facts.HasAnyHandTracking
                    ? $"в прод-манифесте есть записи рук ({feature} / {permission}) — они только для Test-сборок " +
                      "(QuestControllerFreeManifest); их дописал чужой процессор или библиотека"
                    : null;
            }

            if (facts.HandTrackingRequired == null)
                return $"в Test-манифесте нет <uses-feature {feature} required=false> — QuestControllerFreeManifest не сработал, " +
                       "при спящих контроллерах шлем покажет диалог «нужен контроллер»";

            if (facts.HandTrackingRequired.Value)
                return $"в Test-манифесте {feature} объявлен обязательным (android:required не false) — руки стали бы " +
                       "обязательными для установки; это продуктовое решение";

            if (!facts.HasHandTrackingPermission)
                return $"в Test-манифесте нет <uses-permission {permission}> — QuestControllerFreeManifest не сработал";

            return null;
        }

        /// <summary>Факты из AndroidManifest.xml Gradle-проекта.</summary>
        public static ManifestFacts ReadXml(XmlDocument document)
        {
            const string AndroidNs = "http://schemas.android.com/apk/res/android";

            bool vr = document.SelectNodes("/manifest/application/activity/intent-filter/category")
                              ?.Cast<XmlElement>()
                              .Any(e => e.GetAttribute("name", AndroidNs) == VrCategory) ?? false;

            string devices = document.SelectNodes("/manifest/application/meta-data")
                                     ?.Cast<XmlElement>()
                                     .Where(e => e.GetAttribute("name", AndroidNs) == SupportedDevicesKey)
                                     .Select(e => e.GetAttribute("value", AndroidNs))
                                     .FirstOrDefault();

            return new ManifestFacts(vr, devices,
                                     QuestControllerFreeManifest.ReadHandTrackingRequired(document),
                                     QuestControllerFreeManifest.HasHandTrackingPermission(document));
        }

        /// <summary>
        /// Факты из вывода <c>aapt dump xmltree &lt;apk&gt; AndroidManifest.xml</c>: элементы — строки
        /// <c>E: имя</c>, атрибуты — <c>A: android:name(0x…)="значение" (Raw: …)</c>; булевы — без кавычек:
        /// <c>A: android:required(0x…)=(type 0x12)0x0</c> (false) или <c>…0xffffffff</c> (true).
        /// </summary>
        public static ManifestFacts ReadAaptXmlTree(string xmlTree)
        {
            var attribute = new Regex("^\\s*A: android:(name|value)\\([^)]*\\)=\"([^\"]*)\"");
            var requiredAttribute = new Regex("^\\s*A: android:required\\([^)]*\\)=(.*)$");
            bool vr = false;
            string devices = null;
            string element = null;
            string name = null;

            bool? handRequired = null;
            bool handPermission = false;
            // required текущего uses-feature; без атрибута по умолчанию true (правило Android).
            bool featureRequired = true;

            void CommitFeature()
            {
                if (element == "uses-feature" && name == QuestControllerFreeManifest.HandTrackingFeature)
                    handRequired = handRequired == true || featureRequired;
            }

            foreach (string line in xmlTree.Split('\n'))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("E: ", StringComparison.Ordinal))
                {
                    CommitFeature();
                    element = trimmed.Substring(3).Split(' ')[0];
                    name = null;
                    featureRequired = true;
                    continue;
                }

                Match required = requiredAttribute.Match(line);
                if (required.Success)
                {
                    if (element == "uses-feature") featureRequired = !IsAaptFalse(required.Groups[1].Value);
                    continue;
                }

                Match match = attribute.Match(line);
                if (!match.Success) continue;

                if (match.Groups[1].Value == "name")
                {
                    name = match.Groups[2].Value;
                    if (element == "category" && name == VrCategory) vr = true;
                    if (element == "uses-permission" && name == QuestControllerFreeManifest.HandTrackingPermission) handPermission = true;
                }
                else if (element == "meta-data" && name == SupportedDevicesKey && devices == null)
                {
                    devices = match.Groups[2].Value;
                }
            }

            CommitFeature();
            return new ManifestFacts(vr, devices, handRequired, handPermission);
        }

        /// <summary>Булев атрибут aapt равен false: <c>(type 0x12)0x0</c> или строка <c>"false"</c>.</summary>
        private static bool IsAaptFalse(string value)
        {
            string trimmed = value.Trim();
            return trimmed.EndsWith(")0x0", StringComparison.Ordinal) ||
                   trimmed.StartsWith("\"false\"", StringComparison.OrdinalIgnoreCase);
        }

        private static void Fail(string message)
        {
            LastError = "проверка манифеста после генерации Gradle: " + message;
            throw new BuildFailedException(GameBuilder.ResultMarker + " " + LastError);
        }
    }
}
