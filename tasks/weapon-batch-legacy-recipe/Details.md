# Детали: weapon-batch-legacy-recipe

## Исходный список (2026-10-23)


Источник: пользователь (AskUserQuestion, «ты и есть другой агент») и сессия «Ворктри и ветка по хватам»
(native канал uds ...98cbfe15..., в хабе не зарегистрирована). Разбиение согласовать с пользователем.

A. Herrington: сборщик KINEMATION кладёт патрон в гнездо вложенным экземпляром `Herrington_mag.prefab` (не развёрнутой
   копией), сохранив якорь, `WeaponInternalAmmoAlignment.ConfigureAnchor`, ссылки, id, overrides; затем убрать
   `GrabPoseCoverageTests.…(Herrington, CyborgHandsBase)` из known-red.
B. AR15 и FabarmSDASS на полный рецепт Hands без доноров (промпт пользователя): звуки/механика/подсветка из пака,
   хваты KeepCurrent, `_orig`, BrowningHiPower — на усмотрение (не назван). + патрон Fabarm в гнезде вложенным
   `FabarmSDASS_Ammo.prefab`, убрать `…(FabarmSDASS, CyborgHandsBase)` из known-red. Удалять ли донор-ассеты — спросить.
   Пользователь проверяет звуки и стрельбу в Play/шлеме перед вливанием.
C. Пункты «1-6» от сессии хватов:
   1. Убрать кнопки-наследие вкладки «Взаимодействие»: «Обновить механические анимации KINEMATION»
      (`KinemationFixReview.Apply`) и «Обновить внутренний патрон двух дробовиков»
      (`WeaponInternalAmmoAlignment.ApplyCurrentShotguns`) — если полная пересборка делает то же.
   2. `_orig` для деталей механики: затворы BrowningHiPower, PPK, Viper, помпа FabarmSDASS — поза руки из клипа
      перезарядки/взвода, snap `Orig/<…>_Action_orig/Left|Right`; схему имён и пути сообщить сессии хватов.
   3. TR15: экспорт не нашёл окно «магазин вынут и неподвижен» — разобрать пороги/клип, экспортировать настоящую
      `Kinemation_TR15_Magazine_orig` и snap Orig в `TR15_mag`.
   4. Нет `_orig`: AR15 Support, FabarmSDASS Grip — должны появиться после B.
   5. Чистка записей и папок `Grabs/MEF_Base_Avatar/...` (GUID b6fe59db941fa944696ece5e1aabc032) во всех префабах
      оружия и магазинов, с align-узлами, если никто не ссылается (AR15.prefab overrides `__prefabGuid` на узлы
      AR15_Magazine — учесть); `WeaponGripCarryOver` не должен их возвращать. Тесты: GrabPoseCoverage,
      CyborgGripEntriesOwnership, AvatarGripRegistration, PackMagazineStructure.
   6. `BrowningHiPower_Magazine`: вторая точка хвата (`_additionalGrabPoints[0]`) с двумя пустыми записями —
      выяснить, нужна ли; предложить пользователю убрать или сделать нормальный хват (пустые записи сейчас
      делают магазин зелёным в покрытии).
Работа mag-grip-button уже в origin/dev (a2c2f7e51, d32f9ef83): записи хвата CyborgHandsBase во всех магазинах —
переносить как есть.

## Передать сессии «Ворктри и ветка по хватам» (2026-10-23, native-адрес устарел, в хабе не зарегистрирована)

Схема `_orig` деталей механики: вид точки = имя граббабла детали у корня `Slide`/`Pump`; поза
`Assets/Art/HandPoses/Orig/<Пак>/<Пак>_<Предмет>_<Slide|Pump>_orig.asset`; snap `<оружие>/<Slide|Pump>/Orig/<поза>/Left|Right`
(внутри граббабла, `FindSnap(grabbable, "Slide"|"Pump", side)`); вторая рука — зеркало по боковой оси корпуса.
Регистрация: поза `<Пак>_<Предмет>_Slide_Grip`/`_Pump_Grip`, задание в `Jobs` для граббабла `root.Find("Slide"|"Pump")`, точка 0.
Плюс: MEF_Base_Avatar выводится полностью; у BrowningHiPower_Magazine убирается вторая точка хвата.

## Пересечение с arsenal-combat-lineup

Ветка `task/arsenal-combat-lineup` правит HandsPackWeaponBuilder, WeaponRebuildFinish, WeaponInteractionReview,
WeaponSystemAuthoring, IWeaponModel, HandsPackWeaponImporter, WeaponInfo-ассеты (Revolver, TR15, CurrentGameplayArsenal).
Сообщено: inbox event 12231 + native. Сериализованные ассеты (WeaponInfo, префабы оружия) — после её вливания.
