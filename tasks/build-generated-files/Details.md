# Build-Game: generated-файлы сборочных checkout — разбор

2026-10-20. Исходные факты — сборки Test scene-detail: c03777aa (server + quest) и da78253b6 (e2e Win64 + quest);
сохранённые патчи — `F:/CodexWorktrees/71af/Vr_Battlegrounds_ai/tasks/scene-detail/reports/build-roots-save-20261020/`.

## 1. `Packages/packages-lock.json` (обе платформы)

- `Packages/manifest.json`: `com.coplaydev.unity-mcp` = git URL с фиксированным commit `30d22075…`.
- В основном checkout, worker и Editor'ах агентов лежит embedded-копия `Packages/com.coplaydev.unity-mcp/`
  (ignored в `.gitignore`, ставит `Tools/UnityMcp/Install-Upstream.ps1` с патчами проекта). Embedded-пакет
  перекрывает зависимость из manifest, UPM пишет в lock `file:com.coplaydev.unity-mcp` / `embedded` — так lock
  и закоммичен в 706add496 (2026-10-03).
- В сборочном checkout копии нет → UPM при каждом запуске Unity пишет git-форму: URL из manifest, `source: git`, `hash`.
- Разница — свойство окружения, а не правка. Вариант (a) — один вид подключения — отклонён:
  - закоммитить git-форму → lock начнёт грязнеть во всех checkout с embedded-копией (основной, worker, агенты);
  - ставить embedded-копию в сборочные checkout или отключать lock (`enableLockFile`) — изменение MCP/UPM,
    зона agent-infra и сопровождающего, затрагивает worker; без согласования не делаем.

## 2. `Assets/URPDefaultResources/PC.asset` (только Win64)

- URP 17 `Editor/ShaderBuildPreprocessor.cs` в `OnPreprocessBuild` для всех URP-ассетов целевой платформы
  (`activeBuildTarget.TryGetRenderPipelineAssets`) вызывает `UpdateShaderKeywordPrefiltering` и
  `SaveAssetIfDirty`. Значения зависят от платформы: для Android с XR (`isQuest`) — off-вариант доп. света
  (`m_PrefilteringModeAdditionalLight: 4`) и уровни soft shadows (`Medium: 0`, `SoftShadows: 1`); для Win64 — `3/1/0`.
- Уровень PC исключён для Android в `QualitySettings` (`excludedTargetPlatforms: Android`), поэтому
  Android-сборка PC.asset не трогает; Quest исключён для Standalone — Win64 не трогает Quest.asset.
- Флаги `s_KeepOffVariantForAdditionalLights` / `s_UseSoftShadowQualityLevelKeywords` статические и
  сбрасываются только перезагрузкой домена: Win64-сборка в том же процессе после Quest (прежний общий
  запуск `server,quest`, меню в одном Editor) пишет в PC.asset Quest-значения `4/0/1`. Отсюда история:
  01babe4bd → `3/1/0`, b5f8f308f (тот же день) → `4/0/1`.
- Свежий процесс `-buildTarget Win64` (Build-Game) детерминированно даёт `3/1/0`: server c03777aa и e2e
  da78253b6 записали одинаковый blob `d57e092c8`.
- Значения перезаписываются перед компиляцией шейдеров каждой сборки и в Editor не используются, поэтому
  закоммиченное значение на сборку и Play Mode не влияет.

Решение (b): в dev фиксируется Unity-записанный blob `d57e092c8` (`3/1/0`), скопированный из build-test-win64.

## 3. Транзиентные файлы во время сборки

Во время сборки в checkout временно появляются `ProjectSettings/ProjectSettings.asset` (штамп `bundleVersion`)
и `Assets/Resources/PerformanceTestRun{Info,Settings}.json(.meta)`; сборщик убирает их сам (в win64 после e2e
da78253b6 их уже не было). В список generated они не входят: если остались — это сбой сборки, отказ правильный.

## 4. Механизм (c): `Tools/release/BuildCheckoutGenerated.ps1`

- Точный список: `Packages/packages-lock.json`, `Assets/URPDefaultResources/PC.asset`, `Assets/URPDefaultResources/Quest.asset`.
- Файл generated, только если статус ` M` (индекс не тронут) и:
  - lock: запись MCP в индексе — embedded или git-форма из manifest, в рабочей копии — форма, соответствующая
    фактическому наличию embedded-копии в этом checkout, а без полей `version/source/hash` MCP оба JSON совпадают;
  - URP: каждая строка diff — `  m_Prefilter*: <число>`, набор полей до и после один и тот же.
- Остальное (staged, untracked, другие поля, другие файлы) — Unexpected: `-Ref` и парная сборка отказывают, как раньше.
- `Build-Game.ps1`: проверяет всю группу до любых изменений; для выделенных checkout (не текущего проекта)
  generated-файлы сохраняет diff в `Logs/build-<stamp>-generated-{before,after}-<checkout>.patch` и возвращает
  `git checkout -- <пути>` перед `-Ref` и в `finally` после сборки (не трогает checkout, пока держится
  `Temp/UnityLockfile`). `-DryRun` только сообщает. Ревизия `-dirty` и парная проверка не учитывают generated.
  Раньше `-Ref` проверял и переключал checkout в одном цикле; теперь отказ по второму checkout не оставляет первый переключённым.
- `Run-QuestE2E.ps1 -Build`: тот же модуль перед проверкой чистоты пары (без возврата после сборки — его
  выполнит следующий запуск любого дирижёра).

## 5. Вне задачи / заметки

- Builder manifest (`BuildProvenance.CaptureSource`) снимает dirty уже после старта Unity, когда lock переписан:
  в `dirty.entries` сборки из выделенного checkout будет `Packages/packages-lock.json`. Это честная запись
  фактического состояния, итог `success` от неё не зависит. Можно отдельно научить его той же классификации.
- `BuildProvenance` фиксирует отпечаток `Tools/release/Build-Game.ps1`, но не нового модуля; при желании —
  добавить `Tools/release/BuildCheckoutGenerated.ps1` в список `tools` (правка Editor C#, нужна компиляция).
