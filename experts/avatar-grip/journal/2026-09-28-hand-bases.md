# Базы игровых аватаров разделены по типу кисти: PlayerBase_SdkHands и PlayerBase_NonSdkHands

Коммит `26e94280aa19928a692dcbd19902ffa928970051` (2026-09-28). PlayerBase — сетевой каркас без поз
кисти; PlayerBase_SdkHands (4 кости на палец, позы Controller*/Demo* с киборга, наследник
Heavy_Soldier_Base_Avatar) и PlayerBase_NonSdkHands (3 фаланги, только позы пака, наследник
MEF_Base_Avatar). Родитель варианта меняет `AvatarHandBases.Rebase` переписыванием YAML с
сохранением id объектов.

Проверено по сообщению коммита: слепок всех сериализованных полей до/после — у MEF изменился только
`_parentPrefab`. Добавлен `AvatarHandPoseChainTests`; результат его прогона в источнике не указан —
unknown. Грабли смены родителя — known-issues Issue 24.

Источники: `git show 26e94280`; `Docs/UltimateXR/known-issues.md` Issue 24, Issue 26 (запись на базе кисти).
