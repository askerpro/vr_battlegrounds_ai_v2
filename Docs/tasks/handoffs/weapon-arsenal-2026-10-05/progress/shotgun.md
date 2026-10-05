# Поштучная перезарядка — исполнение

Дата: 2026-10-05. Исполнитель `/root/shotgun_per_shell_implementation`.
Прямое поручение пользователя запускает реализацию сейчас; совместная Unity/шлем приёмка впереди.
HEAD до работы: `55e40873f188c09372411412cf0ba4974a941393`.
Assets ещё не изменялись. Чужие незакоммиченные изменения сохраняются.

## Контракт, переданный root

Только FABARM SDASS (`ShotgunReal`) и Herrington/Remington11-87; общий предел M+C=8/7.
SDK ledger — единственный владелец патронника, SDK ammo store — единственный владелец M.
Принятый shell даёт +1 к M без Reload, смены identity tube store или авто-досылания.
Текущая готовность C сохраняется. Manual extraction, full return и автоматическая подача
используют существующие ledger transitions; один залп расходует один C независимо от 9/6 pellets.
Authority shell и оружия проверяется до commit; другая/неизвестная authority не разрешает совместную загрузку.
Неизменённые legacy SDK shotgun и detachable guns используют прежний путь.

## Архитектурный gate до Assets

Рекомендация: постоянный внутренний SDK UxrFirearmMag как единственный tube M,
отдельный физический shell intake. Это сохраняет нынешние identity/evidence/capture/Controller
contracts. Новый SDK AcceptRound commit пишет M+1 и consumed identity атомарно;
accepted физический shell никогда не становится контейнером M.
Root принял этот permanent SDK store как настоящий единственный reservoir/stable CurrentMagazine.
SDK internal reserve и отдельный game-owned provider не внедряются. Source-wave пока не разрешена:
сначала bounded tmp drafts, standalone compile/model, guards и подготовленный finite Unity lease пакет.

Consumed unit должен иметь собственный сохраняемый consumed marker: список только в weapon
не защищает повторную загрузку того же shell в другое оружие. Despawn/cleanup не выполняется
реентерабельно внутри PlaceObject. Snapshot и silent replay не создают новое acceptance.

## Снабжение

ReserveAmmo=32 — патроны, MaxMagazineCount=3 не превращается в 3 shell.
PlayerLoadoutManager и ArsenalMagazineSupply остаются серверными владельцами выдачи.
Карман/слот предъявляет по одному физическому shell вместо россыпи Rigidbody;
finite reserve сохраняет owner/phase/refill policy. Общие ArsenalPreset/Binding/Builder
не меняются без отдельного согласования. Avatar assets не меняются.

## План волн

1. Root ruling + точный source ownership, SHA и чужой diff.
2. SDK additive ammo/consumed seam и временный scoped serialization/idempotency probe.
3. Game shell/intake policy, явный opt-in capability, отключение tube MagazineEject.
4. Снабжение и exact-addressed producers, два prefab/readiness binding и shell assets.
5. Собственная Unity lease: актуальная compilation, AndroidCompileGate, scoped native
   readbacks/probes с cleanup-before-release. Старые unsafe SRM runtime probes запрещены.
6. Документация и физический checklist, root reviewable handoff.

Постоянные gameplay tests и commit ждут human acceptance. Compile/probe GREEN не считается
проверкой реального ввода, шлема или двух живых клиентов.

## Checkpoint: подготовка вне Assets

- Чистая C# модель скомпилирована: actual RED1/1 для optimistic cross-origin intake,
  GREEN19/0 для serialized-server admission/request barrier и конечного reserve32.
  Выводы: `shotgun-per-shell-admission-model-red.txt` / `-green.txt`. Это модель, не SDK/Mirror proof.
