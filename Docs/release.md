# Сборка: сервер, Quest, планшет

Три сборки одной сессии — выделенный сервер под Windows, клиент Quest и пульт админа на
Android-планшете. Код — `Assets/Editor/VR_Battlegrounds/Release/`.

## Как собрать

| Откуда | Как |
|---|---|
| Редактор | `Tools/VR Battlegrounds/Release/Собрать всё (сервер, Quest, планшет)` или по одной |
| Проверка без сборки | `Tools/VR Battlegrounds/Release/Проверить UXR id (без сборки)` |
| CLI, выделенные сборочные worktree (по умолчанию) | `powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1 [-Targets all\|server,quest,tablet] [-Config Test\|Prod] [-Ref <commit>] [-DryRun]` |
| CLI, в текущем checkout | `powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1 -UseCurrentWorktree [-Targets ...] [-Config Test\|Prod]` |
| Агент | `GameBuilder.Build(new[] { BuildProfile.Server }, BuildConfig.Test)` → `Result.Summary` |
| Пара для прогона на шлеме | `Run-QuestE2E.ps1 -Build` (batch по умолчанию собирает через `build-test-android` + `build-test-win64` без переключения платформы) или `E2EPlayerBuilder.StartPairBuild(<статус>)` в редакторе — [testing.md](testing.md) |

### Выделенные сборочные worktree (без переключения платформы и без очереди воркера)

Чтобы тестовые и релизные сборки не переключали платформу туда-обратно (`Android ↔ Win64`), не перетирали кэш `Library/Bee` между `Test` и `Prod` и не занимали интерактивный `unity-editor-worker`, используются **4 выделенных сборочных worktree** с ролью LFS `build` (`hydration = full`, см. [agents/lfs-worktrees.md](agents/lfs-worktrees.md)):

| Назначение | Платформа (`-buildTarget`) | Путь worktree | Профили |
|---|---|---|---|
| **Test — Win64** | `Win64` | `F:\CodexWorktrees\build-test-win64\Vr_Battlegrounds_ai` | `Server` (`Test`), `e2e`-сервер (`Build/e2e/VrBattlegrounds.exe`) |
| **Test — Android** | `Android` | `F:\CodexWorktrees\build-test-android\Vr_Battlegrounds_ai` | `Quest` (`Test`), `Tablet` (`Test`) |
| **Prod — Win64** | `Win64` | `F:\CodexWorktrees\build-prod-win64\Vr_Battlegrounds_ai` | `Server` (`Prod`) |
| **Prod — Android** | `Android` | `F:\CodexWorktrees\build-prod-android\Vr_Battlegrounds_ai` | `Quest` (`Prod`), `Tablet` (`Prod`) |

Правила работы со сборочными worktree:
- `Build-Game.ps1` по умолчанию автоматически направляет `quest,tablet` в `build-<config>-android`, а `server` — в `build-<config>-win64` (если папки существуют; ключ `-UseCurrentWorktree` возвращает сборку в текущий checkout).
- `Run-QuestE2E.ps1 -Build` по умолчанию собирает APK Quest в `build-test-android` (`E2EPlayerBuilder.RunQuestBatch`) и Windows e2e-плеер сервера в `build-test-win64` (`E2EPlayerBuilder.RunBatch`) без единого переключения платформы и без аренды `unity-editor-worker` (ключ `-SingleWorktreeBuild` возвращает сборку в один checkout через `RunPairBatch`).
- Перед стартом `Unity.exe` скрипты проверяют, что:
  1. В целевых сборочных worktree закрыт редактор (`Temp/UnityLockfile`) и `vrb.lfs.role != agent`, а все отслеживаемые LFS-файлы гидратированы (нет указателей `version https://git-lfs.github.com/spec/v1`).
  2. Оба парных worktree (`win64` и `android`) находятся на одном `HEAD` SHA и не содержат незакоммиченных правок в `Assets/`, `Packages/`, `ProjectSettings/` (параметр `-Ref <commit>` в `Build-Game.ps1` переключает задействованные сборочные worktree на нужный коммит и дотягивает LFS).
  3. Тестовые сборки в обоих worktree штампуются единым `-buildRevision <sha12>`, а после завершения сверяются отпечатки `uxr-ids.txt` между `build-<cfg>-win64/Build/Server` и `build-<cfg>-android/Build/Quest|Tablet`.
- При запуске `Prod`-сборки в `unity-editor-worker` `GameBuilder` выводит явное предупреждение в `Result.Summary`.

### Конфигурация: тест и прод

Переключатель в меню `Tools/VR Battlegrounds/Release/Конфигурация: …` или `-Config Test|Prod` (по умолчанию
Test). IL2CPP-настройки Android выставляет `BuildConfigScope` на время сборки и возвращает — Player Settings
не меняются. Сервер на Mono, от конфигурации у него только флаг Development.

