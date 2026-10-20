# Аренда 471 (2026-10-12) — первый прогон тестов разделителя

Вход A 5a6a340d6 = B 2c3148767 + SightSplit/** + тесты + SightAdjustablePart.cs(.meta); R 38403e30e accepted, paths пусто; receive выполнен.
- Компиляция: ошибок консоли 0, сборки VrBattlegrounds.SightSplit.Editor и VrBattlegrounds.Tests.EditMode.SightSplit загружены.
- Тесты VrBattlegrounds.Tests.EditMode.SightSplit: 70 шт., status failed. Падения только у стволов пака Hands (MP5K, Uzi, Revolver, Gun, Scar):
  1) имя меша в разборе = имя узла (Base/Gate/Scar_Detail_02), фактическое — Rifle04_Body_Mesh/Gate_mesh/... → сверять по GUID+localFileId;
  2) меши FBX пака имеют bindposes (скин-атрибуты), отказ «скинированный» — для MeshFilter это не важно → снимать скин-атрибуты при копировании.
  Kinemation (AK105, Mk14, Viper, R08, Herrington) и PPK падений в списке нет.

# Аренда 477 — повтор после правок (localFileID, снятие скин-атрибутов)
Вход A d371e946b (B 2c3148767), R 8714a78f9 accepted, paths пусто; receive выполнен.
- Консоль: ошибок 0. Тесты VrBattlegrounds.Tests.EditMode.SightSplit: 69/70; упал Table_CoversAgreedWeapons (Herrington: подвижных 0 — в части разбора нет флага movable).
- Plan (сухой прогон, plan-477.txt): все 11 стволов без отказа; Δ точки к разбору ≤ 0,001 мм. Найдено: у Scar вынесена вся рамка мушки (острова 2,1,0,3,4)
  вместо столбика 2. Исправлено в генераторе: подвижные острова берутся из movable.islands уровня ствола; тест «деталь меньше корпуса».

# Аренда 485 — финальный прогон
Вход A 86bcb811c (B c8f31097d + файлы этапа), R 6e196440e accepted, paths пусто; receive выполнен. После тестов открыта Assets/Scenes/Offline.unity.
- Консоль: ошибок 0. VrBattlegrounds.Tests.EditMode.SightSplit: 70/70 passed (7,9 с).
- Plan по 11 стволам без отказов (plan-485.txt): вынос только подвижной детали; Δ точки к разбору ≤ 0,001 мм.
