---
description: How to generate and update UXR Hand Poses for the custom avatar
---

# UltimateXR Hand Pose Generation

После настройки рига и превращения аватара в префаб, необходимо автоматически сгенерировать позы кистей для новых пропорций костей.

## Шаг 6: Автоматическая генерация поз

Выполни этот шаг через меню Unity:
1. Выдели корневую модель аватара на сцене (например `Heavy_Soldier_Rig_Mask_Winter` или `AutoSetupAvatarTarget`).
2. Выбери в меню: **Tools > VR Battlegrounds > Avatars > UXR Setup Wizard > 6. Generate Default Poses**.

Этот скрипт сделает следующее:
* Прочитает все дефолтные пресеты UltimateXR для поз (Grab, Point, Open, Closed, Default и т.д.).
* Вычислит дескрипторы для каждого пальца на основе текущего рига.
* Сохранит индивидуальные ассеты поз (ScriptableObjects) в папку `Assets/Art/Avatars/<Имя_Модели>/HandPoses/`.
* Добавит эти позы в массив `_handPoses` на компоненте `UxrAvatar`.
* Установит `DefaultHandPoseName` (позу для состояния покоя).
* **Создаст UI-реакции**: Автоматически привяжет созданные позы Pointing и Grab к событиям контроллера (через компонент `UxrStandardAvatarController`), чтобы аватар сгибал пальцы в кулак при нажатии кнопки Grip и вытягивал указательный палец при касании стиков/триггеров.

Убедись, что позы успешно сгенерированы в консоли Unity (ты увидишь сообщение `✅ [6/6] Hand Poses successfully configured!`).
