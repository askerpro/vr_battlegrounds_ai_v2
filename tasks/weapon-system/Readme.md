# Единая WeaponSystem: машина состояний оружия

## Цель и мотивация

Всё поведение огнестрела — затвор, патронник, спуск, поза, звук, вибрация, отклик — решает одна машина
состояний `WeaponStateMachine`. Учёт патронов один, в SDK; у каждого канала вывода один исполнитель.
Раньше за одно и то же отвечали несколько компонентов и правили состояние друг за другом: рассинхрон позы,
двойные звуки, сетевая ошибка учёта.

## Статус

Обновлено 2026-10-09. Владелец — агент-эксперт `weapon-system-expert` (`claude --agent weapon-system-expert`);
передача от прежней сессии — миграцией сопровождающего протокола (хаб 1795/1797, план 1874): worktree
`F:/CodexWorktrees/weapon-system-expert/Vr_Battlegrounds_ai`, ветка `claude/weapon-system`.

Влиты: B, C, C2, `drive` (5285ceb7), `waves-f` (5af1c7ef), `ejection` (722d14df), `expert-kb` (cd497223).
На машине все стволы арсенала, кроме револьверов и SDK Shotgun (F5). Вылет живого патрона и гильзы, четыре
категории отклика (автомат, дробовик, пистолет, снайперская винтовка), повтор инициализации учёта (чинит стрельбу
в раунде), запрет перехвата своей второй рукой (`ManipulationConstraints.AllowHandTransfer`).

## Архитектура

Чистая машина (`Assets/Scripts/Weapons/Core`) → датчики → порт учёта SDK → исполнители. Полное описание, инварианты,
решения пользователя — база знаний [`Docs/weapons/`](../../Docs/weapons/README.md).

## План

Очередь этапов и риски — [`Docs/weapons/roadmap.md`](../../Docs/weapons/roadmap.md) (единственный источник).
Ближайший — `ejection-manual`: проект и решения владельца — [ejection-manual.md](ejection-manual.md), SDK-патч 69.
Машинный план — [plan.json](plan.json); подробности — [Details.md](Details.md); спецификация машины —
[refactor-plan.md](refactor-plan.md); учёт C2 — [ledger-single-source.md](ledger-single-source.md); журналы этапов —
`changelog/`; скрипты (офлайн-компиляция, claim, продление допуска) — [tools/](tools/Readme.md).

## Синхронизация

- Контракты: `weapon-use-context@1` (владелец мы), `weapon-equipment-binding@1` (bots-fix; foundation ещё не принят),
  `haptics-api@1` (haptics-system).
- Сопровождающий: строки `Docs/weapons` в `AGENTS.md` и `Docs/README.md` — отдельный выпуск после миграции (1494).

## Проверка

C2, `drive`, `waves-f`, `ejection` — шлем пользователя, Android PASS, EditMode (последний: аренда 337, 401 тест,
1 красный вне этапа — бюджет треугольников R08). Подробности — журналы `changelog/`.

## Следующий шаг

Эксперт после миграции: inbox и статус хаба (owner `weapon-system-expert`), закрепить свой `session_id` по
инструкции сопровождающего, удалить локальную ветку `claude/expert-kb-hold` (содержимое уже в origin/dev).
Unity-воркер на обслуживании (1814/1868) — аренды не брать до «worker ready». Затем `ejection-manual`:
`begin` → субагент по [ejection-manual.md](ejection-manual.md) → шлем → тесты → вливание.
