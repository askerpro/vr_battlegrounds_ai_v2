# Проект: perf-stress без рук

Дата: 2026-10-12. Источники: контракт `quest-device-run` rev3, `Docs/testing.md` «Прогон на шлеме», запрос
lobby-decoration 4449 (переслан launch-infra 4453), код `Assets/Scripts/Debug/StressTest/`.

## Факты кода (origin/dev 2c3148767)

- Сценарий — `IE2EScenario`, запускается в обеих ролях: сервер (`server`) и шлем (`client-1`); каждый `-Arg`
  уходит обеим сторонам как `-e2eArg`. Параметры — только `E2EContext.Arg(ключ)`. Файлы — `E2EResult.AddArtifact`
  (абсолютный путь внутри `persistentDataPath`). Вложенные корутины через `StartCoroutine` нельзя.
- Старт — `StressTestLauncher.TryStart(config)` с клиента; сервер отказывает без аватара игрока и вне разминки
  (`StressTestServer`, «только в разминке»). Отказ сервера приходит позже: `StressTestClientSession.LastServerText`.
- Фазы ведёт сервер (`StressTestServer.Run` + `StressTestPlan`, меняются вместе). `AddPair` = «<имя>~успокоение»
  (не мерится) + «<имя>» (мерится).
- Отчёт: клиент и сервер пишут каждый свой `perf/<ts>_<role>_…/perf.log` и `summary.json` (роль, устройство,
  developmentBuild, refreshRate, display = FFR/OVR); строка в `perf/history.log`.
- Кукла: `StressPuppet` ставит корень = якорь + смещение корня игрока от старта. Если игрока переставить
  (телепорт), куклы уедут вместе с ним.

## Этап `scenario`

Клиент: ждёт подключение и свой аватар (`PlayerSession`), разминку; собирает `StressTestConfig` из аргументов;
`TryStart`; ждёт конец прогона; проверяет, что пройдены все фазы плана; объявляет артефакты.
Сервер: ждёт клиента и запуск прогона, ждёт его конец, объявляет свой каталог отчёта.

Аргументы (имена — performance, умолчания = `StressTestConfig`): `mode=normal|short|map-only|per-skin`,
`puppets` (0..20), `clutter`, `phase_s`, `warmup_s`, `skin` (-1 — вперемешку). Неверное значение — `error` до старта.

Проверки (объявить заранее): подключение и аватар; разминка; старт принят сервером; все фазы плана пройдены
(completed, без досрочного конца); отчёт записан (summary.json и perf.log существуют); приложение в фокусе весь
прогон (потеря фокуса/трекинга — прогон не замер, `passed=false` с причиной); время плана помещается в таймаут
сценария (иначе отказ до старта с подсказкой `-Timeout`).

Артефакты: клиент — каталог своего прогона и `perf/history.log`; сервер — свой каталог прогона.
Бюджетных порогов нет. Поза лежащего шлема статична — условие прогона, пишется в summary заголовка сценарием
(ключ `pose=static`).

Наблюдаемое состояние конца прогона берётся из кода стресс-теста; если его не хватает — добавить чтение
(последний каталог, completed, endReason) в `StressTestClientSession`/`StressTestServer`, без второго писателя.

## Этап `arena-layout`

Режим раскладки `arena`: куклы на полу `PhysicalArenaDefinition.FloorBounds` текущей сцены с отступом от стен
и станции арсенала, лицом в разные стороны, детерминированно (seed). Корни кукол закреплены: повторяют голову и
кисти игрока относительно корня, но не перемещение корня — иначе обход точек обзора их двигает.
`PhysicalArenaDefinition` — зона level-design, только чтение. Аргумент `layout=rows|map|arena`.

## Этап `view-tour`

Точки обзора — пресет (`views=lobby`) хелпера launch-infra (`E2EViews`, этап `e2e-views`). План фаз сервера:
разгон → «база: <точка>» по всем точкам → спавн кукол → «куклы: <точка>» по всем точкам (обход дважды, без
спавна на каждой точке). Клиент на старте каждой «~успокоение» переходит на её точку; в отчёт фазы — id точки и
фактическая поза камеры. Метрики по точке — фазы отчёта (p50/p95/max кадра, ovr_gpu, ovr_cpu), отдельного
views.json нет: один источник. Высота глаз = трекинг лежащего шлема, не 1,6 м — условие в отчёте.

