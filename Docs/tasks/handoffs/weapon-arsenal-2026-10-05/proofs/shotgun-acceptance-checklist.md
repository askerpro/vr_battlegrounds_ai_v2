# Совместная проверка двух дробовиков

Подготовленный перечень, а не отчёт о выполнении. Assets ещё не интегрированы.
Доступность после exact2 migration: existing Arsenal/WeaponInfo выдаёт FABARM `ShotgunReal`
и Herrington из прежних weapon/shell prefab references; generic station/binding/maps/avatars
не изменяются. Explicit FixedTubeChamber profile маршрутизирует existing supply/loadout.
Shell выдаётся существующим magazine path по одной; hidden reservoir не выдаётся в руку.

## Unity / шлем: физическая механика

- FABARM: старт total8, после полного rear и rest готов; один залп9pellets расходует1.
  После выстрела manual pump до настоящего конца назад и полного rest; удержание промежуточно
  не досылает. ForeEnd/Action grab не конкурируют с Visuals. Rear release/regrab не восстанавливает
  старую позицию. MagazineEject не выбрасывает tube.
- Herrington: старт total7, залп6pellets расходует1; обычная automatic chamber preservation,
  empty hold-open, shell insert и физическое закрытие — отдельные переходы.
- Для обоих: partial tube reload сохраняет C/M, каждый принятый shell ровно+1; повтор placement,
  отмена, повтор request, full tube не уничтожают и не удваивают единицу; pending запрещает shot
  через canonical readiness barrier. При empty insertion не делает Reload/prepare автоматически.
- Rear extraction ejects ровно один существовавший C; отсутствие C не создаёт/ejects phantom.
  Общая вместимость M+C никогда не превышает8/7. Partial action/cancel сохраняет принятую ledger policy.
- Shell grab/scale/collider/RB/CollisionDetection/OutOfWorldGuard и loss/drop проверяются в реально
  выбранном аватаре. Avatars source/prefabs не меняются в этой задаче.

## Снабжение и authority

- Начальный reserve32 в cartridge units: одна outstanding shell, затем новая после потребления.
  MaxMagazineCount3 не ограничивает tube снабжение тремя патронами. Возврат same unit в pocket
  не refund; потеря выдаёт новую только из оставшегося finite reserve, не reset.
- Existing owner/trading phases/free price/pocket limits обычных магазинов прежние.
  Warmup/refill и round reset проверить отдельно: пополнение только существующим policy grant,
  все previous outstanding identities учитываются, consumed не возрождается.
- Два origin/два оружия на одной identity: один server commit, проигравший получает refusal
  и resync; никакого optimistic +1. Unknown/чужой holder/cancel/transfer/stale revision refused.
- Snapshot до/после shell admission и выстрела, latejoin и reconnect: C/M/unit-consumed согласованы;
  invalid/partial snapshot не оставляет playable C1/M8. Commit callback/publication fault не refund/retry.
- Batch replay9/6, точные saved orientations, duplicate/stale zero emission; faults с per-shot
  исходами проверяются отдельным безопасным native package. Нативные networking/Quest проверки pending.
- Retirement flag остаётсяOFF до доказательства reliable ordering/netId lifetime. Retained consumed
  units bound32/weapon; обычный round/map cleanup удаляет exact known units, не общий SDK UID registry.

## Устаревшие тестовые ожидания до human acceptance

- `WeaponBalanceTests` lines273–276 требует внешнюю `MagazinePrefab/UxrFirearmMag` с полным
  MagazineSize. Для этих двух shell prefabs ожидание должно стать unit+canonical tube totalcap.
- `MagazineRefillPlannerTests` проверяет ordinary magazine model. Shell-specific конечный reserve32
  после принятия механики покрывается отдельно, ordinary expectations сохраняются.
- `ShotgunPelletsTests` / `WeaponSpreadTests` имеют legacy ProjectileShot/source emission ожидания;
  после acceptance нужны явные ledger batch/replay/outcome assertions. Старые unsafe runtime SRM
  fixtures НЕ используются и не переписываются для обхода NO_GO.
- `AvatarLoadoutTests` / `PrefabCompositionTests` не меняются без avatar scope/test-policy ruling.
  Physics/drop/part-grab/scale existing checks сохраняются для external weapon/shell; hidden tube
  authoring exception документируется после принятия. Permanent gameplay tests сейчас не добавлены.

## Фактически подтверждено

Только read-only native source inventory2, standalone SDK/game/editor compilation0/0/0,
pure admission model RED1/1 и GREEN22/0. Unity integrated compilation, AndroidCompileGate,
native runtime/model equivalence, actual source emissions, two peers, Quest и human acceptance pending.
