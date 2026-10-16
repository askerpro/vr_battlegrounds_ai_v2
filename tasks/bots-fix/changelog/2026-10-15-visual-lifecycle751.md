# Unity751: второй матч на том же Mind, кадры рук и блокеры приёмки

2026-10-15, bots-fix r53, clip-cutover RUNNING epoch3.

Input `748d8e8493bf48d8155d6a0b374bf88c75794034`, B `018e63d4fcc8c97355165474f0e080abd34aa79a`, ticket751/epoch636.
DONE/receive R=`ef429ee1b99c2c29bbca079b89076091ac03041f`: paths0/unexpected0/binary_invalid0.
82 собственных пути, borrowed SDK0; реальные HEAD/index сохранены. 220 broker artifacts,
52 991 399 байт, SHA256 mismatch0. Копия `../reports/received751/`, включая оба полных launch logs. Своей аренды нет.

## Фактические результаты

- Visual G: old1487/body180 и new2520/body185, session179, MEF Black/AK105, Right grabPoint0/Left1.
  Обе руки каждой пары сняты и заморожены в одном AvatarsUpdated frame; before/after fingerprints совпали.
  10 live PNG и120 fit-render PNG. NeedsReview,38 shots/29 hits/foreign0,29 DamageReceived согласованы с Bind.
  Призрак407 кадров, shots/grabs0. Исторический foreign1 в734 этим не закрыт.
- M2 ServerOnly: четыре roundScored (две победы/две ничьи) → actual Round5 → один adminStop → Warmup → один GoLive.
  Epoch2→3→4, те же sessions/Mind, contextReset и Buy shopToken1 обеих сессий во втором матче, затем Round2.
  Watch completed=true/sameMindProof=true; final probe completed=true/incomplete=false, errors/drops0.
  Director reset6/buy10/offer8/arm10; восемь оплаченных покупок на12100, балансы сходятся.
  Бой M2:8 shots/4 hits/2 kills, всё от Бота2. NaturalEnd этой веткой не проверен; он отдельно наблюдался в747.
- B7 completedObservation=true/incomplete=false. Все10 sampled owned firearm исчезли, максимум1 на сессию;
  полный spawn provenance UNKNOWN. Публичный OutcomeCount найден после751 и добавлен только в будущий template.
- Cleanup: Playfalse/launchIdle/CleanupPassed, request удалён, observers/камеры очищены, guard compile/import/test=false;
  сразу штатные finish/receive. Офлайн7 сборок и visual5/M2 6 C#6 payloads errors0. Новые игровые тесты не создавались.

## Открытые блокеры

- R08:536 Combat samples без WeaponSystem; SeesTarget444, SDK ready/loaded/ammo8 подтверждены, shots0.
  Same-GO host/profile обязателен в BotGunner до LegacyAmmo/SDK shot. Это достаточный блок при достижении gate,
  но точный первый return каждого запроса raw751 не хранит. Canonical weapon-system8563, authoring/SDK не менялись.
- Все4 fit analysis_incomplete, runtimePose/poseAsset и weaponSource GUID неизвестны. ±2мм — лишь review priority,
  не fixed-reference phalanges владельца8388. Возможный пробел применения пальцев в UpdateExternally передан
  avatar-ik8576/grip8578; authored requiredBinding нельзя выдавать за applied pose.
- Performance8491 требует seed/setup receipt до AddBots; запрос8548 владельцу vr-test-stand. Четыре свежие
  Host hybrid/candidate×N8/N10 ячейки ещё не выполнены. SDK81 grants/proofs для Host нужны отдельно.
- Binding r3 получил weapon ACK8581 и owner contract-agree (publication80e62dd6), state=agreed;
  active до принятого вливания не объявлен. Network8363 (S8/Spectator), остальные zone reviews и Unity/headset/Quest-приёмка открыты.
  Pause/reload/Respawn/refusal/invalid-descriptor не подменяются Elimination опытом.

## Evidence

[Галерея](../reports/received751/visual-review.html), [G audit](../reports/cc1-visual-analysis751.md),
[M2 audit](../reports/cc2-lifecycle-analysis751/analysis.md), [будущая cc3 матрица](../reports/cc3-next-package-preparation.md).
Infra8478/8483/8489/8554: B0/B4 timeout, но native SUCCESS54,5928/63,1508с и durable files подтверждены;
replay/recovery не было. Exact tool input/reply/history без токенов — `../reports/751-visual-b0-infra.json`.
Явный timeout>55с не передавался. Accepted code commit/публикация origin/dev и полная приёмка не выполнены.

