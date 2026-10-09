# Адресация native уведомлений и скрытые служебные процессы

Runtime agent-infra241659884b2220d9fcdd72afa2169ac56c9b181a установлен.
Внедрены route-publish/route-resolve/route-clear: self-publication, own checkout,
CAS epoch/tombstone, защита дублей и candidate/manual. План, контракт и permit
не меняются. Claude использует name [ref] из свежего полного ListAgents; UUID
плана и raw ref не используются. Хаб не подтверждает native identity/delivery.

Во всех согласованных служебных Git/PowerShell/Python subprocess добавлен
CREATE_NO_WINDOW для Windows. Исправлены также продуктовые заглушки поиска
runtime и запуска прокси, сохранив argv/stdio/return code.

Проверки: полный UV-набор333 — 332 passed, 1 skip (symlink ОС), 587,815 с;
routes10/10, coordination97/97, заглушки4/4. Baseline заглушек: 2 Windows failures,
Unix без изменения поведения. Ревью native routing без Critical/Important.

Live: собственный маршрут Codex очищен (нет глобальных peer endpoints), resolver
вернул manual; чужие адреса не публиковались и native send не выполнялся.
Publisher42320 возобновлён, pause=false. За 8 секунд после старта новых видимых
консолей не обнаружено; это не гарантия поведения всех процессов Codex/Claude.

Для заглушек нужен sync своего worktree с origin/dev. Уже работающий MCP-прокси
перечитывает код после переподключения клиента, выполнять после своего finish.
Никаких Editor restart, автоматических кликов диалогов или новых daemon не добавлено.
