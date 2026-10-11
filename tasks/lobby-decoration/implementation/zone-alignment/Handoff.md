# Связь пространственных зон Lobby

10.10.2026. Пакет кода заморожен для интегратора. Main checkout, ветка `lobby-decoration`; свежие guards implementation epoch3 перед записью. Ведущий Unity-интегратор подтвердил окно вне Play/активных операций. Общий Authoring, Lobby.unity, MapRoot, validators, SDK, тесты, commit/push не изменялись этим исполнителем.

## Файлы и SHA256

- `Assets/Scripts/Environment/LobbyZoneLayoutBinding.cs` — `8B6153D32120B1AC5490C3C9479002AF393B506736EB3ECEBDA227C324913866`.
- `Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyZoneLayoutEditor.cs` — `185C22D3BD4579B77BFAE634105B675CDB95A6972BB1C566A7F88489F14660ED`.
- `Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyZoneHierarchyBuilder.cs` — `C94CED7FB032C055600919C2FE37FEB484D1B3AC83F432EE27003985575A8416`.
- Соответствующие новые `.meta`; GUID проверены по Assets, каждый встречается один раз.
- `offline_compile.ps1` в этом каталоге — отдельная компиляция editor и runtime-data пакетов.

## Контракт API

Runtime namespace `VrBattlegrounds.LobbyZones`: компонент `LobbyZoneLayoutBinding` хранит serialized ссылки SourceZone/EnvironmentRoot/GameplayRoot, GroupPlacement[] и последний применённый frame источника. Не имеет runtime Update, ExecuteAlways, OnValidate, RPC или сетевого writer. Методы записи данных существуют только при UNITY_EDITOR.

Editor namespace `VrBattlegrounds.Editor.Maps.LobbyDecoration`:

```csharp
using (LobbyZoneLayoutEditor.SuppressAutomaticSynchronization())
{
    var zones = LobbyZoneHierarchyBuilder.GroupByRoles(decorationRoot, canonicalWorldPivot,
        new LobbyZoneHierarchyBuilder.ZoneAssignment("ArmoryRangeZone", tent, interior, dressing, closeBackstop, northCliffLayers),
        new LobbyZoneHierarchyBuilder.ZoneAssignment("PerimeterZone", sideMountains, forest, perimeter));
    // Остальные функциональные роли передаются отдельными явными assignments.
    // Сохранить собственный prefab. После его инстанцирования получить явные scene Transform ссылки.
    LobbyZoneLayoutEditor.BindAndCapture(gameplayOwner, sceneArmoryRangeZone,
        environmentRoot, gameplayRoot, shootingRangeGroup, /* явные согласованные группы базы/MapShell */ otherGroup);
}
```

Builder работает только с явно переданными descendants собственного decorationRoot, не ищет игровые сущности по строкам. Возвращает dictionary role→Transform. Создаёт `FunctionalZones/<role>`; все новые zone pivots первоначально canonicalWorldPivot/world identity rotation/scale1. Проверяет мировые матрицы всех перенесённых потомков с допуском0,0001. Не распаковывает префабы, не меняет sceneId/Uxr IDs или игровые ссылки.

Binding создаётся на scene объекте внутри Gameplay после установки собственного prefab. Source внутри Environment, scale1. Ведомые группы находятся в Gameplay либо являются явно переданными неинтерактивными ветками Environment вне Source. MapRoot, PhysicalArenaLayout и calibration anchors запрещены. Отказывают cross-scene, ancestor cycles, пересекающиеся followers и другие владельцы тех же групп. Родители ведомых групп имеют world scale1.

Сервис следит за ObjectChangeEvents, Undo/Redo, sceneOpened; один отложенный проход применяет изменение Source к целым groups. События собственных записей подавлены. Отдельная правка ведомого group root не затирается: выдаётся ошибка до явного `CaptureRelativeArrangement(binding)`, доступного также кнопкой Inspector. Редактирование дочерних мишеней остаётся свободным.

`FlushPendingSynchronization()` завершает pending sync. `ValidateScene(scene)` сверяет/применяет только допустимое изменение Source; `ValidateLoadedScenes()` сначала завершает pending sync и используется перед Play. SaveGuard исключает несогласованную загруженную `.unity` из сохранения. IProcessSceneWithReport проверяет baked layout без записей в Build. Прямые методы авторинга запрещены в Play; сервис runtime трансформы не двигает.

## Повторная генерация без двойного сдвига

Простая безопасная интеграция: `RequireCanonicalBeforeRegeneration(oldZoneDictionary, canonicalWorldPivot)` перед изменениями. Она отказывает, если пользователь переместил/повернул/масштабировал зону. Не сбрасывать её молча.

Если интегратор реализует сохранение поз, порядок обязателен:

1. `CaptureZonePoses(oldZones)`; проверить текущие bindings до разрушения старых объектов.
2. В suppression scope: `NormalizeForRegeneration(existingBindings, canonicalWorldPivot)` нормализует источники вместе с их followers; затем `NormalizeZonePoses(oldZones, canonicalWorldPivot)` нормализует чисто декоративные зоны.
3. Генерировать canonical геометрию от неизменённого PhysicalArenaLayout/world floor. По возможности сначала собрать draft, сохраняя старую сценовую композицию до успешной проверки.
4. После нового scene instance выполнить `BindAndCapture` с новыми Source ссылками, пока и source, и followers ещё canonical.
5. `RestoreZonePoses(newZones, savedPoses)`; затем `ValidateScene(scene)` применит captured relativeframes к восстановленным позам.
6. При отказе до замены старого instance восстановить старые poses и ValidateScene в cleanup. Не оставлять нормализацию как окончательный результат неудачной генерации.

Нельзя capture binding после восстановления custom Source: это превращает canonical followers в новые неправильные offsets. Повторная генерация может обновлять внутренний состав объектов по своему действующему контракту; мemento сохраняет именно zone poses.

## Фактические проверки и оставшаяся приёмка

`offline_compile.ps1`: editor exit0/0warnings, `-RuntimeOnly`: runtime-data exit0/0warnings, против установленной Unity6000.4.1f1 API/current project DLLs. Логи: `tasks/lobby-decoration/reports/implementation/zone-alignment/offline-compile-editor.txt` и `offline-compile-runtime-data.txt`. Scoped git diff-check exit0; отдельный rg новых source файлов: trailing whitespace и запрещённые logging/runtime follower patterns —0совпадений.

Эти проверки не подтверждают native compilation, Scene save/Play/build callbacks, Unity Undo integration или gameplay. Новые gameplay tests до пользовательской проверки не добавлялись.

Интегратору проверить: actual Unity compilation; initial grouping world matrices и IDs/serialized refs; один Move/Rotate Source с одним Undo и Redo, включая немедленный Save/Play до delayCall; child target edit; явный отказ direct follower edit и успешный capture; source missing/replaced, cross-scene/duplicate ownership, scale≠1; save/reopen; saved build preflight; canonical regeneration и отказ/preservation custom movedzone. После Play — попадание, падение/возврат мишеней, фото/мониторы, работа арсеналов и standing/facing refs. PhysicalArenaLayout/calibration frames не должны измениться. Визуальная композиция и Quest acceptance остаются у пользователя.
