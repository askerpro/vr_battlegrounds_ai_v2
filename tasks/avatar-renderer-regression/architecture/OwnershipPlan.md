# Renderer ownership — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Единый editor-контракт явного владения body renderers для prefab variants.
**Architecture:** Метаданные роли веток; единственный writer существующего списка UxrAvatar.
SDK Explicit policy запрещает непреднамеренный fallback; runtime scanner отсутствует.
**Tech Stack:** Unity 6 URP, C#, UltimateXR, Unity Editor SerializedObject/PrefabUtility, NUnit.
**Spec:** [Design.md](Design.md).
**Статус:** ПРИНЯТ как основа в упрощённом виде решением пользователя 2026-10-12 (событие 4651). Упрощение задачи 3:
staging-префаб, backup/promotion/restore и тесты `FailedStagingLeavesTargetUntouched`/`FailedPromotionRestoresTarget`
не делаются — Validate до сохранения, при ошибке цель не записывается, откат через Git. Реализация — задача
`avatar-renderer-ownership` (worktree avatar-ik) после `legs-ik/pose-set`; перед ней перепроверить ссылки на строки.

## Global Constraints

- Только новый task `avatar-renderer-ownership` и отдельный linked worktree от свежего origin/dev.
- Резервировать scopes до записи; main checkout read-only; preserve foreign diffs.
- SDK patch-reserve при реализации; запись в task changelog, не SDK journal.
- GameLog, русские comments/docs, Editor scripts в Assets/Editor/VR_Battlegrounds.
- До runtime-приёмки не писать/переписывать gameplay/avatar tests.
- После приёмки — закрепить ожидания тестами; accepted commit и только FF integration.
- Не менять haptics, часы/лучи, gameplay visibility или tag policy без владельцев.

## Review Focus

- Explicit пустое PlayerBase не должно снова собирать controllers в OnValidate.
- Неактивные LOD body остаются; часы в MEF LOD не становятся body.
- Nested avatar/hand integration не становятся частью родительского body.
- Variant удаляет inherited body refs: сохранение не оставляет missing references.
- Непомеченный renderer внутри известной модели нельзя молча считать принадлежащим телу.

## Задача 1: согласование роли и границы миграции

**Files:** task Readme.md, plan.json, contracts/renderer-ownership.md, reports/body-map.json.
**Interfaces:** выход — точная mapping renderer→role→owner для каждого registered prefab.

- [ ] Зарегистрировать task/stages по текущему mode, получить admission и scopes.
- [ ] Проверить inbox/routes, договориться с avatar, haptics и SDK owners о metadata и builder файлах.
- [ ] Fetch/rebase origin/dev с сохранением чужих изменений; записать baseline SHA.
- [ ] Подготовить read-only probe mapped prefabs, включая Cyborg, Ghost, MEF, PlayerBase.
- [ ] В probe перечислить resolved renderer refs, nearest avatar/hand owner, LOD membership,
  ближайшие будущие role boundaries, controller/accessory bindings и variant overrides.
- [ ] Для Cyborg ожидать тот же принятый набор 15; отдельно показать 14 auxiliary и 5 Mesh refs.
- [ ] Для MEF выделить body skins и часы даже при общем LODGroup; не использовать имя как правило.
- [ ] Owner утверждает body mapping и intentional empty base; неоднозначность — stop до asset edits.

## Задача 2: единый editor writer и metadata

**Create:** `Assets/Scripts/Player/Avatars/AvatarRendererRole.cs`.
**Create:** `Assets/Editor/VR_Battlegrounds/Avatars/AvatarRendererOwnership.cs`.
**Modify:** `Assets/Editor/VR_Battlegrounds/Avatars/FixAvatarRenderers.cs`.
**Proposed Interfaces:**

- `public enum AvatarRendererRoleKind { Body, Controller, Accessory, Authoring }`.
- `public sealed class AvatarRendererRole : MonoBehaviour`, serialized role;
  `public AvatarRendererRoleKind Role { get; }`. Никаких renderer refs или enabled writes.
- `public static Renderer[] Build(UxrAvatar avatar)` — editor snapshot по ближайшей роли.
- `public static string[] Validate(UxrAvatar avatar, bool allowEmptyBody)` — read-only ошибки.
- `public static void Apply(UxrAvatar avatar, Renderer[] body, bool allowEmptyBody = false)` — sole writer,
  validate candidate first; true передаёт только утверждённый builder пустого PlayerBase.
- Все три метода принадлежат `AvatarRendererOwnership`, `FixAvatarRenderers.Setup` сохраняет signature.

- [ ] Реализовать nearest role lookup с остановкой на avatar root; unknown role исключает candidate
  и диагностируется для модели, обозначенной mapping из задачи 1.
- [ ] Реализовать Build: inactive включены, distinct, foreign/hand/EditorOnly исключены.
- [ ] Реализовать Validate: точное покрытие Body, null/duplicate/foreign/invalid-role ошибки;
  LOD проверять только для renderer с Body ownership, не целиком LODGroup.
- [ ] Apply сначала валидирует candidate, затем SerializedObject записывает один список.
- [ ] В этом шаге SDK Explicit API ещё отсутствует: Setup выбирает новый Build/Apply по наличию
  AvatarRendererRole в своей иерархии; unmarked legacy сохраняет f33 поведение. В задаче 3
  добавить выбор по Explicit policy; нельзя ссылаться на ещё не созданный SDK enum/getter.
- [ ] Проверить compilation и существующие AvatarRendererOwnershipTests без изменения ожиданий.

## Задача 3: SDK Explicit policy и все точки записи

