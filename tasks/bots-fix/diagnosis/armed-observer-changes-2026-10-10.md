# Read-only telemetry вооружённого стенда

Дата: 2026-10-10. Runtime observer подключается до Begin/Arm и не управляет lifecycle оружия.

- Case result содержит bounded WeaponTimeline: first-seen snapshot, typed ledger commits, source emission/firearm events, readiness faults, actual damage ActorSource/ActorTarget и Life до/после.
- Readiness читается через public cloned snapshot, M/total getters; identity и binding owner/epoch разрешаются заново. Unity Object/state DTO не сериализуются: сохраняются значения.
- First-seen registration не считается cold-before-automation. `ColdBaselineStatus=Unknown` сохраняется явно, nullable поля не заменяются нулём. Source resolver отделён от actual damage attribution.
- 2048 записей на case, 64 наблюдаемых оружия, 32 pending записей на ещё не связанную candidate weapon, 16 ошибок observer. Счётчики dropped показывают потерю полноты.
- Подписки static/source/firearm/actor снимаются при Dispose до удаления собственных акторов, даже если сервер остановлен. Замена текущих actor объектов пересобирает подписки при sample.
- Никаких assertions, NUnit, ammo/health/pose writes, reflection, автоматической подготовки или прямого выстрела не добавлено. Existing gameplay expectations остаются прежними.

Проверка Unity и compilation ещё не выполнялась; root проверяет joint candidate отдельно. SDK71 owner attribution остаётся внешней зависимостью. First-seen/sample timeline без owner pre-automation hook не доказывает холодный старт; позднее создание/замена актора между samples может оставить health-before неизвестным.
