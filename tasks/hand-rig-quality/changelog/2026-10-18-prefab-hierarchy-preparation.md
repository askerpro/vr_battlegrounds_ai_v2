# Подготовка миграции чистой базы кистей — 2026-10-18

Это подготовка исходников; ассеты ещё не мигрированы и работа не принята.
Основание — решение пользователя из Antigravity, повторно переданное в текущий чат.
Контракт: `../contracts/cyborg-prefab-hierarchy-v2.md`.

## Изменения

- `CyborgHandsPrefabMigration` снимает независимые разрешённые экземпляры всех пяти
  prefab до изменения предков; снимок Variant снимает все outer-root connections,
  сохраняя вложенный MEF_Rig. Новый скелет вместо существующего graft не создаётся.
- Общий PlayerBase освобождается от собственного Cyborg body; device integration,
  controller visuals, camera/input остаются. У CyborgHandsBase прямой Unity/UXR parent
  PlayerBase, полный разрешённый набор поз и прежний GUID. Leaf defaults сохраняются.
- MEF/Black сохраняют авторенные LOD/меши/хитбоксы/карманы/часы/калибровку из снимков.
  Действующая ссылка на удаляемое старое Cyborg body блокирует удаление.
- Пакет сохраняет исходные байты outputs и .meta в ignored reports, проверяет входящие
  PPtr по GUID/localID и структурной идентичности. При отказе возвращает только свои
  outputs. Если Unity меняет fileID внешних потребителей, требуется отдельный точный
  remap; автоматической записи в оружие или чужие данные нет.
- Прежние writers, возвращавшие MEF в базу или меняющие только SDK parent, удалены.
  AvatarHandBases использует CyborgHandsBase и native reparent с единым writer.
  Чистый string helper RebaseYaml остаётся obsolete без production-вызовов.

## Фактические проверки

- Полная offline-компиляция доступного default Editor assembly: 333 исходника,
  405 metadata references, exit0, errors0, warnings36 (вне изменённых файлов).
  Метаданные Unity взяты read-only из действующего worker; runtime assembly/SDK
  asmdefs не пересобирались, Unity import, Weaver и выполнение Prefab API не проверены.
- Отчёты `reports/prefab-hierarchy-20261018/compile-final/compile-result.json`,
  `compile.rsp`, `compile.log`; source hashes во время компиляции не изменились.
- Статическое независимое ревью выявило P1 одиночного unpack Variant; исправлено
  циклом root-only unpack, ревьюер подтвердил устранение по исходнику.
- `git diff --check`, JSON plan и отсутствие CR в owned text files проверены.
- Новые/переписанные тесты и probes не создавались и не запускались.

## Открыто

Unity MCP tools отсутствуют в текущем клиенте; canonical infra_issue8986 отправлен
agent-coordination-protocol. Route-resolve вернул recipient_cleared: подтверждённой
native-доставки нет. Launcher --help в sandbox: uv cache access denied; broker status
в sandbox не открывает SQLite. Elevated read-only status работает: active/offered=null,
blocked/recovery_required=false, базовый SHA8dfccb580. Эти факты не доказывают MCP initialize.
Runtime bcd591040c02dff494b8fcff63d63c4c8260df14. Ремонт/обход не выполнялся.

Уточнение по запросу пользователя: сравнение pre-rebase4f501f4af и HEAD по
`.codex/config.toml`, `.mcp.json`, `unity_mcp_proxy.py`, `broker_runtime.py` — без изменений.
`codex mcp list --json` вне sandbox видит enabled unityMCP с правильным worktree launcher;
elevated proxy --help exit0. Desktop log
`codex-desktop-36fc46c8-f9bf-417c-9d65-f9c607dff351-168452-t0-i1-012257-0.log:2241`
фиксирует текущий chat01a14cad-ff5a-7023-a900-8dbdb580abc2 в01:44:26.995Z:
`MCP client for unityMCP failed to start: MCP startup failed: timed out awaiting tools/list after 30s`.
Следовательно конфигурация при старте была обнаружена; rebase не принёс новый MCP.
Успешный --help не доказывает доступность tools/list; restart ещё не выполнялся.

Далее: после штатного восстановления tools — свежий fetch/rebase, scoped checkpoint,
Unity request/lease/guard, Describe → Rebuild и полный сохранённый исход/диагностика.
При известном terminal сразу finish/receive. При timeout сначала durable evidence,
без replay. Затем Unity/headset-проверка качества пользователем и только после неё
обновление устаревших тестовых ожиданий, ревью и принятый commit/integration.
