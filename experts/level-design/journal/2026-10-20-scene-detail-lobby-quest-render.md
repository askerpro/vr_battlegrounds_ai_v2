# scene-detail: Lobby на Baked Lit, box-UV и перезапечённый свет шатра, 2026-10-20

Окружение Lobby рендерится дешёвыми шейдерами: пол/бетон/плитка пака MilitaryCamp — URP Baked Lit с box-UV при импорте
(`Assets/Editor/VR_Battlegrounds/Performance/BoxUv/`, конфиг целей пишет только сканер), 91 материал декора — Baked
Lit (`LobbyGpuMaterialTier`, исключает чужие материалы). Грунт (WornGround, OutsideGravel) — единственный писатель
`LobbyDecorationAuthoring.ApplyGroundMaterials`, повторный авторинг не возвращает дорогой шейдер. Свет шатра: пресет
`LobbyNightLighting` — лампы 6 / 10 м, свет в BayLamp_2/4 и Pendant_NW/SW, плафоны Simple Lit/Unlit с эмиссией и GI None
(EmissiveIsBlack в URP выключает _EMISSION); пол шатра — свой меш с UV2 (`LobbyTentFloorLightmap`, общий
nalchik_tolst.fbx не трогается) в лайтмапе. Лайтмапы перезапечены (тикет 925). Тесты состояния —
`Assets/Tests/EditMode/Maps/LobbyQuestRenderStateTests.cs`. Пользователь принял вид в шлеме.