**Create:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/UxrAvatarRendererCollectionMode.cs`.
**Modify:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/UxrAvatar.cs`.
**Modify:** `Assets/Editor/VR_Battlegrounds/Avatars/Workbench/AvatarMaintenanceTools.cs`.
**Modify:** `Assets/Editor/VR_Battlegrounds/Avatars/Workbench/AvatarScopedActions.cs`.
**Modify:** `Assets/Editor/VR_Battlegrounds/Avatars/ExtractPlayerBase.cs`.
**Modify:** `Assets/Editor/VR_Battlegrounds/Avatars/GhostAvatarBuilder.cs`.
**Modify:** `Assets/Editor/VR_Battlegrounds/Avatars/SkinnedMeshTransplant.cs`.
**Proposed Interface:** enum `LegacyAutomatic = 0, Explicit = 1`;
UxrAvatar serialized `_avatarRendererCollectionMode`, getter `AvatarRendererCollectionMode`.

- [ ] Резервировать SDK patch и записать локальную запись задачи.
- [ ] OnValidate: fallback только LegacyAutomatic; Awake не добавляет scanner;
  GetAllAvatarRendererComponents/rig inference сохраняют текущую ответственность.
- [ ] SavePrefab/SaveNew: Validate preview, save temporary staging prefab, reload/Validate staging
  до изменения target. Promotion сохраняет target GUID/variant связи; перед записью scoped backup
  target bytes/meta. При save/import/reload error restore backup и reload-check; failed new target
  удалить вместе с новым meta. Staging/preview cleanup finally; при error не Register.
- [ ] GhostBuilder после Strip перестраивает список; Paint обрабатывает только snapshot body skins.
- [ ] ExtractPlayerBase явно сохраняет Explicit empty body после удаления Cyborg:
  Apply(baseAvatar, emptyBody, allowEmptyBody: true); registered spawnable avatars не получают этот допуск.
- [ ] Transplant заменяет прямую запись kept+created на metadata нового Geo и Build/Apply.
- [ ] AvatarScopedActions renderer action вызывает тот же контракт; NormalizeAndVerify дополняется
  read-only ownership validation без второго writer.
- [ ] Просмотреть diff всех writes `_avatarRenderers`: product writes только Apply;
  SDK fallback остаётся только в LegacyAutomatic.

## Задача 4: migration и runtime acceptance

**Modify:** только prefab paths из approved mapping; исходно
`Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab`,
`Assets/Prefabs/Player/Ghost/GhostAvatar.prefab`;
точный MEF/прочий scope закрепить из registry probe до записи.
**Checks:** существующие AvatarRendererOwnershipTests, GhostAvatarTests,
RegisteredAvatarBodyTests renderer-list cases; AndroidCompileGate.

- [ ] Установить role boundaries и Explicit; сравнить до/после exact body refs каждого prefab.
- [ ] Cyborg не превращать Ghost ветку в EditorOnly; accessories не уничтожать.
- [ ] Собрать Ghost и new avatar в выделенные временные outputs; сравнить повторный build.
- [ ] Проверить prefab reload/OnValidate и intentional empty PlayerBase; cleanup preview scenes.
- [ ] Unity пакет: checkpoint → request → watch-ticket → claim → begin → guard → execute/wait
  → finish → receive; finish сразу после prepared operations, до diagnosis/user wait.
- [ ] Записать фактическую compilation и XML выбранных existing tests; полный suite не заявлять.
- [ ] Если existing test ожидание изменилось, перечислить его и получить runtime-приёмку до правки.
- [ ] Пользователь проверяет Cyborg/MEF/Ghost: spawn, hide/show, LOD distances, controllers/hands,
  часы/лучи, переход Ghost/respawn, local и remote; нет missing refs и двойных visibility writers.

## Задача 5: regression после приёмки и выпуск

**Test:** `Assets/Tests/EditMode/Prefabs/AvatarRendererOwnershipTests.cs`;
`Assets/Tests/EditMode/Prefabs/GhostAvatarTests.cs`;
`Assets/Tests/EditMode/Prefabs/RegisteredAvatarBodyTests.cs`.

- [ ] После подтверждения runtime добавить/обновить `ExplicitEmptyBaseSurvivesOnValidate`,
  `InactiveBodyLodsRemainOwned`, `AccessoryInBodyLodIsExcluded`,
  `NearestRoleOverridesBody`, `NestedAvatarAndHandKeepOwnGeometry`,
  `RebuiltGhostHasNoInheritedDeadRefs`, `TransplantBuildIsIdempotent`.
- [ ] Assertions: точный ref set, empty intentional base, no invalid refs, invariant после reload;
  не проверка равенства всем descendants.
- [ ] `FailedStagingLeavesTargetUntouched` и `FailedPromotionRestoresTarget`: staged invalid refs
  и fault target import/reload; исходные bytes/meta/GUID восстановлены, Register не вызван.
- [ ] Запустить meaningful regressions, existing relevant suite и Android gate через новый worker package.
- [ ] Проверить returned XML/counts и baseline failures; checkpoint не считать accepted commit.
- [ ] Review SDK/asset diff владельцами, user acceptance record, accepted commit.
- [ ] Enforced: merge-request/merge-execute; FF origin/dev, remote SHA proof, без force-push.
- [ ] Rollback при отказе: сохранить отчёт, не выпускать candidate;
  после выпуска использовать согласованный revert всего metadata/policy/builder bundle.

## Self-review

Coverage: один writer, SDK fallback, четыре builders, variants, mixed LOD и acceptance имеют задачи.
Signatures consistent; proposed interfaces не выдаются за существующие.
Тестовые изменения отложены до приёмки; существующие проверки выполняются раньше.
Неопределённые asset paths закрываются bounded registry probe и scope gate задачи 1.
