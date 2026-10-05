# Рентген и анализ исправленного SDK grip preview

Пользователь принял MEF preview и поручил дальнейшие исправления. Это ограниченный срез существующего Editor-инструмента: binding, кэш и контактные метки; gameplay/аватары/сохранённые позы не меняются.

## Контракт

- Панель выбирает сценовый предмет, конкретный UxrGrabbableObject, GrabPoint и сторону. Текущая редактируемая Fixed/Blend поза читается без сохранения asset; unsaved finger rotations тоже учитываются.
- Геометрия кисти производится исправленным SDK preview core из единственного HandRenderer, затем ставится в rigid snap frame выбранного GrabPoint. LOD других рук/головы не смешиваются. При фактически выбранной иной позе анализируется кандидат текущего окна; это видно в панели/отчёте.
- Один session provider владеет frozen snapshot, report metadata и fingerprint. Live overlay, GPU points и CPU export используют именно этот snapshot. Экспорт не запекает сценовую руку повторно.
- Дешёвый input key отслеживает pose/bones/mesh dirty revision/renderer/blend shape/snap/object transforms/settings. Камера исключена. Capture/mesh hash/topology не повторяются на неизменной геометрии. Явное обновление поддерживает стороннюю mesh mutation без уведомления Unity.
- Контакты сбрасываются вместе с query generation. Старый async readback не может опубликоваться после A→B→A; не требуется постоянный SceneView.RepaintAll готового поля. GPU строит поле/оценивает расстояние; полный CPU-анализ выполняется по кнопке.
- Удаление объекта, Play, reload, закрытие окна освобождают только собственные ресурсы. Подсветка сохраняет включённые renderer, Game camera и чужие property blocks.

## Проверки

- [x] Native RED legacy capture primitive: point0→1 оставлял тот же hand hash/центр, error4,16–4,41м в prefab inputs. Это проверка примитива, не full scene UI; предыдущая actual scene диагностика показала Tick443мс и зазор1,81м. Первый прямой prefab ctor fixture был остановлен guard скрытого HandRenderer, не трактуется как отказ игровой логики.
- [x] Реализация snapshot SDK preview, session cache, GUI selections и generation-safe query.
- [x] Native binding11/11: MEF AK105 Grip/Support Left/Right совпадают с SDK preview текущих bones на snap; единственный HandRenderer. Дополнительно stale asset vs visible bones, unsaved rotation, mesh/LOD revision и camera suppression.
- [x]100 no-change input checks→1capture; median1,628мс/P951,686мс, initial189мс. Query state11/11 (включая A→B→A), invalid-input cleanup5/5, GPU30/30, math37/37, camera ownership14/14 и Android PASS.
- [x] Frozen export: CaptureCount1, fingerprints equal, JSON/25PNG сохранены вне Assets/Git.203 пары0–2мм в read-only prefab current bind-pose fixture; изображения с контактами просмотрены. Это не авторский pose benchmark.
- [x] Независимое ревью: Critical/Important нет; документация обновлена.
- [ ] Человеческая проверка нового UI: MEF/AK105 orbit/zoom, drag fingers, основные/поддерживающие точки; фактические game IK/controller/Quest отдельно.

Рабочие JSON: `tmp/hand-fit-sdk-preview-research-20261005/{binding-red.json,sdk-binding-native-tests.json,binding-state-result.json,binding-lifecycle-result.json,binding-performance.json,binding-export-result.json,binding-android-gate.json}`. Галерея: `UserReports/HandPoseFit/20261005-124640-429-c548def3/index.html`. Не сохранять отчёты/архивы в Git.

## Исполнение

Готовить source patches в tmp, импортировать только Edit Mode под Unity lease после preflight (в том числе SDK-window autosave). Fixtures используют read-only prefab inputs и собственные plain transforms/meshes; SDK components/Singleton/UID lifecycle не активируются. После terminal result/cleanup сразу release. Не коммитить до принятия новых UI изменений.
