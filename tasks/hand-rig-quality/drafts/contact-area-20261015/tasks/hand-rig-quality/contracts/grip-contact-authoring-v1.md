# Контактный авторинг хвата — v1

2026-10-15. Пользователь отклонил R2 для продакшна и прямо поручил реализацию IK субагенту.
Назначение: редакторский авторинг и измерение обхвата по фактической деформированной коже.
UltimateXR остаётся единственным runtime writer; solver не сохраняет production ассеты.

## Площадь контакта

Шесть фиксированных материальных областей: palm, thumb, index, middle, ring, little.
Разметка не зависит от предмета и расстояния; Source/renderer, submesh и original triangle
определяют грань. В первой версии UI выбирает целые грани. Weight означает равномерную долю
площади, не геометрический вырез внутри грани. Правки сбрасывают Reviewed.
Binding fingerprint включает mesh GUID/local id/dependency hash, сторону, LOD и topology keys.
Avatar dependency hash не подходит: включает pose assets. Stale/missing/empty не превращаются в0%.

Основной процент — площадь размеченной кожи в окне расстояния ±2мм / текущая размеченная площадь.
Выводятся также fixed-reference-area-weighted процент, площадь контакта в мм², текущая/reference
площади, зазор, глубина/площадь проникновения, неоднозначность знака и противоположность нормалей.
Fixed reference weighting защищает objective от искусственного улучшения растяжением кожи.
Unknown sign не означает outside. Площадное интегрирование имеет явный бюджет/погрешность.
ReviewedMask/ReadyForComparison не означают принятие хвата. Автоматической нормы100% нет.

## Source и IK

Opt-in capture снимает detached BakeMesh/обе доступные руки/palm/distal/weapon assembly и provenance
после sample до уничтожения source clones. Без callback прежний путь неизменен. Live Transform/Bones
не переживают callback. SemanticSourceSide отделяет исходный хват от другой руки поддержки.
Mirror маркируется inferred; асимметрия и attachments проверяются на фактическом предмете.
Контактные цели не считаются валидными без material patch/correspondence. Uniform scale учитывается
ровно один раз; nonuniform/negative в первом срезе отклоняются, normals inverse-transpose.

Первый IK фиксирует ладонь/запястье/пястные перенесённого prior, решает независимые bounded DOF.
Пределы задаёт caller/profile; длины костей, bindposes, масштабы и runtime frame order неизменны.
Каждый кандидат применяет q от точного q0 к temporary clone и заново проверяет BakeMesh.
Набор material anchors/регионов фиксирован между оценками. Full-skin validator включает также кожу
вне attraction-mask, ладонь/фаланги, self/other-hand, весь weapon assembly и controls clearance.
Targets/coverage, collision feasibility, joint limits и reliability разделены: один хороший pad point
или рост coverage не компенсирует нарушенные ограничения. Unreliable candidate — только preview.
Явные TargetsReached/Solved/RequiresReview и бюджет; impossible не ослабляет пределы скрыто.
NoContact/PreservePrior покрывают пальцы без ожидаемого касания. Palm optimization — отдельный
fallback после доказанной недостижимости. После Solve нужно Apply(q), затем CaptureDescriptor:
обычный GripPoseEncoder.Encode восстанавливает prior. Сохранение — только штатным writer позже.

## Проверка

Managed compile/ad hoc math не заменяют Unity/BakeMesh/игровую приёмку. Сначала paired preview
пользовательского хорошего и плохого хвата с точными prefab/avatar/side/grab point/pose GUID.
Показывать mask overlay, baseline transfer, curl712, IK, одинаковые камеры и все ограничения.
Картинки до/после встраиваются в отчёт; static и runtime_after_sdk_update не смешиваются.
Новые формальные игровые тесты — после проверки человеком; accepted commit — после принятия.
Анимация курка отложена. R2/R3–R5 массовый Apply не продолжается автоматически.
