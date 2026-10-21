# scene-detail-followups: дверные лампы Lobby, 2026-10-22

LobbyNightDoorLamp не светилась: CopyEmissive ставил EmissiveIsBlack, URP снимал _EMISSION, ярус материалов scene-detail
перевёл лампу в Baked Lit. Исправлено фиксером аудитора `EmissionOffFixer` (Simple Lit без бликов, RealtimeEmissive,
карта T_Door_lamp_EM); `LobbyNightLighting.CopyEmissive` теперь ставит RealtimeEmissive. Пресет не переписывает материал
дверной лампы после создания; материалы окружения на Baked Lit ведёт `BakedLitMaterialFixer` (QPM-SHADER-007).
Тест `LobbyQuestRenderStateTests.LampMaterialsGlowWithoutGiAndHighlights` покрывает обе лампы.
