# SDK grip preview: деформация как в игре

**Goal:** исправить SDK-превью MEF/AK105 так, чтобы деформация соответствовала штатному применению той же позы на целевой риг, и использовать этот источник для диагностики хвата без фактического удержания.

**Architecture:** существующее Runtime-ядро позы `UxrAvatarRig.UpdateHandUsingDescriptor` и native Unity skinning остаются единственными авторами деформации. Editor-превью получает собственную временную иерархию Transform/SkinnedMeshRenderer без Avatar/SDK components, применяет существующий runtime метод, запекает меш и выбирает кэшированные hand vertices. SDK API preview/proxy/alignment сохраняется. Нового игрового вычислительного ядра и синхронизации двух формул нет.

**Spec:** пользователь требует одинаковое skinning в preview/game, разрешил SDK patch; основная проблема MEF, Cyborg — контроль. Превью должно работать на Grabbable/GrabPoint/side при наличии аватара на сцене, без фактического grab. Gameplay poses/assets не меняются. Источник сравнения здесь runtime-equivalent deformation; Quest/IK/controller acceptance отдельно.

## Пакеты

- [x] Native RED: SDK preview против `UpdateHandUsingDescriptor`+Unity BakeMesh на собственных Transform/SMR. MEF AK105 Grip/Support Left/Right RMS85,6–144,3 мм; Cyborg Fixed/Blend почти совпадал. Mapping0; runtime matrices с legacy weights совпадают, значит причина — source-rig matrices.
- [x] Editor SDK patch `UxrPreviewHandGripMesh.cs`: runtime deformation целевого рига, native skinning; subset/source vertex mapping через совместимый overload MeshExt. Public API/rigid proxy coordinates/mesh identity сохранены, teardown без SDK activation.
- [x] Native GREEN12/12, max0,000323 мм. Постоянные NUnit методы15/15 напрямую вызваны в Editor: normals/scale/blendshape/Refresh identity/source preservation/cleanup. Полный NUnit runner не запускался с dirty сценой. Editor/Android compilation PASS.
- [x] Independent read-only review SDK patch: Critical/Important нет. Обязательные references UltimateXR/UltimateXR.Editor в test asmdef добавлены.
- [x] Документы: SDK patch register/known issues/tool/README/CHANGELOG. Коммит только после человеческой SDK UI-приёмки.
- [ ] Штатная SDK UI-приёмка MEF/AK105 и измерение refresh/navigation времени; проверка Quest и фактического игрового размещения wrist/IK.

Рабочие отчёты: `tmp/hand-fit-sdk-preview-research-20261005/{compare-red.json,compare-green.json,sdk-preview-native-tests.json,android-gate.json}`. Это временные результаты, не архивы под Git. Сам runtime-equivalent skinning проверен; реальные UI/FPS/controller/Quest этой фикстурой не доказываются.

## Контекст анализатора

SDK preview context (avatar GUID resolution, actual instance, Grabbable, GrabPoint, side, pose/Blend, proxy rigid world transform) должен принадлежать одному provider. Live overlay и CPU export читают одну исправленную frozen geometry. SceneView orbit/zoom не требуют нового skinning/hash; invalidation только при изменении pose/rig/target. [Binding/cache срез](2026-10-05-sdk-preview-diagnostics-binding.md) теперь использует единственный SDK HandRenderer на snap и frozen snapshot для overlay/export. Camera-only checks не запускают повторный capture; native проверки зелёные. Пользователь принял MEF preview, новый UI рентгена ещё ожидает проверки.

## Границы

Не создавать/активировать новые UxrAvatar/GrabManager/SDK components, не подменять SDK singleton/UID state, не менять original bones/materials/pose assets. Все fixtures — собственные preview-scene plain transforms/SMR/meshes, cleanup в finally. Не менять Play человека. Исходники в Assets применяются только в Edit Mode под своим lease, native preflight обязателен и проверяется условием до mutation.
