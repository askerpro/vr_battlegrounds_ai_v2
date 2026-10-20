# LF для управляемого текста

PhaseA охватывает собственные C# в Assets/Scripts и Assets/Editor/VR_Battlegrounds,
Docs Markdown, agent instructions и Tools Python/PowerShell (*.ps1, *.psm1)/JS/patches. Новые и
успешно изменяемые файлы этих путей сохраняйте с LF. Не переписывайте untouched
legacy/foreign файлы; binary/LFS и generated IDE outputs сохраняют свои правила.
GUID/meta и чужие staged/untracked изменения сохраняются.

PhaseB добавляет LF для Unity YAML (.meta/.prefab/.unity/.asset/.mat/.controller/.anim
и прочих текстовых форматов Unity из индекса), ProjectSettings/**, Packages/manifest.json,
packages-lock.json и tasks/**/*.md|json. Индекс уже LF: атрибуты меняют только
рабочую копию. YAML физически не перематериализуйте вручную и не делайте массовый
checkout/renormalize: файлы сходятся к LF при сохранениях Unity. Утилита
перематериализации сопровождающего — только для текстовых исходников и чистых
(без локальных изменений) файлов. 36 бинарных Unity SerializedFile .asset
(LightingData, NavMesh, TerrainData) помечены binary в .gitattributes; vendor .cs/.shader
остаются CRLF по умолчанию. Default `* text=auto eol=crlf` не меняется.
Порча serialized binary (LF→CRLF при старой материализации): аудит
`python Tools/agents/editor-broker.py serialized-audit --repo <worktree>`, восстановление
`python Tools/agents/serialized_repair.py --repo <worktree>` (dry-run), затем `--apply`
при закрытой Unity этого worktree. Если утилита не может определить состояние Unity
(процессы не перечислить, нет `-projectPath`), `--apply` откажет до явного
`--editor-closed-confirmed` — только после ручной проверки, что Unity закрыта.

Перед checkpoint и публикацией проверьте только явно принадлежащие вам файлы:
`python Tools/agents/check_text_eol.py --repo . --staged --path Assets/Scripts/Your.cs`.
Повторите --path для каждого своего изменённого/нового файла. --staged проверяет
raw индекс дополнительно; физический файл проверяется всегда. Git clean/diff или
LF blob в индексе не доказывают отсутствие CRLF/mixed на диске. Checker не исправляет
и не stages файлы. Exit0 — проверенные LF пути чисты,1 — нарушения,2 — отказ/ошибка.
Binary/LFS/non-LF policy/symlink paths перечисляются как skipped, не как проверенные.

MCP 10.2.0 получает tracked `Tools/UnityMcp/script-lf-10.2.0.patch` через штатный
SDK installer: четыре create/update/text-edit/structured-edit sinks нормализуют
только итог успешной изменяющей операции после проверки original SHA/offsets.
No-op не переписывает файл; returned SHA соответствует физическим UTF8 bytes.
Патч требует отдельной SDK reservation и Unity приёмки в broker RUNNING lease.
Существующий embedded package installer не перезаписывает: его обновление root
проводит отдельно с guard, исходным hash/backup и проверкой результата.

Unity new C# использует Unix. Это не конвертирует существующие скрипты.
IDE csproj/sln генераторы сохраняют CRLF. Массовый git add --renormalize запрещён.

`.githooks/pre-push` read-only проверяет отправляемый диапазон (remote sha..local sha;
для новой ветки — от merge-base с dev): изменённые пути с eol=lf, кроме -text/binary,
не должны содержать CR в blob. Нарушение — exit 1 со списком путей; файлы не
исправляются. Удаления и пропущенная база не блокируют push; затем работает git lfs pre-push.

## UTF-8 BOM для Tools/**/*.ps1|psm1

Целевая оболочка — встроенный Windows PowerShell 5.1: он читает .ps1/.psm1 без BOM
как ANSI, и кириллица в строках/комментариях ломает разбор (например, Import-Module
модуля из Tools/e2e). Поэтому `Tools/**/*.ps1` и `Tools/**/*.psm1` с любым
байтом >= 0x80 храним как UTF-8 с BOM (EF BB BF); чистый ASCII допустим без BOM.
`.editorconfig` задаёт для них `charset = utf-8-bom` (секция перекрывает общую
`[*] charset = utf-8`, из-за которой редакторы снимали BOM при сохранении).
`.githooks/pre-push` в том же диапазоне, что и EOL-проверка, read-only проверяет
изменённые/добавленные пути (расширение без учёта регистра): raw blob с байтом >= 0x80
без BOM — отказ exit 1 со списком `<local ref>: <path>`; отчёты EOL и BOM выводятся
вместе. Исправление: сохраните файл как «UTF-8 with BOM» (VS Code: Save with Encoding)
или `$t = Get-Content -Raw -Encoding UTF8 <file>; [IO.File]::WriteAllText(<полный путь>, $t,
(New-Object Text.UTF8Encoding $true))`, сохраните LF и сделайте новый коммит.

Source checker/runtime и tracked launcher выпускаются штатно отдельно; владельцы
обновляются rebase от origin/dev с сохранением своих diff/staged/untracked/meta.
Вызов нового launcher до обновления runtime может завершиться отсутствием checker;
root проверяет export и exact launcher после pinned source deployment.
