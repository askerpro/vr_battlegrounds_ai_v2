# Version Control: Git -> Plastic (UVCS) Mirror

## Зачем в проекте двойной коммит

В проекте используется Git как основной VCS для локальной разработки и истории в репозитории, но параллельно поддерживается Unity Version Control (Plastic SCM) для командных процессов в Unity-экосистеме.

Автодублирование нужно чтобы:
- сохранять одинаковую историю изменений и в Git, и в Plastic;
- не делать второй ручной commit в Plastic после каждого git commit;
- гарантировать, что коммиты, собранные из staged-файлов, повторяются в Plastic с тем же текстом сообщения;
- снизить риск рассинхронизации между Git и UVCS.

## Где это настроено

### Локальная встроенная интеграция Unity Version Control

На машине asker 2026-10-03 встроенный UVCS-плагин collab-proxy 2.11.4 выключен
штатным переключателем. Причина подтверждена таймером Unity и логом Plastic:
BeforeAssemblyReload ждёт 10 секунд один ThreadWaiter при отсутствии UVCSOperations
и занятых соединений. После выключения два обычных reload заняли 4,749/4,819 с
вместо примерно 15 с. Полный [аудит](audit/editor-reload-audit-2026-10-03.md).

Это EditorPrefs-предпочтение с productGUID проекта, не файл под Git и не удаление
пакета/workspace. Git-хуки и внешний Plastic CLI продолжают работать независимо.
Включить обратно можно штатным переключателем Unity Version Control в Unity;
это возвращает встроенные статусы, overlays и обработчики ассетов и может вернуть
ожидание. При повторении сначала проверить реальные pending operations. Для
повторного выключения под Unity lock — `Tools/UnityMcp/Tests/DisableUvcs.cs.txt`.

### Git -> Plastic через внешний CLI

Система состоит из двух частей:

1. Автоконфиг git hooks path
- Файл: Assets/Editor/VR_Battlegrounds/VersionControl/GitHooksInitializer.cs
- Что делает: при загрузке Unity Editor запускает команду
  git config core.hooksPath .githooks
- Результат: Git начинает брать хуки из папки .githooks в корне проекта.

2. Post-commit хук
- Файл: .githooks/post-commit
- Тип: bash script
- Когда вызывается: после успешного git commit.

## Каким хуком делается дублирование

Используется именно post-commit хук: .githooks/post-commit.

Логика хука:
1. Читает текст последнего git-коммита:
- git log -1 --format=%B

2. Добавляет в Plastic новые файлы из этого коммита:
- git diff-tree --diff-filter=A ... | cm add

3. Удаляет из Plastic удаленные файлы из этого коммита:
- git diff-tree --diff-filter=D ... | cm rm

4. Делает commit в Plastic по списку путей из конкретного Git-коммита:
- git diff-tree ... | cm ci -a -c "$COMMIT_MSG" -

Критически важно, что используется список файлов только из HEAD (git diff-tree по последнему коммиту), а не глобальный cm ci -a по рабочему дереву. Это позволяет корректно зеркалить именно staged-набор последнего коммита.

## Предусловия

Для работы автодублирования должны выполняться условия:
- установлен Git;
- установлен Plastic CLI (cm) и доступен в PATH;
- открыт корректный Plastic workspace для этого проекта;
- у пользователя есть права на commit в текущую ветку UVCS;
- core.hooksPath у этого git-репозитория указывает на .githooks.

Проверки:
- git config --get core.hooksPath
- cm status --nochanges
- Get-Command cm (PowerShell)

## Как новому разработчику настроить у себя

Ниже минимальный онбординг, чтобы зеркалирование коммитов заработало на новой машине.

1. Установить инструменты
- Git for Windows (с Git Bash).
- Unity Version Control / Plastic SCM client и CLI (`cm`).

2. Проверить доступность `cm` в терминале
- PowerShell: `Get-Command cm`
- Если команда не найдена, добавить путь к `cm.exe` в `PATH`.

3. Открыть репозиторий и проверить hook path
- После первого открытия проекта в Unity сработает автоконфиг из
  `Assets/Editor/VR_Battlegrounds/VersionControl/GitHooksInitializer.cs`.
- Проверить: `git config --get core.hooksPath`
- Ожидаемое значение: `.githooks`

4. Если автоконфиг не сработал, выставить вручную
- `git config core.hooksPath .githooks`

5. Проверить, что Plastic workspace активен
- `cm status --nochanges`
- Команда должна вернуть состояние workspace без ошибок авторизации.

6. Сделать тестовый коммит в Git
- Выполнить обычный `git commit` с любым тестовым изменением.
- В выводе должен появиться блок:
  - `Дублируем коммит в Unity Version Control (Plastic SCM)...`
  - `Успешно скопировано в Plastic SCM!`

7. Проверить changeset в Plastic
- `cm find changeset "where branch = '/main/dev'" --format="{changesetid} | {date} | {owner} | {comment}" --nototal`
- В конце списка должен быть changeset с текстом последнего git-коммита.

## Как проверить, что дублирование сработало

После git commit в терминале появляется блок:
- "Дублируем коммит в Unity Version Control (Plastic SCM)..."
- "Успешно скопировано в Plastic SCM!"

Дополнительно можно проверить changeset:
- cm find changeset "where branch = '/main/dev'" --format="{changesetid} | {date} | {owner} | {comment}" --nototal

Или адресно по id:
- cm find changeset "where changesetid = <id>" --format="{changesetid} | {date} | {owner} | {comment}" --nototal

## Типовые проблемы и диагностика

1. Хук не запускается
- Проверить права и путь хука:
  - git config --get core.hooksPath
  - наличие файла .githooks/post-commit

2. В консоли нет cm
- Проверить PATH:
  - Get-Command cm
- Если команда не найдена, установить Plastic CLI или добавить в PATH.

3. Коммит в Git есть, в Plastic нет
- Проверить workspace:
  - cm status --nochanges
- Проверить доступ к ветке и авторизацию в UVCS.

4. Сообщение об ошибке в post-commit
- Хук печатает предупреждение, если cm ci завершился неуспешно.
- Сначала проверить состояние workspace и права, затем повторить commit.

## Ограничения

- Хук работает после git commit, а не до него.
- Если коммит отменен, post-commit не вызовется.
- Хук зеркалит только последний commit (HEAD), что и требуется для корректного соответствия Git <-> Plastic.

## Рекомендации для команды

- Не отключать .githooks без необходимости.
- Не менять post-commit на глобальный cm ci -a без списка файлов.
- При изменении логики хука обновлять Docs/CHANGELOG.md и этот документ.
