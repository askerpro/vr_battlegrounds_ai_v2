# Причины отказов и ограниченный ремонт

2026-10-19, user_instruction9786: исправить связанные тесты и публиковать готовое после независимых ревью. Это разрешение заменяет прежний запрет commit/merge/push, но не принимает новое игровое поведение.

Воспроизведение до ремонта: native ticket825/job de4ea1b6cc7741988477e3285cc37e0c, 135 результатов:120passed/15failed/0skipped. Полные ошибки сохранены в reports/test-paths; повтор для доказательства уже наблюдавшихся отказов не нужен.

## SDK: актуальный fixture

SdkHandPreviewParityTests сравнивает BuildForAvatar с runtime-применением avatar.GetHandPose; SdkPreviewBindingTests проверяет capture/cache, обе точки и AK105-позы. Числовой эталон исторического NonSdkHands rig не зафиксирован. Оба класса созданы f29fedc2014cb9acfe26acdc5fba9a0d6fef91a7; Optimized_MEF_Player отсутствует в прежнем корне после архивирования. Выбран зарегистрированный MEF_cyborg_hands_Player с CyborgHands, а не retired archive.

MEF prefab наследует CyborgHandsBase GUID71b74b0d08444f49bbb7aa2828d0890a; AvatarData OptimizedMEF.asset:17 ссылается на него. Общая база регистрирует runtime AK105 Grip/Support GUIDa5d40ee130b373d4eb6e46a55ecf2ea3/a8f0c12373c44a14581020cf060e1d22, AK105.prefab:1132–1136,1171–1175 содержит записи этой базы и align обеих рук. Mesh GUIDe9b83f62dc5c4612b997ef16b30b8396 — MEF_CyborgHands.fbx. Проверка пригодности fixture требует фактического нового прогона, особенно scaled/blendshape и VisibleBonesOverrideStaleSavedPose.

## Исходная семья pack poses

HandsPack/Kinemation сохраняют NonSdkHands-фильтр и Assets/Art/HandPoses inputs. Их29asset-registration cases прошли в825; шесть геометрических параметризованных методов не имели аргументов, потому что активных потомков этой семьи нет. Менять их на Cyborg означало бы другой контракт. Использован местный LegsGroundingTests:117–147 pattern: один явно Ignored sentinel при отсутствии применимой семьи. Отсутствующий PoseBase или пустой весь registry остаются ошибками discovery; реальные pairs никогда не получают Ignore. Результат непроверенной геометрии будет skipped, не PASS.

## Ghost: потерянные removal overrides

В 9c79ccb580fb1d79a78dcc2dec245ea9ff01e775 Ghost потерял3removedComponents и16removedGameObjects; до коммита удалялись карманы/прокси,11Hitbox и legacyGhost. В актуальном Ghost оба списка пусты, overrides enabled/active/isTrigger отсутствуют. GhostViewEffect управляет постобработкой/вибрацией и не выключает коллайдеры. PlayerBase содержит PlayerLoadoutManager и MagazineEjectInput; Ghost наследует их. Поэтому assertions не ослабляются: нужен native census и восстановление прежнего Strip-контракта по GhostAvatarBuilder:88–119, без Build/Register/Paint и без касания registry/managers.

## Controllers: существующая геометрия головы

localDisabled пуст ещё до layout: changelog hand-rig-quality/2026-10-15-tests-683.md:16–19. UxrMirrorAvatar:86–88,109–111 выключает у локального владельца именно этот список. Модель CyborgGeo.fbx GUID8948537ed54bfaa43b07445dec8fd311 содержит отдельные HelmetGeo (2750 исходных FBX vertices), VisorGeo(352), HelmetInteriorGeo(973). Действующие nodes — Cyborg/CyborgGeo/*; одинаковые meshes под неактивным Cyborg/Ghost не являются target. Нужна native-проверка действующих renderer/mesh IDs, затем заполнение списка только этими3GO. Rig Head и всё тело не отключаются; список body-renderers не расширяется.

Это реальные asset corrections. Технический PASS не заменит UserQA локального/удалённого тела, невидимой своей головы и отсутствия у призрака физики/снаряжения. Коррекции готовятся в той же задаче; пол остаётся bots-validation.
