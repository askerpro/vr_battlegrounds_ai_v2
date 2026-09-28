# Сборка: сервер, Quest, планшет

Три сборки одной сессии — выделенный сервер под Windows, клиент Quest и пульт админа на
Android-планшете. Код — `Assets/Editor/VR_Battlegrounds/Release/`.

## Как собрать

| Откуда | Как |
|---|---|
| Редактор | `Tools/VR Battlegrounds/Release/Собрать всё (сервер, Quest, планшет)` или по одной |
| Проверка без сборки | `Tools/VR Battlegrounds/Release/Проверить UXR id (без сборки)` |
| CLI, редактор закрыт | `powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1 [-Targets quest,tablet] [-Config Test\|Prod]` |
| Агент | `GameBuilder.Build(new[] { BuildProfile.Server }, BuildConfig.Test)` → `Result.Summary` |

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

В тест-сборке события UXR несут отладочные подписи (патч 8; совместимо с прод-сборками) и работает
`DebugOrchestrator` (автозагрузка карты и автостарт матча из `DebugBootstrapConfig`). В прод он выключается
сам, сервер попадает в Lobby через `onlineScene`.

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
Oculus на месте. Проверка на шлеме: `adb shell dumpsys package <пакет> | findstr Category` — должна быть
строка `com.oculus.intent.category.VR`.

## Что меняется на время сборки

Всё возвращается в `finally`, файлы проекта не меняются:

- лоадеры и `InitManagerOnStart` XR Management — в памяти, **без** `AssetDatabase.SaveAssets()`
  (`XRPackageMetadataStore.AssignLoader` его зовёт и сохранил бы невыданные на диск id — known-issues #11);
- идентификатор Android-пакета (планшет), min SDK Android (Quest);
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
  реимпорт, поэтому «Собрать всё» начинает с текущей платформы.
