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
    /// Этот процессор идёт следом (10001) и роняет сборку с диагностикой настроек XR.
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
    /// Работает только во время сборки GameBuilder: профиль взводит <see cref="Arm" /> вокруг
    /// <c>BuildPlayer</c> и снимает <see cref="Disarm" />. Ручные сборки и чужие пайплайны не трогает.
    /// Те же правила (<see cref="Evaluate" />) GameBuilder применяет к готовому APK через aapt.
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
        private static readonly List<string> Warnings = new List<string>();

        /// <summary>Ошибка последней проверки; пусто — не было.</summary>
        public static string LastError { get; private set; } = string.Empty;

        /// <summary>Что нашлось в манифесте.</summary>
        public readonly struct ManifestFacts
        {
            public readonly bool HasVrCategory;

            /// <summary>Значение <see cref="SupportedDevicesKey" />; null — ключа нет.</summary>
            public readonly string SupportedDevices;

            public ManifestFacts(bool hasVrCategory, string supportedDevices)
            {
                HasVrCategory = hasVrCategory;
                SupportedDevices = supportedDevices;
            }

            public override string ToString() =>
                $"VR-категория {(HasVrCategory ? "есть" : "нет")}, supportedDevices {SupportedDevices ?? "нет"}";
        }

        public int callbackOrder => 10001;

        /// <summary>
        /// Скомпилированы ли editor-скрипты под Android: есть ли в домене <c>OculusManifest</c> (он под
        /// <c>#if UNITY_ANDROID</c>). Нет — Quest-сборка вышла бы без VR-категории, плоской.
        /// </summary>
        public static bool OculusManifestCompiled() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType(OculusManifestType, false) != null);

        /// <summary>Включает проверку на одну сборку профиля Android.</summary>
        public static void Arm(BuildProfile profile)
        {
            if (profile == BuildProfile.Server) throw new ArgumentException("манифест есть только у Android-профилей", nameof(profile));

            _expected = profile;
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
            string manifestPath = Path.Combine(path, ManifestRelativePath);

            if (!File.Exists(manifestPath))
                Fail($"{profile}: нет манифеста {manifestPath} — проверка VR-категории невозможна");

            var document = new XmlDocument();
            document.Load(manifestPath);

            ManifestFacts facts = ReadXml(document);
            string error = Evaluate(profile, facts, Warnings);

            if (error != null)
            {
                Fail($"{profile}: {error} ({facts}). Манифест: {manifestPath}. " +
                     $"{XrBuildSettingsScope.Describe(BuildTargetGroup.Android)}; " +
                     $"OculusManifest в editor-сборке: {(OculusManifestCompiled() ? "есть" : "нет — редактор скомпилирован не под Android")}");
            }
        }

        /// <summary>
        /// Правила манифеста профиля. Возвращает ошибку или null; предупреждения дописывает в
        /// <paramref name="warnings" />. Quest: обязательны VR-категория и supportedDevices, без Quest 3 —
        /// предупреждение (список шлемов — зона performance). Планшет: VR-категории быть не должно.
        /// </summary>
        public static string Evaluate(BuildProfile profile, ManifestFacts facts, List<string> warnings)
        {
            switch (profile)
            {
                case BuildProfile.Quest:
                    if (!facts.HasVrCategory)
                        return $"в манифесте нет {VrCategory} — Oculus XR не увидел лоадер, шлем откроет приложение плоским окном";

                    if (facts.SupportedDevices == null)
                        return $"в манифесте нет {SupportedDevicesKey} — Oculus XR не дописал свои ключи";

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

            return new ManifestFacts(vr, devices);
        }

        /// <summary>
        /// Факты из вывода <c>aapt dump xmltree &lt;apk&gt; AndroidManifest.xml</c>: элементы — строки
        /// <c>E: имя</c>, атрибуты — <c>A: android:name(0x…)="значение" (Raw: …)</c>.
        /// </summary>
        public static ManifestFacts ReadAaptXmlTree(string xmlTree)
        {
            var attribute = new Regex("^\\s*A: android:(name|value)\\([^)]*\\)=\"([^\"]*)\"");
            bool vr = false;
            string devices = null;
            string element = null;
            string name = null;

            foreach (string line in xmlTree.Split('\n'))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("E: ", StringComparison.Ordinal))
                {
                    element = trimmed.Substring(3).Split(' ')[0];
                    name = null;
                    continue;
                }

                Match match = attribute.Match(line);
                if (!match.Success) continue;

                if (match.Groups[1].Value == "name")
                {
                    name = match.Groups[2].Value;
                    if (element == "category" && name == VrCategory) vr = true;
                }
                else if (element == "meta-data" && name == SupportedDevicesKey && devices == null)
                {
                    devices = match.Groups[2].Value;
                }
            }

            return new ManifestFacts(vr, devices);
        }

        private static void Fail(string message)
        {
            LastError = "проверка манифеста после генерации Gradle: " + message;
            throw new BuildFailedException(GameBuilder.ResultMarker + " " + LastError);
        }
    }
}