| | Тест на шлеме | Прод |
|---|---|---|
| Development Build | да | нет |
| IL2CPP Code Generation | OptimizeSize (быстрее сборка) | OptimizeSpeed |
| C++ Compiler Configuration | Release | Master |
| Stacktrace Information | метод, файл, строка | только метод |
| Managed Stripping Level | Minimal | Low |

Общее для обеих: IL2CPP, ARM64, .NET Standard 2.1, Incremental GC, HTTP запрещён, Active Input Handling
**Both** — на `PlayerBase` висит `UxrGamepadInput`, который без `ULTIMATEXR_USE_UNITYINPUTSYSTEM_SDK` читает
старый `Input.GetAxis`. Без Player Settings (значения `{}`) Unity берёт OptimizeSpeed / Release / MethodOnly / Minimal.

Прод со stripping Low может вырезать то, что UltimateXR и Mirror достают рефлексией: прод-сборку прогонять
на шлеме целиком.

Прод всем профилям (сервер, Quest, планшет) добавляет define `VRBG_NO_E2E` через `extraScriptingDefines`: e2e-харнесс
и его сообщения Mirror в прод-сборку не попадают. Тест и e2e-плеер — без него.

**Штамп ревизии** (тест и e2e-плеер, не прод): на время сборки `BuildRevisionStamp` ставит
`bundleVersion = <база>+<ревизия>` (например `0.1.0+605c90736abc`; `-dirty` — есть изменения отслеживаемых файлов
в `Assets/`, `Packages/`, `ProjectSettings/`; `unknown` — git недоступен, сборка не падает). Ревизия —
`git rev-parse --short=12 HEAD`, её видно в `Result.Summary`, во versionName APK и в `Application.version`.
После сборки версия возвращается и настройки сохраняются — `ProjectSettings.asset` не меняется. Прод не штампуется:
versionName магазина не трогаем. Пару для прогона на шлеме (e2e-плеер + APK одной ревизии) собирает
`E2EPlayerBuilder` — [testing.md](testing.md), «Прогон на шлеме».

**Диалог «Unsupported Input Handling on Android»** (Active Input Handling = Both): перед Android-сборкой `GameBuilder`
отвечает за человека «Ignore» на текущую сессию редактора — без этого сборка агента в открытом редакторе висела
до клика. В итоге сборки — строка `ВНИМАНИЕ: Active Input Handling = Both…`; корень — backlog
`input-handling-new-only`. Публичный `EditorUtility.SetDialogOptOutDecision` для этого не годится: в Unity 6000.4
диалог читает opt-out из `SessionState` под ключом `DialogOptOut.<ключ>` как `DialogResult`, поэтому сборщик пишет
`SessionState.SetInt("DialogOptOut.AndroidInputHandlingWarning", DialogResult.DefaultAction)`. В batch-режиме Unity
диалог не показывает, только предупреждение в логе.

В тест-сборке события UXR несут отладочные подписи (патч 8; совместимо с прод-сборками) и работает
больше не работает `DebugOrchestrator`: с 2026-09-30 он только в редакторе (сборка `VrBattlegrounds.DebugBootstrap`) и
в сборку не попадает ни в какую. Сервер попадает в Lobby через `onlineScene`, ботов на сервере добавляет админ с планшета.

Результат:

| Профиль | Файл | Отличия |
|---|---|---|
| `Server` | `Build/Server/VrBattlegroundsServer.exe` | Windows Dedicated Server (subtarget `Server`), XR не стартует. Define-символы Server в Player Settings — копия Standalone; сборщик всё равно досыпает недостающие и проверяет `MIRROR`/`ULTIMATEXR_USE_MIRROR_SDK` |
| `Quest` | `Build/Quest/VrBattlegrounds_Quest.apk` | Android + `OculusLoader`, min SDK поднимается до 32, если ниже 29 |
| `Tablet` | `Build/Tablet/VrBattlegrounds_Tablet.apk` | Android без XR, `BUILD_ROLE_ADMIN`, пакет `<id>.tablet` |

Рядом с каждой сборкой — `uxr-ids.txt` с отпечатком UXR id.

Запуск сервера — `Start-Server.cmd` (или `Start-Server.ps1`) рядом с exe; сборщик копирует их из
`Tools/release/server/`. Сервер стартует с `-logFile Logs\server-<дата-время>.log`, лог одновременно
показывается в консоли, хранятся последние 20. Доп. аргументы передаются серверу как есть.
Перенаправление stdout (`> file`) не используем: в PowerShell оно идёт через его конвейер с его кодировкой.

Кириллица в консоли сервера: Unity пишет UTF-8, консоль Windows читает в 866 («╨Ю╤В╨┐╤А…»).
`ServerConsoleEncoding` (`Scripts/Core/`) при старте Dedicated Server делает `SetConsoleOutputCP(65001)`;
для старых сборок — `chcp 65001` перед запуском.

