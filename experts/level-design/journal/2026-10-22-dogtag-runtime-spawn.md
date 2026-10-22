# dogtag-runtime-spawn: жетона в станциях и сценах больше нет, 2026-10-22

Влито `1909c211`. Вшитый жетон удалён из `DogTagPanel`, станций `CommonOpenArsenalStation`/`LobbyDemoArsenalStation`
и 8 сцен (TestMap1–3, ServiceYard, ReferenceMap04, Debug/BotCombatStand, Tools/CommonArsenalReview, Lobby): в сценах
удалены только строки переопределений жетона (`_uxrUniqueId`, `__isInPrefab`, битые `m_AddedComponents` на Identification);
id крючков `DogTagAnchor` остались. Вид жетона (меш, материал, ушко, надпись «ГОТОВ») перенесён из Common-станции в
`Assets/Prefabs/Arsenal/DogTag/DogTag.prefab`; Lobby-станция получила тот же вид.

Инвариант: в сцене и станции жетона быть не должно — правка компонентов жетона (точки хвата) теперь правит один файл
префаба. Коллайдер жетона появляется в рантайме, в запечённый навмеш не попадает. Проверка — `DogTagSetupTests`
(`DogTagPanelsInScenes_HaveNoBakedTag`), видимость жетона на крючке — `ArsenalVisibilityGeometryAudit` по мешам префаба.
