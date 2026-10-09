# Инструменты задачи weapon-system

Корень worktree каждый скрипт вычисляет от своего расположения; выход — в `tasks/weapon-system/reports/` (вне Git).

| Скрипт | Зачем |
|---|---|
| `compile.ps1` | Офлайн-компиляция Core → UltimateXR → VrBattlegrounds → Editor → EditMode-тесты без Unity (csc из .NET SDK, ссылки — из csproj и Library основного checkout, только чтение). Проверка перед арендой и перед `verify` |
| `wait-claim.ps1` | Фоновое ожидание предложения тикета брокера и немедленный `claim` (предложение истекает быстро) |
| `permit-renew.ps1` | Фоновое продление допуска этапа, пока пользователь проверяет в шлеме |

Пути к .NET SDK и редактору Unity в `compile.ps1` — машинные; при обновлении Unity поправить `$ns`.