Сервер запускается как обычный exe: он headless сам по себе, `Mirror.Utils.IsHeadless()` верно и без
`-batchmode -nographics`, `GameNetworkDiscovery` выбирает роль сервера.

## Одинаковые UXR id — почему сборки сходятся

Канал состояния UltimateXR ссылается на компоненты по `_uxrUniqueId`. Объект сцены берёт id из сцены,
объект из префаба — `Combine(id префаба, netId)`. Разные id у сервера и клиента — и каждое событие
(захват, выстрел, магазин) отвергается с `UxrComponentNotFoundException`.

Что гарантирует одинаковость:

1. **Сборка id не выдаёт.** `NotifyOnValidate` заглушён на `BuildPipeline.isBuildingPlayer`, а префабу-ассету
   id не меняет вообще (патч 10, `sdk-patches.md`).
2. **Префабы уходят в сборку из памяти, сцены — с диска.** Поэтому перед сборкой `GameBuilder`:
   - прогоняет `UxrUniqueIdPersister.NormalizeAll()` — флаги `__isInPrefab`/`__prefabGuid` на диске верные;
   - сверяет id каждого UXR-компонента префабов сборки в памяти с диском — расхождение = отказ;
   - отказывает, если сцена сборки открыта и не сохранена (хост в редакторе разошёлся бы со сборками);
   - отказывает на нулевых id.
3. **Отпечаток.** Хэш всех пар «файл — id» по сценам сборки, их зависимостям и `Resources`. Снимается до
   сборок и сверяется после каждого переключения платформы и после каждой сборки. Изменился — сборка падает.
4. **Сравнение с прежними сборками.** Собрал один профиль — `GameBuilder` сверяет отпечаток с `uxr-ids.txt`
   остальных в `Build/` и предупреждает, если они собраны с другими id.

Правило: **все три сборки — из одного коммита, лучше одним запуском «Собрать всё».** Пересобрал что-то после
правки префаба или сцены с UXR-компонентами — пересобери и остальные. Отпечаток ловит расхождение id, но не
расхождение кода: сборки разных коммитов несовместимы по протоколу, даже если id совпали.

## XR выставляется после переключения платформы

Переключение платформы реимпортирует `XRGeneralSettingsPerBuildTarget.asset` с диска, и лоадеры,
заданные в памяти до него, пропадают. Так первая сборка Quest вышла без Oculus: в манифесте не было
`com.oculus.intent.category.VR`, шлем запускал её плоским окном — чёрный экран без VR. Сейчас
`XrBuildSettingsScope` оборачивает только `BuildPlayer`, а перед сборкой Quest проверяется, что лоадер
Oculus на месте.

## Quest собирается только редактором, скомпилированным под Android

VR-категорию и `com.oculus.supportedDevices` дописывает в манифест `OculusManifest` (Oculus XR Plugin,
`IPostGenerateGradleAndroidProject`, callbackOrder 10000). Класс обёрнут в `#if UNITY_ANDROID`: он есть только
в editor-сборке, скомпилированной при активной платформе Android. Переключение платформы внутри вызова
(`GameBuilder`, `BuildPlayer`) editor-скрипты не перекомпилирует — это происходит позже, с перезагрузкой домена.
Поэтому редактор на Standalone, переключившись на Android и сразу собрав Quest, выпускает плоский APK, хотя
лоадер Oculus на месте (2026-10-12, аренда 516: worker на Standalone). То же было у `Build-Game.ps1 -Targets all`:
`-buildTarget` выбирался по первому профилю, `server` → Win64. Под тем же `#if` и Mirror `AndroidManifestHelper`
(`CHANGE_WIFI_MULTICAST_STATE` для LAN-discovery) — он нужен и Quest, и планшету.

Правило и проверки:

- `GameBuilder.Build` до любой сборки проверяет `QuestManifestValidator.OculusManifestCompiled()` — есть ли в
  домене тип `UnityEditor.XR.Oculus.OculusManifest`. Нет — ошибка без сборки для любого Android-профиля (Quest и
  планшет): «Переключи активную платформу на Android, дождись компиляции и повтори; batch — запускай Unity с
  -buildTarget Android». Планшету VR-категория не нужна, но без Android-домена Mirror не добавит multicast, и
  LAN-discovery не найдёт сервер — правило одно: Android собирает только редактор, скомпилированный под Android;
- `Build-Game.ps1` запускает Unity отдельно на группу платформ: `quest,tablet` — один запуск с
  `-buildTarget Android`, `server` — отдельный с `-buildTarget Win64`; у каждого свой лог
  `Logs/build-<дата>-android.log` / `-server.log`. Первая упавшая группа останавливает скрипт (код 1);
