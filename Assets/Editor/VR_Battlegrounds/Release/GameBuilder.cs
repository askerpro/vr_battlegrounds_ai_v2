using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.EditorTools.VersionControl;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>Что собираем.</summary>
    public enum BuildProfile
    {
        /// <summary>Windows Dedicated Server: headless, без XR.</summary>
        Server,

        /// <summary>Oculus Quest: Android с лоадером Oculus.</summary>
        Quest,

        /// <summary>Android-планшет админа: без XR, <c>BUILD_ROLE_ADMIN</c>.</summary>
        Tablet
    }

    /// <summary>
    /// Сборка трёх клиентов сессии — выделенного сервера, Quest и планшета — из одного и того же
    /// состояния сцен и префабов.
    ///
    /// <para>
    /// <b>Главное — одинаковые id UltimateXR.</b> Перед сборкой флаги UXR-префабов приводятся
    /// к верным (<see cref="UxrUniqueIdPersister" />), затем снимается отпечаток id
    /// (<see cref="UxrIdFingerprint" />). Он сверяется перед и после каждой сборки: переключение
    /// платформы реимпортирует ассеты, и если бы это тронуло id, сборка упала бы, а не уехала
    /// на устройство с чужими id. Отпечаток пишется рядом со сборкой (<c>uxr-ids.txt</c>);
    /// сборки с разными отпечатками между собой не работают.
    /// </para>
    ///
    /// <para>
    /// Профиль меняет настройки только на время сборки и возвращает их: XR-лоадеры
    /// (<see cref="XrBuildSettingsScope" />), идентификатор пакета планшета, min SDK Quest.
    /// Роль планшета включается <c>extraScriptingDefines</c>, без правки Player Settings.
    /// </para>
    ///
    /// <para>
    /// Манифест Android проверяется дважды: после генерации Gradle (<see cref="QuestManifestValidator" />)
    /// и по готовому APK через aapt. Quest без <c>com.oculus.intent.category.VR</c> или планшет с ней —
    /// сборка падает: такой Quest на шлеме открылся бы плоским окном.
    /// </para>
    ///
    /// <para>
    /// <b>Quest — только из редактора, скомпилированного под Android.</b> VR-категорию дописывает
    /// <c>OculusManifest</c> под <c>#if UNITY_ANDROID</c>; переключение платформы внутри сборки editor-скрипты
    /// не перекомпилирует. Поэтому до любой сборки проверяется
    /// <see cref="QuestManifestValidator.OculusManifestCompiled" />: нет типа — Quest не собирается вовсе.
    /// CLI запускает Unity с <c>-buildTarget Android</c> для Android-профилей (Tools/release/Build-Game.ps1).
    /// </para>
    ///
    /// <para>
    /// Тестовая сборка получает штамп ревизии исходников в версии приложения
    /// (<see cref="BuildRevisionStamp" />, versionName <c>0.1.0+&lt;ревизия&gt;</c>); прод — нет.
    /// Перед Android-сборкой модальный диалог «Unsupported Input Handling on Android» подавляется
    /// на текущую сессию редактора ответом «Ignore»: иначе сборка агента в редакторе висит до человека.
    /// </para>
    ///
    /// Вызов:
    ///   — из редактора: Tools/VR Battlegrounds/Release/…
    ///   — агентом:      GameBuilder.Build(new[] { BuildProfile.Server }, BuildConfig.Test)
    ///   — из CLI:       Tools/release/Build-Game.ps1 (только при закрытом редакторе)
    /// </summary>
    public static class GameBuilder
    {
        /// <summary>Маркер в консоли, по которому агент находит результат.</summary>
        public const string ResultMarker = "[GameBuilder]";

        /// <summary>Корень сборок, в <c>.gitignore</c>.</summary>
        public const string OutputRoot = "Build";

        private const string MenuRoot = "Tools/VR Battlegrounds/Release/";
        private const string TestMenu = MenuRoot + "Конфигурация: тест на шлеме";
        private const string ProdMenu = MenuRoot + "Конфигурация: прод";
        private const string ConfigPref = "VrBattlegrounds.GameBuilder.Config";

        /// <summary>Роль планшета: <c>LocalClientProfile</c> читает её при компиляции.</summary>
        private const string AdminDefine = "BUILD_ROLE_ADMIN";

        /// <summary>Без них UltimateXR не подключает Mirror: аватары не выравнивают id по netId.</summary>
        private static readonly string[] RequiredDefines = { "MIRROR", "ULTIMATEXR_USE_MIRROR_SDK" };

        /// <summary>
        /// Ключ opt-out диалога «Unsupported Input Handling on Android» (кнопки «Ignore» / «Cancel Build»)
        /// из UnityEditor.Android.Extensions, CheckPrerequisites.ValidateInputHandling.
        /// </summary>
        private const string InputHandlingDialogKey = "AndroidInputHandlingWarning";

        /// <summary>
        /// Префикс, под которым EditorDialog (Unity 6000.4) хранит opt-out: SessionState.GetInt(префикс + ключ),
        /// значение — DialogResult. Публичный EditorUtility.SetDialogOptOutDecision пишет SessionState.SetBool
        /// без префикса, и этот диалог его не читает (проверено по IL UnityEditor.CoreModule 6000.4.1f1).
        /// </summary>
        private const string DialogOptOutPrefix = "DialogOptOut.";

        /// <summary><c>PlayerSettings.activeInputHandler</c>: 0 — старый Input Manager, 1 — Input System, 2 — оба.</summary>
        private const int ActiveInputHandlingBoth = 2;

        /// <summary>Сколько ждать aapt при проверке манифеста APK.</summary>
        private const int ToolTimeoutMs = 120000;

        /// <summary>Итог сборки одного или нескольких профилей.</summary>
        public sealed class Result
        {
            public bool Passed = true;
            public string Fingerprint = string.Empty;

            /// <summary>Штамп ревизии исходников; пусто — сборка без штампа (прод).</summary>
            public string Revision = string.Empty;

            /// <summary>Корень checkout, из которого выполнялась сборка.</summary>
            public string CheckoutPath = string.Empty;

            public readonly List<string> Lines = new List<string>();

            public string Summary =>
                ResultMarker + (Passed ? " PASS" : " FAIL") + (Fingerprint.Length > 0 ? $" — UXR id {Fingerprint}" : "") +
                (Revision.Length > 0 ? $" — ревизия {Revision}" : "") +
                (CheckoutPath.Length > 0 ? $" — checkout {CheckoutPath}" : "") +
                "\n" + string.Join("\n", Lines);

            public Result Fail(string message)
            {
                Passed = false;
                Lines.Add("ОШИБКА: " + message);
                return this;
            }
        }

        private sealed class Spec
        {
            public BuildTarget Target;
            public BuildTargetGroup Group;
            public NamedBuildTarget Named;
            public StandaloneBuildSubtarget Subtarget = StandaloneBuildSubtarget.Player;
            public string Path;
        }

        // ── Меню ───────────────────────────────────────────────────────

        [MenuItem(MenuRoot + "Собрать всё (сервер, Quest, планшет)", priority = 0)]
        private static void BuildAllMenu() => RunFromMenu(BuildProfile.Server, BuildProfile.Quest, BuildProfile.Tablet);

        [MenuItem(MenuRoot + "Собрать Dedicated Server (Windows)", priority = 20)]
        private static void BuildServerMenu() => RunFromMenu(BuildProfile.Server);

        [MenuItem(MenuRoot + "Собрать Quest", priority = 21)]
        private static void BuildQuestMenu() => RunFromMenu(BuildProfile.Quest);

        [MenuItem(MenuRoot + "Собрать Android-планшет", priority = 22)]
        private static void BuildTabletMenu() => RunFromMenu(BuildProfile.Tablet);

        [MenuItem(MenuRoot + "Проверить UXR id (без сборки)", priority = 40)]
        private static void CheckMenu() => Log(Check());

        [MenuItem(TestMenu, priority = 60)]
        private static void SelectTest() => EditorPrefs.SetInt(ConfigPref, (int)BuildConfig.Test);

        [MenuItem(ProdMenu, priority = 61)]
        private static void SelectProd() => EditorPrefs.SetInt(ConfigPref, (int)BuildConfig.Prod);

        [MenuItem(TestMenu, true)]
        private static bool SelectTestValidate()
        {
            Menu.SetChecked(TestMenu, SelectedConfig == BuildConfig.Test);
            Menu.SetChecked(ProdMenu, SelectedConfig == BuildConfig.Prod);
            return true;
        }

        /// <summary>Конфигурация из меню, по умолчанию тест.</summary>
        private static BuildConfig SelectedConfig => (BuildConfig)EditorPrefs.GetInt(ConfigPref, (int)BuildConfig.Test);

        private static void RunFromMenu(params BuildProfile[] profiles)
        {
            Result result = Build(profiles, SelectedConfig);
            Log(result);

            EditorUtility.DisplayDialog("Сборка VR Battlegrounds",
                                        (result.Passed ? "Готово." : "Сборка не удалась.") + "\n\nПодробности в консоли.",
                                        "OK");
        }

        // ── Точки входа ────────────────────────────────────────────────

        /// <summary>
        /// Предполётная проверка без сборки: флаги UXR-префабов, память против диска, нулевые id.
        /// </summary>
        public static Result Check()
        {
            var result = new Result();
            Preflight(EnabledScenes(), needsAndroid: false, result);
            return result;
        }

        /// <summary>Собирает профили в <c>Build/&lt;профиль&gt;/</c>. Останавливается на первой ошибке.</summary>
        public static Result Build(IEnumerable<BuildProfile> profiles, BuildConfig config) =>
            Build(profiles, config, null);

        /// <summary>
        /// То же с заданной ревизией штампа: пара e2e-плеер + APK штампуется одной ревизией, посчитанной
        /// один раз (<see cref="BuildRevisionStamp.ReadRevision" />). <c>null</c> — посчитать здесь.
        /// Для <see cref="BuildConfig.Prod" /> штампа нет, ревизия игнорируется.
        /// </summary>
        public static Result Build(IEnumerable<BuildProfile> profiles, BuildConfig config, string revision) =>
            Build(profiles, config, revision, null);

        /// <summary>
        /// То же с дополнительными корневыми папками <c>Build/</c> других сборочных worktree для кросс-сверки <c>uxr-ids.txt</c>.
        /// </summary>
        public static Result Build(
            IEnumerable<BuildProfile> profiles,
            BuildConfig config,
            string revision,
            IEnumerable<string> externalBuildRoots)
        {
            var result = new Result
            {
                CheckoutPath = Path.GetFullPath(".")
            };

            if (config == BuildConfig.Prod &&
                result.CheckoutPath.IndexOf("unity-editor-worker", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Lines.Add(
                    "ВНИМАНИЕ: Prod-сборка запущена в рабочем checkout воркера (unity-editor-worker) с общей dev-Library; " +
                    "для релизных сборок используй выделенные сборочные worktree build-prod-win64 и build-prod-android.");
            }

            List<BuildProfile> ordered = OrderBySwitches(profiles.Distinct());

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return result.Fail("выйди из Play Mode");

            string[] scenes = EnabledScenes();
            bool needsAndroid = ordered.Any(p => p != BuildProfile.Server);

            // Набор editor-типов задан компиляцией домена и в этом вызове не меняется (переключение платформы
            // не перекомпилирует), поэтому проверка — до всех сборок, а не перед Quest после сервера.
            // Любая Android-сборка — только из редактора, скомпилированного под Android: обработчики манифеста под
            // #if UNITY_ANDROID иначе не существуют. Quest: OculusManifest (VR-категория — без неё плоское окно).
            // Tablet: Mirror AndroidManifestHelper (CHANGE_WIFI_MULTICAST_STATE — без него LAN-discovery не найдёт сервер).
            if (needsAndroid && !QuestManifestValidator.OculusManifestCompiled())
            {
                string profilesText = string.Join(", ", ordered.Where(p => p != BuildProfile.Server));
                return result.Fail(profilesText + ": editor-скрипты скомпилированы не под Android (нет UNITY_ANDROID): " +
                                   "обработчики манифеста не сработают — Quest выйдет плоским окном (нет OculusManifest), " +
                                   "планшет без разрешения multicast для LAN-discovery (Mirror AndroidManifestHelper). " +
                                   "Переключи активную платформу на Android, дождись компиляции и повтори; batch — Unity с -buildTarget Android");
            }

            UxrIdFingerprint baseline = Preflight(scenes, needsAndroid, result);
            if (!result.Passed) return result;

            if (needsAndroid) SuppressAndroidInputHandlingDialog(result);

            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup originalGroup = BuildPipeline.GetBuildTargetGroup(originalTarget);
            StandaloneBuildSubtarget originalSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

            // Штамп на все профили запуска сразу; Dispose возвращает версию и сохраняет настройки
            // до общей сверки id после сохранения ниже. Прод — без штампа: versionName магазина не трогаем.
            using (BuildRevisionStamp stamp = config == BuildConfig.Test ? StampFor(revision, result) : null)
            {
                try
                {
                    foreach (BuildProfile profile in ordered)
                    {
                        if (!BuildOne(profile, scenes, baseline, config, result)) break;
                    }
                }
                finally
                {
                    // Редактор оставляем на той платформе, на которой он был: Play Mode с активным
                    // Dedicated Server или Android — не то, чего ждёт следующий, кто нажмёт Play.
                    // В batch-режиме полное переключение (реимпорт) незачем, но подцель Standalone
                    // возвращаем: она хранится в Library, и редактор, открытый после сборки сервера,
                    // оказывался на Dedicated Server.
                    if (Application.isBatchMode) EditorUserBuildSettings.standaloneBuildSubtarget = originalSubtarget;

                    if (!Application.isBatchMode &&
                        (EditorUserBuildSettings.activeBuildTarget != originalTarget ||
                         EditorUserBuildSettings.standaloneBuildSubtarget != originalSubtarget))
                    {
                        EditorUserBuildSettings.standaloneBuildSubtarget = originalSubtarget;
                        EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalTarget);
                    }
                }
            }

            // Unity пишет Player Settings на диск в начале каждой сборки — со значениями профиля
            // (min SDK Quest, IL2CPP тест/прод, XR в preloadedAssets). Возвращены они только в
            // памяти; без сохранения на диске остаётся снимок середины сборки, и его прочтёт
            // следующая batch-сборка (планшет получил бы min SDK Quest). Сохранение пишет всё
            // грязное, поэтому id UXR сверяются ещё раз.
            AssetDatabase.SaveAssets();
            string afterSave = UxrIdFingerprint.Compute(scenes).Hash;
            if (afterSave != baseline.Hash)
                result.Fail($"UXR id на диске изменились при сохранении после сборки ({baseline.Hash} → {afterSave}) — " +
                            "сборки собраны со старыми id, пересобери всё.");

            if (result.Passed) CompareWithOtherBuilds(ordered, baseline, result, externalBuildRoots);
            return result;
        }

        /// <summary>
        /// Точка входа для -executeMethod. Аргументы: <c>-buildProfile all|server,quest,tablet</c>,
        /// <c>-config test|prod</c> (<c>-development</c> — то же, что test),
        /// <c>-buildRevision &lt;ревизия&gt;</c>, <c>-compareBuildRoot &lt;путь&gt;</c>. Код возврата 0 или 1.
        /// </summary>
        public static void RunBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string profileArg = "all";
            BuildConfig config = BuildConfig.Test;
            string revisionArg = null;
            var compareRoots = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-buildProfile" && i + 1 < args.Length) profileArg = args[i + 1];
                if (args[i] == "-development") config = BuildConfig.Test;
                if (args[i] == "-buildRevision" && i + 1 < args.Length) revisionArg = args[i + 1];
                if (args[i] == "-compareBuildRoot" && i + 1 < args.Length) compareRoots.Add(args[i + 1]);
                if (args[i] == "-config" && i + 1 < args.Length &&
                    !Enum.TryParse(args[i + 1], true, out config))
                {
                    Debug.LogError($"{ResultMarker} FAIL — неизвестная конфигурация '{args[i + 1]}'. Есть: test, prod.");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            var profiles = new List<BuildProfile>();

            foreach (string name in profileArg.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (name.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                    profiles.AddRange((BuildProfile[])Enum.GetValues(typeof(BuildProfile)));
                else if (Enum.TryParse(name.Trim(), true, out BuildProfile p))
                    profiles.Add(p);
                else
                {
                    Debug.LogError($"{ResultMarker} FAIL — неизвестный профиль '{name}'. Есть: all, server, quest, tablet.");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            Result result = Build(profiles, config, revisionArg, compareRoots);
            Log(result);
            EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        // ── Предполётная проверка ──────────────────────────────────────

        private static UxrIdFingerprint Preflight(string[] scenes, bool needsAndroid, Result result)
        {
            if (scenes.Length == 0)
            {
                result.Fail("в Build Settings нет включённых сцен");
                return null;
            }

            // Сборка берёт сцену с диска, редактор — из памяти. Несохранённая сцена — это
            // расхождение редактора со сборками: хост в редакторе не сойдётся с Quest.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty && scenes.Contains(scene.path))
                    result.Fail($"сцена {scene.path} открыта и не сохранена — сохрани или откати её");
            }

            if (!result.Passed) return null;

            // Создаёт настройки XR Android и сохраняет их через SaveAssets — поэтому до отпечатка.
            if (needsAndroid && XrBuildSettingsScope.EnsureAndroidSettings())
                result.Lines.Add("Созданы настройки XR для Android (Assets/XR/XRGeneralSettingsPerBuildTarget.asset).");

            string normalized = UxrUniqueIdPersister.NormalizeAll();
            if (!normalized.Contains("исправлено: 0")) result.Lines.Add("Флаги UXR-префабов: " + normalized.Trim());

            UxrIdFingerprint fingerprint = UxrIdFingerprint.Compute(scenes);

            foreach (string path in fingerprint.ZeroIds)
                result.Fail($"{path}: UXR-компонент с нулевым id — открой и сохрани, редактор выдаст id");

            foreach (string problem in fingerprint.FindMemoryMismatches())
                result.Fail(problem + " — сохрани префаб (для Assets/Prefabs: Tools/VR Battlegrounds/VersionControl/Persist UltimateXR Unique Ids)");

            result.Fingerprint = fingerprint.Hash;
            result.Lines.Add($"UXR id: {fingerprint.Summary}");
            return fingerprint;
        }

        // ── Штамп ревизии и диалог ввода Android ───────────────────────

        private static BuildRevisionStamp StampFor(string revision, Result result)
        {
            if (revision == null)
            {
                revision = BuildRevisionStamp.ReadRevision(out string warning);
                if (warning != null) result.Lines.Add("ВНИМАНИЕ: " + warning);
            }

            var stamp = new BuildRevisionStamp(revision);
            result.Revision = stamp.Revision;
            result.Lines.Add($"Ревизия сборки: {stamp.Revision} — версия на время сборки {stamp.StampedVersion}");
            return stamp;
        }

        /// <summary>
        /// Отвечает за человека «Ignore» на модальный «Unsupported Input Handling on Android» на текущую
        /// сессию редактора (SessionState, не EditorPrefs): без этого сборка агента в открытом редакторе
        /// висит до клика. В batch-режиме Unity диалог не показывает, только пишет предупреждение.
        /// </summary>
        private static void SuppressAndroidInputHandlingDialog(Result result)
        {
            // DialogResult.DefaultAction — кнопка ok (здесь «Ignore»): EditorDialog вернёт true и сборка
            // продолжится. Любое другое значение означало бы «Cancel Build».
            SessionState.SetInt(DialogOptOutPrefix + InputHandlingDialogKey, (int)DialogResult.DefaultAction);

            int? handler = ReadActiveInputHandler();

            if (handler == ActiveInputHandlingBoth)
            {
                result.Lines.Add("ВНИМАНИЕ: Active Input Handling = Both: Unity предупреждает о проблемах ввода и " +
                                 "производительности на Android; диалог подавлен автоматически, корень — backlog input-handling-new-only.");
            }
            else if (handler == null)
            {
                result.Lines.Add("ВНИМАНИЕ: не удалось прочитать Active Input Handling; диалог ввода Android подавлен на сессию.");
            }
        }

        /// <summary>Читает <c>activeInputHandler</c> из Player Settings без изменения; null — не прочитался.</summary>
        private static int? ReadActiveInputHandler()
        {
            try
            {
                UnityEngine.Object settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
                if (settings == null) return null;

                using (var serialized = new SerializedObject(settings))
                {
                    SerializedProperty property = serialized.FindProperty("activeInputHandler");
                    return property?.intValue;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ── Одна сборка ────────────────────────────────────────────────

        private static bool BuildOne(BuildProfile profile, string[] scenes, UxrIdFingerprint baseline, BuildConfig config, Result result)
        {
            Spec spec = SpecFor(profile);

            string androidId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            AndroidSdkVersions minSdk = PlayerSettings.Android.minSdkVersion;

            try
            {
                {
                    if (profile == BuildProfile.Tablet)
                    {
                        // Своё имя пакета: Quest-клиент и пульт админа ставятся рядом, не затирая друг друга.
                        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, androidId + ".tablet");
                    }

                    if (profile == BuildProfile.Quest && minSdk < AndroidSdkVersions.AndroidApiLevel29)
                    {
                        // Oculus XR Plugin в Unity 6 не собирается ниже 29; у Quest 2/3 — Android 12 (32).
                        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
                    }

                    string[] extraDefines = ExtraDefines(profile, spec, result);
                    if (!result.Passed) return false;

                    if (spec.Group == BuildTargetGroup.Android)
                    {
                        // Сжатие текстур выбирается до переключения платформы, чтобы импорт под Android
                        // прошёл один раз и сразу в ASTC. BuildPlayer запоминает subtarget сборки в этой же
                        // настройке — так первая версия сборщика оставила здесь PVRTC.
                        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
                    }

                    if (EditorUserBuildSettings.activeBuildTarget != spec.Target ||
                        (spec.Group == BuildTargetGroup.Standalone && EditorUserBuildSettings.standaloneBuildSubtarget != spec.Subtarget))
                    {
                        EditorUserBuildSettings.standaloneBuildSubtarget = spec.Subtarget;

                        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(spec.Group, spec.Target))
                        {
                            result.Fail($"{profile}: не удалось переключиться на {spec.Target} — модуль платформы установлен?");
                            return false;
                        }
                    }

                    if (!SameIds(profile, "после переключения платформы", scenes, baseline, result)) return false;

                    string directory = System.IO.Path.GetDirectoryName(spec.Path);
                    Directory.CreateDirectory(directory);

                    var options = new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = spec.Path,
                        target = spec.Target,
                        targetGroup = spec.Group,
                        // Для Android subtarget — формат сжатия текстур (MobileTextureSubtarget), а не
                        // StandaloneBuildSubtarget: Player (2) там значит PVRTC, и текстуры уходили RGBA32.
                        subtarget = spec.Group == BuildTargetGroup.Android ? (int)MobileTextureSubtarget.ASTC : (int)spec.Subtarget,
                        extraScriptingDefines = extraDefines,
                    };

                    DateTime started = DateTime.UtcNow;
                    BuildReport report;

                    // XR выставляется только после переключения платформы: переключение реимпортирует
                    // XRGeneralSettingsPerBuildTarget.asset с диска, и лоадеры, заданные в памяти раньше,
                    // терялись — Quest собирался без Oculus, без VR-категории в манифесте, чёрный экран.
                    bool android = spec.Group == BuildTargetGroup.Android;

                    using (var buildConfig = new BuildConfigScope(spec.Named, config))
                    using (new XrBuildSettingsScope(profile))
                    {
                        options.options = buildConfig.Options;

                        if (profile == BuildProfile.Quest && !XrBuildSettingsScope.HasOculusLoader(BuildTargetGroup.Android))
                        {
                            result.Fail($"{profile}: перед сборкой у Android нет лоадера Oculus — приложение не станет VR");
                            return false;
                        }

                        // Манифест проверяется сразу после генерации Gradle (QuestManifestValidator) —
                        // только в этой сборке и только для Android-профиля.
                        if (android) QuestManifestValidator.Arm(profile, config);
                        if (android) QuestControllerFreeManifest.Arm(profile, config);

                        try
                        {
                            report = BuildPipeline.BuildPlayer(options);
                        }
                        finally
                        {
                            if (android) QuestManifestValidator.Disarm();
                            if (android) QuestControllerFreeManifest.Disarm();
                        }
                    }

                    double seconds = (DateTime.UtcNow - started).TotalSeconds;

                    if (android)
                    {
                        foreach (string warning in QuestManifestValidator.TakeWarnings())
                            result.Lines.Add($"ВНИМАНИЕ: {profile}: {warning}");
                    }

                    if (report != null) result.Lines.Add(HeaviestAssets(profile, report));

                    if (report == null || report.summary.result != BuildResult.Succeeded)
                    {
                        if (android && QuestManifestValidator.LastError.Length > 0)
                            result.Fail($"{profile}: {QuestManifestValidator.LastError}");

                        result.Fail($"{profile}: сборка {report?.summary.result.ToString() ?? "без отчёта"}, " +
                                    $"ошибок: {report?.summary.totalErrors ?? 0} — подробности выше в консоли");
                        return false;
                    }

                    if (!SameIds(profile, "после сборки", scenes, baseline, result)) return false;
                    if (android && !CheckApkManifest(profile, config, spec.Path, result)) return false;

                    baseline.WriteTo(directory, profile);
                    if (profile == BuildProfile.Server) CopyServerLauncher(directory);
                    result.Lines.Add($"{profile}: {spec.Path} — {seconds:F0} с, {report.summary.totalSize / (1024 * 1024)} МБ" +
                                     ", " + BuildConfigScope.Describe(config));
                    return true;
                }
            }
            catch (Exception e)
            {
                result.Fail($"{profile}: {e.GetType().Name}: {e.Message}");
                return false;
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, androidId);
                PlayerSettings.Android.minSdkVersion = minSdk;
            }
        }

        /// <summary>
        /// Самые тяжёлые ассеты сборки по <c>BuildReport.packedAssets</c>. Отчёт заполняется при записи
        /// данных, то есть и тогда, когда сборка упала позже, в Gradle.
        /// </summary>
        private static string HeaviestAssets(BuildProfile profile, BuildReport report)
        {
            var bySource = new Dictionary<string, ulong>();
            var typeOf = new Dictionary<string, string>();
            ulong total = 0;

            foreach (PackedAssets packed in report.packedAssets)
            {
                foreach (PackedAssetInfo info in packed.contents)
                {
                    string key = string.IsNullOrEmpty(info.sourceAssetPath) ? "(" + info.type?.Name + ")" : info.sourceAssetPath;
                    bySource.TryGetValue(key, out ulong size);
                    bySource[key] = size + info.packedSize;
                    typeOf[key] = info.type?.Name;
                    total += info.packedSize;
                }
            }

            var byType = new Dictionary<string, ulong>();
            foreach (KeyValuePair<string, ulong> pair in bySource)
            {
                string type = typeOf[pair.Key] ?? "?";
                byType.TryGetValue(type, out ulong size);
                byType[type] = size + pair.Value;
            }

            string Mb(ulong bytes) => (bytes / (1024.0 * 1024.0)).ToString("F1") + " МБ";

            return $"{profile}: данные {Mb(total)}. По типам: " +
                   string.Join(", ", byType.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key} {Mb(p.Value)}")) +
                   "\n  Тяжелее всего:\n" +
                   string.Join("\n", bySource.OrderByDescending(p => p.Value).Take(25).Select(p => $"  {Mb(p.Value),10}  {typeOf[p.Key]}  {p.Key}"));
        }

        // ── Манифест готового APK ──────────────────────────────────────

        /// <summary>
        /// Вторая проверка манифеста — по готовому APK через <c>aapt dump xmltree</c>, по тем же правилам
        /// (<see cref="QuestManifestValidator.Evaluate" />): между генерацией Gradle и APK манифест ещё
        /// сливается с библиотеками. Нет aapt или он не отработал — предупреждение: проверка после
        /// генерации Gradle уже прошла.
        /// </summary>
        private static bool CheckApkManifest(BuildProfile profile, BuildConfig config, string apkPath, Result result)
        {
            string aapt = FindAapt(out string searched);

            if (aapt == null)
            {
                result.Lines.Add($"ВНИМАНИЕ: {profile}: aapt не найден (build-tools в {searched}) — манифест APK не проверен, " +
                                 "проверка после генерации Gradle прошла");
                return true;
            }

            if (!RunTool(aapt, $"dump xmltree \"{apkPath}\" AndroidManifest.xml", out string output, out string error))
            {
                result.Lines.Add($"ВНИМАНИЕ: {profile}: aapt не прочитал манифест APK ({error}) — проверка после генерации Gradle прошла");
                return true;
            }

            QuestManifestValidator.ManifestFacts facts = QuestManifestValidator.ReadAaptXmlTree(output);
            var warnings = new List<string>();
            string failure = QuestManifestValidator.Evaluate(profile, config, facts, warnings);

            foreach (string warning in warnings)
            {
                string line = $"ВНИМАНИЕ: {profile}: {warning}";
                if (!result.Lines.Contains(line)) result.Lines.Add(line);
            }

            if (failure != null)
            {
                result.Fail($"{profile}: манифест готового APK: {failure} ({facts}); aapt: {aapt}");
                return false;
            }

            result.Lines.Add($"{profile}: манифест APK — {facts} (aapt)");
            return true;
        }

        /// <summary>
        /// <c>aapt</c> старшей версии build-tools: сначала SDK из настроек Android редактора, потом
        /// встроенный в модуль Android. null — не найден; <paramref name="searched" /> — где искали.
        /// </summary>
        private static string FindAapt(out string searched)
        {
            var roots = new List<string>();
            string configured = AndroidSdkRoot();
            if (!string.IsNullOrEmpty(configured)) roots.Add(configured);
            roots.Add(Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK"));

            string exe = Application.platform == RuntimePlatform.WindowsEditor ? "aapt.exe" : "aapt";
            searched = string.Join("; ", roots);

            foreach (string root in roots)
            {
                string buildTools = Path.Combine(root, "build-tools");
                if (!Directory.Exists(buildTools)) continue;

                string best = Directory.GetDirectories(buildTools)
                                       .Select(dir => (dir, version: Version.TryParse(Path.GetFileName(dir), out Version v) ? v : null))
                                       .Where(x => x.version != null && File.Exists(Path.Combine(x.dir, exe)))
                                       .OrderByDescending(x => x.version)
                                       .Select(x => Path.Combine(x.dir, exe))
                                       .FirstOrDefault();

                if (best != null) return best;
            }

            return null;
        }

        /// <summary>
        /// <c>UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath</c> через отражение: прямая ссылка
        /// на UnityEditor.Android.Extensions не скомпилировалась бы в редакторе без модуля Android.
        /// </summary>
        private static string AndroidSdkRoot()
        {
            try
            {
                Type settings = AppDomain.CurrentDomain.GetAssemblies()
                                         .FirstOrDefault(a => a.GetName().Name == "UnityEditor.Android.Extensions")
                                         ?.GetType("UnityEditor.Android.AndroidExternalToolsSettings");

                return settings?.GetProperty("sdkRootPath", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool RunTool(string exe, string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;

            var info = new System.Diagnostics.ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
                {
                    if (process == null)
                    {
                        error = "процесс не запустился";
                        return false;
                    }

                    var stderr = process.StandardError.ReadToEndAsync();
                    output = process.StandardOutput.ReadToEnd();

                    if (!process.WaitForExit(ToolTimeoutMs))
                    {
                        try { process.Kill(); }
                        catch (InvalidOperationException) { }

                        error = $"не ответил за {ToolTimeoutMs / 1000} с";
                        return false;
                    }

                    error = stderr.Result.Trim();
                    if (process.ExitCode == 0) return true;

                    error = $"код {process.ExitCode}: {error}";
                    return false;
                }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Кладёт рядом с сервером лаунчер <c>Start-Server.cmd/.ps1</c>: запуск с <c>-logFile Logs\server-*.log</c>
        /// и показом лога в консоли.
        /// </summary>
        private static void CopyServerLauncher(string directory)
        {
            string source = Path.GetFullPath("Tools/release/server");
            if (!Directory.Exists(source)) return;

            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
        }

        private static bool SameIds(BuildProfile profile, string when, string[] scenes, UxrIdFingerprint baseline, Result result)
        {
            UxrIdFingerprint now = UxrIdFingerprint.Compute(scenes);

            if (now.Hash != baseline.Hash)
            {
                result.Fail($"{profile}: UXR id на диске изменились {when} ({baseline.Hash} → {now.Hash}). " +
                            "Сборки с разными id между собой не работают — выясни, кто пишет префабы, и пересобери всё.");
                return false;
            }

            List<string> mismatches = now.FindMemoryMismatches();

            foreach (string problem in mismatches)
                result.Fail($"{profile}: {when}: {problem}");

            return mismatches.Count == 0;
        }

        /// <summary>
        /// Define-символы сверх Player Settings цели. У Dedicated Server свой набор, в проекте
        /// пустой: без символов Standalone сервер собрался бы без интеграции Mirror в UltimateXR.
        /// </summary>
        private static string[] ExtraDefines(BuildProfile profile, Spec spec, Result result)
        {
            var own = new HashSet<string>(Split(PlayerSettings.GetScriptingDefineSymbols(spec.Named)));
            var extra = new List<string>();

            if (profile == BuildProfile.Server)
                extra.AddRange(Split(PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone)).Where(d => !own.Contains(d)));

            if (profile == BuildProfile.Tablet)
                extra.Add(AdminDefine);

            var effective = new HashSet<string>(own.Concat(extra));

            foreach (string required in RequiredDefines)
            {
                if (!effective.Contains(required))
                    result.Fail($"{profile}: нет define-символа {required} — UltimateXR соберётся без Mirror");
            }

            if (profile != BuildProfile.Tablet && effective.Contains(AdminDefine))
                result.Fail($"{profile}: {AdminDefine} стоит в Player Settings — сборка станет пультом админа");

            return extra.ToArray();
        }

        private static void CompareWithOtherBuilds(
            List<BuildProfile> built,
            UxrIdFingerprint baseline,
            Result result,
            IEnumerable<string> externalBuildRoots = null)
        {
            foreach (BuildProfile other in (BuildProfile[])Enum.GetValues(typeof(BuildProfile)))
            {
                if (built.Contains(other)) continue;

                string localDir = System.IO.Path.GetDirectoryName(SpecFor(other).Path);
                string hash = UxrIdFingerprint.ReadHash(localDir);
                if (hash != null && hash != baseline.Hash)
                {
                    result.Lines.Add($"ВНИМАНИЕ: {other} в {OutputRoot}/ собран с другими UXR id ({hash}) — " +
                                     "с новой сборкой по сети не сойдётся, пересобери его.");
                }

                if (externalBuildRoots == null) continue;
                foreach (string extRoot in externalBuildRoots)
                {
                    if (string.IsNullOrWhiteSpace(extRoot)) continue;
                    string extDir = Path.Combine(extRoot, other.ToString());
                    string extHash = UxrIdFingerprint.ReadHash(extDir);
                    if (extHash != null && extHash != baseline.Hash)
                    {
                        result.Lines.Add($"ВНИМАНИЕ: {other} в {extRoot} собран с другими UXR id ({extHash}) — " +
                                         "с новой сборкой по сети не сойдётся, пересобери его.");
                    }
                }
            }
        }

        // ── Справочное ─────────────────────────────────────────────────

        /// <summary>Полный путь результата профиля (APK, exe сервера).</summary>
        public static string OutputPathFor(BuildProfile profile) => SpecFor(profile).Path;

        private static Spec SpecFor(BuildProfile profile)
        {
            string root = Path.GetFullPath(OutputRoot);

            switch (profile)
            {
                case BuildProfile.Server:
                    return new Spec
                    {
                        Target = BuildTarget.StandaloneWindows64,
                        Group = BuildTargetGroup.Standalone,
                        Named = NamedBuildTarget.Server,
                        Subtarget = StandaloneBuildSubtarget.Server,
                        Path = Path.Combine(root, "Server", "VrBattlegroundsServer.exe")
                    };

                case BuildProfile.Quest:
                    return new Spec
                    {
                        Target = BuildTarget.Android,
                        Group = BuildTargetGroup.Android,
                        Named = NamedBuildTarget.Android,
                        Path = Path.Combine(root, "Quest", "VrBattlegrounds_Quest.apk")
                    };

                default:
                    return new Spec
                    {
                        Target = BuildTarget.Android,
                        Group = BuildTargetGroup.Android,
                        Named = NamedBuildTarget.Android,
                        Path = Path.Combine(root, "Tablet", "VrBattlegrounds_Tablet.apk")
                    };
            }
        }

        /// <summary>Сначала то, что собирается на текущей платформе: каждое переключение — реимпорт.</summary>
        private static List<BuildProfile> OrderBySwitches(IEnumerable<BuildProfile> profiles)
        {
            bool androidFirst = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            return profiles.OrderBy(p => (p == BuildProfile.Server) == androidFirst ? 1 : 0).ThenBy(p => (int)p).ToList();
        }

        private static string[] EnabledScenes() =>
            BuildSceneResolver.Resolve(false);

        private static IEnumerable<string> Split(string defines) =>
            defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).Where(d => d.Length > 0);

        private static void Log(Result result)
        {
            if (result.Passed) Debug.Log(result.Summary);
            else Debug.LogError(result.Summary);
        }
    }
}
