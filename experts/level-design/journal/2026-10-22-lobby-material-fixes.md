# lobby-material-fixes: материалы паков Lobby, 2026-10-22

Розовые материалы паков RuralTown/PostApocalypticTown (HDRP Lit, шейдера HDRP в проекте нет) переведены фиксером
аудитора `BakedLitMaterialFixer` в Baked Lit: MI_Emissive, MI_TopLight, MI_ElectricPole, MI_Lamp, MI_LampPost, MI_Lamp_02,
No Name. Сцена Lobby не менялась: эти материалы назначены только в FBX, а префабы SM_TopLight/SM_Light2 в Lobby ставят свои
URP-материалы (M_TopLight, M_Emissive, M_Glass_Inst и др.) — рендереров с InternalErrorShader в Lobby 0 (worker, ticket 1011).
Значит, FBX этих ламп, поставленный в сцену напрямую, а не префабом, больше не будет розовым (у «No Name» — белый корпус).

Стекло переносной лампы на верстаке WarehouseZone (M_Glass_Inst, вариант «Light Off») оставлено несветящимся по решению
пользователя: в паке оно не светилось.
