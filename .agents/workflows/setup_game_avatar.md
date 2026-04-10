---
description: Подготовка настроенного VR аватара — создание Prefab Variant от PlayerBase, добавление карманов оружия и регистрация в системе выбора персонажей.
---

# Подготовка игрового VR-аватара (Часть 2)

> **Часть 1 (подготовка FBX)** описана в воркфлоу:
> `workflows/setup_custom_avatar.md`
>
> Этот документ — **Часть 2**: превращение настроенного UXR-аватара в полноценного игрового персонажа.

---

## Предварительные условия

- FBX подготовлен по воркфлоу Части 1 (ампутация кистей, torsion bones, eye bones)
- Префаб аватара настроен по шагам 4-5 Части 1 (UxrAvatar, BigHandsIntegration, позы рук)
- `PlayerBase.prefab` существует в `Assets/Prefabs/Player/`

---

## Шаг 1: Создание Prefab Variant от PlayerBase

> **Цель:** Создать новый аватар как вариант PlayerBase, унаследовав все компоненты геймплея.

1. В Project Window: ПКМ на `Assets/Prefabs/Player/PlayerBase.prefab` → **Create → Prefab Variant**
2. Назвать по шаблону: `{ModelName}_Base_Avatar` (например, `Heavy_Soldier_Base_Avatar`)
3. Открыть в Prefab Mode
4. Перетащить подготовленный префаб модели (из Части 1) внутрь как дочерний объект
5. **Скопировать** настройки `UxrAvatar` и `UxrStandardAvatarController` из модели → в рутовый GO варианта 
   *(они уже должны быть эталонно настроены на этапе setup_custom_avatar)*.
6. **Удалить** `UxrAvatar` и `UxrStandardAvatarController` из внутреннего GO модели (чтобы не было дублей).
7. ⚠️ **ВАЖНО:** Компоненты процедурной анимации ног (`Legs Animator` и `LegsAnimatorUxrBridge`) **ДОЛЖНЫ ОСТАТЬСЯ** на внутреннем объекте рига (например, `Heavy_Soldier_Rig_Mask_Winter`), так как они управляют его локальным Animator'ом! Не переноси их на рут.
8. Сохранить префаб

---

## Шаг 2: Сохранение эталонных карманов (один раз)

> **Цель:** Извлечь карманы с настроенными тегами из CyborgAvatar и сохранить как переиспользуемые префабы.
> **Выполняется однократно** — карманы сохраняются в `Assets/Prefabs/Player/Pockets/`.

1. Перетащить на сцену `PlayerControllersCyborgAvatar` (или выделить если уже есть)
2. Меню: **Tools → VR Battlegrounds → Avatars → Save Pocket Prefabs from Selected**
3. Проверить, что в `Assets/Prefabs/Player/Pockets/` появились:
   - `MagazinePocket.prefab` (теги: MagMachinegun, MagGun)
   - `Anchor_Hip_R.prefab` (теги: SideWeapon, Gun)
   - `Anchor_Back.prefab` (теги: BackWeapon, Shotgun, Machinegun)
   - `BackGrabProxy.prefab`

> ⚠️ Если префабы уже созданы — пропустить этот шаг.

---

## Шаг 3: Добавление карманов на нового аватара

> **Цель:** Инстанцировать карманы-префабы на кости нового аватара.

1. Поместить нового аватара на сцену (НЕ в Prefab Mode — для работы Animator)
2. Выделить рутовый GO аватара
3. Меню: **Tools → VR Battlegrounds → Avatars → Add Weapon Pockets to Selected Avatar**
4. Скрипт автоматически:
   - Найдёт кости `pelvis` и `spine` через Humanoid Animator
   - Инстанцирует карманы из префабов (или создаст вручную, если префабы не найдены)
   - Обновит теги если карманы уже существовали (upsert)
5. **Визуально подвинуть** позиции карманов в Scene View:
   - `MagazinePocket` → перед животом/поясом
   - `Anchor_Hip_R` → правое бедро (кобура)
   - `Anchor_Back` → спина (между лопаток)
   - `BackGrabProxy` → совместить с Anchor_Back
6. Применить изменения префаба (Overrides → Apply All)

---

## Шаг 4: Регистрация аватара в системе выбора

> **Цель:** Добавить аватар в AvatarData для отображения в UI выбора персонажа.

1. Открыть `Assets/Scripts/Core/AvatarData.cs`
2. Добавить новый элемент в enum `AvatarType` (если используется)
3. Зарегистрировать префаб в `AvatarRegistry` / `AvatarData` (зависит от архитектуры проекта)
4. Убедиться, что превью-иконка аватара добавлена в UI

---

## Шаг 5: Тестирование

1. Запустить сцену в VR (или через XR Simulator)
2. Проверить:
   - [ ] Руки аватара корректно отслеживаются
   - [ ] Оружие крепится на бедро (Anchor_Hip_R)
   - [ ] Оружие крепится на спину (Anchor_Back)
   - [ ] Магазины кладутся/извлекаются из MagazinePocket
   - [ ] BackGrabProxy позволяет достать оружие рукой из-за спины
   - [ ] Нет клиппинга камеры с мешем головы

---

## Справка: используемые инструменты

| Пункт меню | Назначение |
|---|---|
| `Tools/VR Battlegrounds/Avatars/Save Pocket Prefabs from Selected` | Извлекает карманы из эталона → префабы |
| `Tools/VR Battlegrounds/Avatars/Add Weapon Pockets to Selected Avatar` | Добавляет/обновляет карманы на аватаре |

## Справка: структура карманов

| Карман | Кость | Compatible Tags | maxPlaceDistance |
|---|---|---|---|
| MagazinePocket | pelvis | MagMachinegun, MagGun | 0.1 |
| Anchor_Hip_R | pelvis | SideWeapon, Gun | 0.3 |
| Anchor_Back | spine | BackWeapon, Shotgun, Machinegun | 0.2 |
| BackGrabProxy | spine | — (UxrGrabbableObject) | — |
