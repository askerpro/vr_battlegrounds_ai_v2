# Инструменты задачи weapon-system

Корень worktree каждый скрипт вычисляет от своего расположения; выход — в `tasks/weapon-system/reports/` (вне Git).

| Скрипт | Зачем |
|---|---|
| `compile.ps1` | Офлайн-компиляция Core → UltimateXR → VrBattlegrounds → DebugBootstrap → Editor → EditMode-тесты без Unity. Тесты только компилируются; SDK/csproj/Library основного checkout читаются. `-SourceOverlay` принимает точную DEVELOPMENT-зависимость только из своих reports |
| `prepare-binding-overlay.py` | Проверяет immutable exact11 Binding8435c229 и handoff manifest, создаёт только ignored DEVELOPMENT overlay; production foreign paths не пишет |
| `prepare-authoring-input.py` | Проверяет exact19 своих и exact11 owner-approved source blobs против accepted base; `--dry-run` только manifest, обычный запуск создаёт технический Git input через private index без изменения HEAD/обычного index/stash. Не даёт Unity lease и не принимает код |
| `wait-claim.ps1` | Фоновое ожидание предложения тикета брокера и немедленный `claim` (предложение истекает быстро) |
| `permit-renew.ps1` | Фоновое продление допуска этапа, пока пользователь проверяет в шлеме |

Пути к .NET SDK и редактору Unity в `compile.ps1` — машинные; при обновлении Unity поправить `$ns`.