- `E2EPlayerBuilder.StartPairBuild` в редакторе не на Android запоминает задание в `SessionState`, переключает
  платформу (`SwitchActiveBuildTargetAsync`) и продолжает после перекомпиляции (`[InitializeOnLoadMethod]`):
  Quest первым, затем e2e-плеер Windows. Файл статуса всё это время пустой. Ошибки компиляции под Android или
  три перезагрузки домена без Android — ошибка в статус, задание снимается. `RunPairBatch` без Android-домена
  сразу завершается с кодом 1 — Unity для него запускается с `-buildTarget Android`.

Манифест проверяется дважды, по одним правилам:

- **после генерации Gradle** — `QuestManifestValidator` (callbackOrder 10001, сразу за Oculus), только
  в сборке `GameBuilder` Android-профиля. Quest без `com.oculus.intent.category.VR` или без
  `com.oculus.supportedDevices` — `BuildFailedException` до Gradle; в тексте настройки XR Android
  (`InitManagerOnStart`, лоадеры) и есть ли `OculusManifest` в editor-сборке. Планшет с VR-категорией — тоже ошибка;
- **по готовому APK** — `aapt dump xmltree <apk> AndroidManifest.xml` (старшие build-tools из SDK редактора,
  иначе встроенного в модуль Android). Нет aapt — предупреждение в итоге сборщика, не ошибка.

`supportedDevices` без Quest 3 (`eureka`/`quest3`) — предупреждение: `TargetQuest3` в
`Assets/XR/Settings/OculusSettings.asset` выключен, список шлемов — зона performance.

Проверка на шлеме: `adb shell dumpsys package <пакет> | findstr Category` — должна быть
строка `com.oculus.intent.category.VR`.

## Что меняется на время сборки

Всё возвращается в `finally`, файлы проекта не меняются:

- лоадеры и `InitManagerOnStart` XR Management — в памяти, **без** `AssetDatabase.SaveAssets()`
  (`XRPackageMetadataStore.AssignLoader` его зовёт и сохранил бы невыданные на диск id — known-issues #11);
- e2e-плеер Windows (`E2EPlayerBuilder`) собирается в том же скоупе, что Dedicated Server: без
  `InitManagerOnStart`, иначе e2e-сервер получал Oculus от Standalone и пытался поднять шлем. Платформа
  перед ним явно переключается на Windows до скоупа, поэтому после пары редактор остаётся на Standalone;
- идентификатор Android-пакета (планшет), min SDK Android (Quest);
- `bundleVersion` со штампом ревизии (тест, e2e) — возвращается и сохраняется `AssetDatabase.SaveAssets()`;
- активная платформа — после сборки в редакторе переключается обратно.

На диск пишется одно: при первой сборке Android в `XRGeneralSettingsPerBuildTarget.asset` заводятся
настройки XR для Android (пустые, `InitManagerOnStart = false`). До этого у проекта XR был только для Standalone.

## Android: `BuildPlayerOptions.subtarget` — это сжатие текстур

У Standalone `subtarget` — `StandaloneBuildSubtarget` (Player/Server), у Android — `MobileTextureSubtarget`,
формат сжатия текстур. Первая версия сборщика передавала Android `StandaloneBuildSubtarget.Player` = `2`,
а для Android `2` — это **PVRTC**. Unity 6 PVRTC не поддерживает (`PVRTC compression is obsolete and no
longer supported`) и кладёт текстуры RGBA32: данные Quest — 5,5 ГБ, Gradle падает на
`Archive's size exceeds the limit of 4GByte` (`bundleReleaseLocalLintAar`). Вдобавок BuildPlayer запоминает
subtarget в `EditorUserBuildSettings.androidBuildSubtarget`, и PVRTC оставался в настройках машины.

Сейчас Android собирается с `MobileTextureSubtarget.ASTC`, и то же значение ставится в
`androidBuildSubtarget` до переключения платформы: импорт идёт один раз и сразу в ASTC.

Признаки: в логе строки `PVRTC compression is obsolete`; в итоге сборщика («данные … По типам») `Texture2D`
на гигабайты, текстура 2048 весит 21,3 МБ (RGBA32 с мипами), а не ~5 МБ.
Итог сборщика всегда печатает 25 самых тяжёлых ассетов из `BuildReport.packedAssets`, даже при падении в Gradle.

## Известные ограничения

- `applicationIdentifier` Android — шаблонный `com.UnityTechnologies.com.unity.template.urpblank`. Сменить
  до первой установки на устройства: смена потом — это новое приложение.
- Первая сборка каждой платформы долгая (IL2CPP, импорт текстур под Android); переключения платформы —
  реимпорт, поэтому «Собрать всё» начинает с текущей платформы. С Quest «Собрать всё» работает только из
  редактора на Android (Quest, планшет, затем сервер); на Standalone сборка откажет до старта.
