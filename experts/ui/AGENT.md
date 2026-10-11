# Expert `ui`

VR tablet menu (frame, navigation, MenuKit screens), design system, fonts, wrist-watch HUD, arsenal and
settings views. Consumer of gameplay state (mode, series, economy).

## Owns
- `Assets/Scripts/UI/`, `Assets/Prefabs/UI/`, `Assets/Resources/MenuTheme.asset`, `Assets/Data/UI/Menu/MenuPrefabRegistry.asset`,
  `Assets/Editor/VR_Battlegrounds/UI/`, `Assets/Tests/EditMode/UI/`, `.claude/skills/add-menu-screen/SKILL.md`, `Docs/ui-*.md`.
- Project font and `Assets/ThirdParty/TextMesh Pro/Resources/TMP Settings.asset`; SDK SlimUI, TMP, UltimateXR UI
  (fingertip/canvas; UI part of patches 40/41).
- Views in foreign paths (edits agreed with the path owner): `Assets/Scripts/Arsenal/ArsenalPriceTag.cs`, `ArsenalWalletDisplay.cs`,
  `ArsenalPurchaseTimerDisplay`, `ArsenalCardPrefabBuilder`; «Калибровка» and «Отладка» screens; LaserGridScreensPreview.

## Not owned
- gameplay: economy/arsenal rules, modes, teams, series. network: Command/SyncVar, authority.
- manipulation: tablet grab, haptics. avatar-ik: UxrManager, input, teleport, avatar rig.
- avatar-grip: handle grip poses, `UxrFingerTip` placement on fingers. launch-infra: debug mode.
  performance: Quest budgets.

## Invariants
- A screen is a `Screen_Base` variant, only `Content` changes, built by `MenuKit` from `MenuTheme` tokens:
  no literal colours, custom buttons, 3D text or foreign fonts.
- One frame per tablet (`MenuFrame`), one navigation stack (`MenuNavigation`). `MenuKitBuilder` generates
  `Assets/Prefabs/UI/Menu/Tablet/` `Tablet*.prefab`: never add by hand to `Tablet.prefab`, never recreate `Screen_Base`.
- Admin UI is one rule `MenuPermissions.ShowAdminUi`; it is visibility only, the server checks rights.
- Tabs: declared by the mode (`GameModeData.menuTabs`) plus system tabs, at most 6.
- Font: static Roboto Condensed, never dynamic. One MonoBehaviour per file of the same name.
- UI shows and sends requests; never stores or replicates match state. Watch notifications: local `WatchNotifications.Post` only.
- Scroll, readability, touch: accepted only by the user in the headset.

## Architecture
- `Assets/Scripts/UI/Menu/MenuController.cs` (open/close, tabs), `MenuView.cs` (root), `MenuScreen.cs` (screen
  contract, preview), `MenuPermissions.cs`, `LocalMenuManager.cs` (prefab by device/context via `MenuPrefabRegistry`).
- Kit: `Assets/Scripts/UI/Menu/Kit/MenuFrame.cs`, `MenuNavigation.cs`, `MenuKit.cs`, `MenuTheme.cs`; overview: `Assets/Scripts/UI/Menu/Overview/`.
- Screens: `Assets/Prefabs/UI/Menu/Screens/Screen_Base.prefab`.
- Generation, snapshots, font atlas: `Assets/Editor/VR_Battlegrounds/UI/MenuKitBuilder.cs`, `MenuPreviewTools.cs`, `ProjectFontTool.cs`.
- Watch: `Assets/Scripts/UI/HUD/` (`WatchNotifications.cs`, `WristDisplay.cs`), `Assets/Prefabs/UI/HUD/`.
- Font: `Assets/Art/Fonts/RobotoCondensed/`. Fingertip: `Assets/ThirdParty/UltimateXR/Runtime/Scripts/UI/`,
  `Assets/Scripts/Debug/DebugMode/DebugFingerTipRays.cs`.
- Checks: `MenuDesignRulesTests`, `MenuContainmentTests`, `MenuKitLogicTests`, `MenuWiringTests`, `UiFontCoverageTests`;
  menu `Tools/VR Battlegrounds/UI/Render Menu Snapshots`.

## Details (grep the heading)
- `Docs/ui-design-system.md` «Раскладка», «Токены», «Набор элементов», «Проверки», «Подводные камни Unity».
- `Docs/ui-menu-architecture.md` «Общая схема», «Касание пальцем и диагностика попадания», «Разделы и экраны».
- `Docs/ui-fonts.md` «Как применять шрифт», «Как добавить символ», «Проверка».
- `Docs/troubleshooting.md` «UI-планшет», «Кракозябры»;
  `Docs/UltimateXR/known-issues.md` «После спавна бота планшет перестаёт принимать касания».
- `Docs/Arsenal/Arsenal_Code_Architecture_RU.md` «6. Экономика и покупка».
