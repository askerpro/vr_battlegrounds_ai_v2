# LF для управляемого текста

PhaseA охватывает собственные C# в Assets/Scripts и Assets/Editor/VR_Battlegrounds,
Docs Markdown, agent instructions и Tools Python/PowerShell/JS/patches. Новые и
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

Source checker/runtime и tracked launcher выпускаются штатно отдельно; владельцы
обновляются rebase от origin/dev с сохранением своих diff/staged/untracked/meta.
Вызов нового launcher до обновления runtime может завершиться отсутствием checker;
root проверяет export и exact launcher после pinned source deployment.
