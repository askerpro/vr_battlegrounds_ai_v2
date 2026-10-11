# Read-only shortlist более широких SilverFir

09.10.2026. Только external source reads и own reports. Ни Catalog Unity, ни source/main assets
не изменялись;104 дерева и frozen code SHA из ForestDiagnosis.md сохранены.

Имена проверены actual discovery: RuralTown содержит L_01/L_02/XL_01; L_03 prefab/mesh не найден.
В AbandonedFactory actual stems имеют prefix `SM_`: SM_SilverFir_01..06, а не SilverFir_01..06.
Source binary FBX аудит9 вариантов плюс текущий M_01 завершён без ошибок.
Все варианты имеют material connection order [Bark,Atlas]; actual native import/submesh order
новых source ещё должен проверить главный owner. Raw triangle counts — source, не measured GPU.

## Что preview первым

**RuralTown SM_SilverFir_L_02** — первый кандидат для более широкой кроны mid/far.
Raw canopy width/height .587 против .486 у M_01 (+21% relative silhouette width).
Это доказательство более широкой геометрии, не окончательной visual density/green quality.
Есть4 LOD и используются уже импортированные Bark/Atlas MAT GUIDs.

- Prefab: `RuralTown/URP/Art/Prefabs/SM_SilverFir_L_02.prefab`, GUID `abc00000000010080006360725859281`.
- FBX: `RuralTown/URP/Art/Scanned_Foliage/Foliage/SilverFir/Meshes/SM_SilverFir_L_02.fbx`, GUID `abc00000000017255173111900428372`.
- Materials: Bark `abc00000000002588266041845012720`, Atlas `abc00000000016780034756723253600`; reuse current copies.
- Source thresholds .45/.14/.04/.02. LOD tris21887/10943/5472/2735.

Он дороже M_01, даже последний LOD2735 против1477. Default source thresholds не совпадают
с текущей четырёхуровневой политикой M_01: нельзя заявить, что в100 м автоматически будет
дешёвый последний уровень. Сначала один bounded native preview с corrected slot mapping,
height8–13 и individual LOD/alpha counts; затем малое число replacements и согласованная LOD policy.

**AbandonedFactory SM_SilverFir_06** — второй кандидат только для низкого широкого understory
возле боковых views, не для массового дальнего леса. Raw width/height .919, source raw height
≈358 units против M_01≈784; native units/scale проверяет owner. Один mesh6730 tris, LOD нет.
Возможны2–4 replacements текущих деревьев как более низкие растения после native art approval,
без автоматического масштабирования всей формы до8–13 м и без увеличения общего количества.

- Prefab: `AbandonedFactory/Art/Prefabs/SM_SilverFir_06.prefab`, GUID `abc00000000016159412300961956038`.
- FBX: `AbandonedFactory/Art/Meshes/SM_SilverFir_06.fbx`, GUID `abc00000000005545601074396987417`.
- Atlas MAT GUID `abc00000000016028126587231151397`, Bark `abc00000000017556882785772424354`.
- Source Atlas использует current-package URP/Lit GUID933532a4fcc9baf4fa0491de14d08ed7,
  BaseColor(.674,.792,.475), Cutoff.02, Cull2. Это зеленоватый tint, но single-sided foliage
  требует проверки/согласованной own-material адаптации при взгляде с обеих сторон.

Factory atlas `Textures/T_SilverFir_01_D.PNG` фактически открыт: та же тонкая приглушённая
хвоя/ветки по виду, не новый плотный leafy texture. Поэтому изменение source geometry/material
не является обещанием photoreal forest; сравнить native после текущего bug fix до новой копии.

## Остальные варианты / бюджет

| Source | Source LOD tris | Raw max width/height | Решение |
|---|---|---:|---|
| Rural L_01 | 24308/12154/6077/3039 | .420 | Уже и дороже L_02 при одинаковом output height; не первый |
| Rural L_02 | 21887/10943/5472/2735 | .587 | Первый bounded preview |
| Rural XL_01 | 46530/23265/11633/5817/2908/1455 | .448 | Только optional large far hero после отдельной LOD policy |
| Factory01 | 53566, no LOD | .447 | Не массовый лес |
| Factory02 | 33193, no LOD | .286 | Не массовый лес |
| Factory03 | 30844, no LOD | .417 | Rural L_01 имеет сходный canopy и готовые LOD; не первый |
| Factory04 | 25301, no LOD | .588 | Rural L_02 имеет сходную широкую форму и готовые LOD; предпочтительнее |
| Factory05 | 11221, no LOD | .482 | Почти текущая форма M_01, без LOD; улучшения плотности не доказаны |
| Factory06 | 6730, no LOD | .919 | Малое число low understory replacements |

XL_01 prefab GUID `abc00000000010816312911577100658`, FBX `abc00000000005833297445747185463`.
Source prefab имеет6 levels с thresholds .63/.16/.06/.04/.03/.01 и LOD size51.057926.
Последний1455 tris не означает такую стоимость в80–100 м: после уменьшения высоты там
могут выбираться промежуточные уровни; native scale1 ещё дороже. Кроме того, текущий Tree()
специально пересобирает4 M_01 levels — перед использованием XL нужна новая root-approved
поддержка всех6 уровней, иначе LOD4/5 останутся вне группы. Пока XL не подключён.

## Evidence и следующий gate

`reports/implementation/exterior-paid/fir-variant-<stem>-audit.json` —9 source reports:
material names/order, triangles, material polygon counts, raw geometry extents.
Parser `audit_fir_fbx.py` не импортирует source scripts и не пишет Catalog/assets.
Source MAT/Texture refs read only; main selective copy/native preview делает единственный owner.
Первый выбор: L_02. Второй optional: Factory06 near understory. Никакого удвоения104 M_01,
новой самостоятельной копии source или tree count change этим исполнителем не выполнено.