- Подготовленные SDK drafts: `UxrFirearmMag.shotgun.cs.txt`, `UxrFirearmMag.StateSave.shotgun.cs.txt`,
  `UxrFirearmWeapon.Readiness.shotgun.cs.txt`, `UxrFirearmAmmoUnit.shotgun.cs.txt`.
  Полный standalone SDK compilation exit0: `shotgun-per-shell-sdk-offline-output.txt`.
- Game draft: `ShotgunShell.shotgun.cs.txt`, consumed-state read-only projection без synced
  IsGrabbable setter/local despawn. Standalone compile exit0: `shotgun-per-shell-game-offline-output.txt`.
- Draft snapshot ordering НЕ готов для Assets: malformed partial load может загрузить C перед
  отказом M. Root предложен fixed-only atomic firearm snapshot DTO с каноническим mag store;
  ordinary mag serialization прежняя. Только транспорт after-values, не второй live M.
- Root предложен серверный admission без optimistic M: own request barrier, reliable Command,
  строгий sender=current-holder/intake/revision, SDK commit broadcast до retirement, result/resync.
  Existing relay Execute→Rpc без ACK не подтверждает принятие и не исправляет проигравший origin.
  Требуемые narrow dependencies: `StateEventAuthority.cs` exact server admission rule,
  `NetworkStateRelay.cs` public resync request; broad relay rewrite не предлагается.
- Consumed shell retirement: server/offline exact-known unit после Place/commit и reliable
  публикации, на следующем tick; no retained unit refs в firearm snapshot, no unbounded dormant list.
  Actual ordered replay/latejoin/retirement ещё pending.
- Native read-only source inventory двух prefabs сохранён. Дополнительный motion readback
  вернул success=false без отчёта; mutations не было, результат не выдаётся за native proof.

Assets/Unity mutation не выполнялись, Unity lease не захватывалась. Android и physical/native
acceptance нового среза не запускались.

## Checkpoint: полный TMP source package

- Root принял server arbitration admission и fixed-only atomic snapshot. Drafts реализуют
  exact publication firewall, token barrier, sender/holder/unit/intake/revision revalidation,
  committed notification failure и all-ready-peer snapshot recovery. Optimistic writes отсутствуют.
- Полный proposed source package:29 files (6 new), manifest с exact target/base/meta/draft SHA256
  `shotgun-per-shell-source-manifest.json`, diff `shotgun-per-shell-full-source.diff`.
- Game supply использует конечный ReserveAmmo32, одну physical outstanding shell/type,
  no return refund/no endless loss replenishment; ordinary magazines прежние. Retirement defaultOFF,
  finite retention cap32/weapon + existing round/map cleanup. Actual network ordering ещё pending.
- Exact2 authoring migration/preflight подготовлены: capacity8/7, existing shell prefab identity,
  total initial8/7, controller-only native pump travel/action mapping, SDK pump отключён; hidden
  permanent reservoir исключён из unit-only guard/history. Assets всё ещё untouched.
- Root принял TMP generic additional-shot batch seam: readonly author plan, DTO v2/v1fallback,
  max31extras, descriptor/finite normalized rotation prevalidation, one ledger debit/revision/sequence,
  per-shot Emitted/NotEmitted/Indeterminate и saved replay без local producer/reroll/retry/refund.
- Fresh SDK/game/editor standalone compile: exit0/0/0 (после актуального batch и authoring guard).
  Harness учитывает ВСЕ nested asmdefs, новый foreign BotCombatStand не смешивается с game assembly.
  Pure admission model RED1/1/GREEN22/0; это model, не SDK/native/network proof.
- Prepared safe actual SDK nesting RED: `shotgun-pellet-native-red.cs.txt`, root review pending,
  без Unity Object/UID/SRM/source emission/Play. Negative/fault ordering в
  `shotgun-pellet-batch-contract-and-oracles.md`. Actual native projectile/replay/fault cases pending.
- Следующий gate: root exact source review + own finite lease wave GO. Source compile/Android,
  native authoring/physical/network/headset acceptance и документация текущей принятой механики
  остаются работой до готовности; permanent tests/commit пока запрещены.