API хелпера (launch-infra 4718, файл `Assets/Scripts/Debug/E2E/E2EViews.cs`, этап `e2e-views`; точная сигнатура —
по файлу после вливания): `E2EViews.TryLoadFromContext(ctx, out views, out error)` — точки из `-e2eArg views=<путь>`
(дирижёр `-ViewsFile` кладёт копию обеим сторонам); `E2EViews.GoTo(point, outcome, settleFrames=3)` — только
`yield return null`; `E2EViewOutcome`: Succeeded, Error, CameraPosition, Yaw, Pitch, Roll, EyeHeight, DeviationXZ,
DeviationYaw, FloorDeviation.

Условие замера GPU (4718): лежащий шлем даёт камеру на 0,80 м и тангаж +32° вниз — для GPU-замера лобби шлем ставить
на подставку ровно на высоте глаз, взгляд горизонтально. Фактические высота и тангаж из outcome — в отчёт фазы;
прогон с тангажом далеко от 0° для сравнения сцен не годится.

Факт 2026-10-14 (909fb967a): параметр — `views=<путь к файлу>` (не пресет); дирижёр `-HeadPose` (по умолчанию 1,6 м)
даёт глаза 1,60 м и тангаж 0° на всех точках — подставка не нужна; фаза на точку 20 с
(`StressTestPlan.ViewTourPhaseSeconds`), `views` включает раскладку «арена» и вокруг каждой точки круг 1,5 м без кукол
(`StressTestLayout.ViewPointExclusion`). Прогон 8 точек Lobby — ~7 мин плана, 33 фазы.

## Этап `controller-free-launch`

Решение пользователя 2026-10-12: e2e/перф-прогоны на шлеме без пробуждения контроллеров. Исследование (launch-infra
5183 + субагент, 2026-10-12): системный `LaunchCheckControllerRequiredDialogActivity` показывается VR-приложению
(`com.oculus.intent.category.VR`) без объявленной поддержки рук. Oculus XR Plugin 4.5.4 записи рук не пишет
(`OculusBuildProcessor.cs` ~607–691). Источник — форум Meta (Scriptable Testing), не официальная документация:
проверить на шлеме.

Решение: новый `Assets/Editor/VR_Battlegrounds/Release/QuestControllerFreeManifest.cs` —
`IPostGenerateGradleAndroidProject`, callbackOrder 10002 (после `OculusManifest` 10000 и `QuestManifestValidator`
10001), действует только когда GameBuilder взвёл его для профиля Quest и конфигурации Test (`Arm`/`Disarm` вокруг
`BuildPipeline.BuildPlayer`, как у валидатора, `GameBuilder.cs` ~519/527 — зона launch-infra, zone-ack). Идемпотентно
добавляет в `/manifest`:
`<uses-feature android:name="oculus.software.handtracking" android:required="false"/>` и
`<uses-permission android:name="com.oculus.permission.HAND_TRACKING"/>`. Prod не меняется.
`QuestManifestValidator` (по готовому манифесту и aapt): в Test записи рук обязательны, в Prod их быть не должно.

Побочные эффекты: Horizon OS при отложенных контроллерах переключится на руки; UltimateXR не переключится —
`UxrMetaHandTracking` под `#if ULTIMATEXR_USE_OCULUS_SDK` (define нет), ввод только через
`UxrUnityXRControllerInput`; руки аватара замирают, как при спящих контроллерах. Ревью avatar-ik — уведомление.

Проверка на шлеме: системная настройка «отслеживание рук» включена; при спящих контроллерах `am start -W` даёт
activity пакета, а не диалог; после пробуждения контроллеров ввод аватара работает; запроса разрешения нет;
Prod-APK (aapt) без записей рук; perf-stress GREEN без касания контроллеров. Тесты EditMode на патч манифеста —
после приёмки.
