# UltimateXR — VR UI (Интерфейс пользователя)

> Источник: `Assets/ultimate-xr/Docs/guides/ui-interaction.md`  
> Скрипты: `Assets/ultimate-xr/Runtime/Scripts/UI/`

---

## Ключевые классы

| Класс | Назначение |
|---|---|
| `UxrPointerInputModule` | Заменяет стандартный Unity EventSystem модуль для VR |
| `UxrCanvas` | Добавляется на Canvas для поддержки VR-взаимодействия |
| `UxrLaserPointer` | Лазерный указатель с руки для нажатия кнопок издали |
| `UxrFingerTip` | Прямое касание UI кончиком пальца |

---

## UltimateXR полностью совместим с Unity UI и TextMeshPro

Даже существующий UI без VR работает через UltimateXR без правок — достаточно настройки сцены.

---

## Два способа взаимодействия с UI

| Способ | Компонент | По умолчанию в аватаре |
|---|---|---|
| Прямое касание пальцем | `UxrFingerTip` (на дистальной кости указательного пальца) | ? Включён |
| Лазерный указатель | `UxrLaserPointer` (на ForwardLeft/ForwardRight) | ? Выключен |

---

## Обязательная настройка сцены

```
1. GameObject ? UI ? EventSystem  (создать в сцене)
2. Добавить UxrPointerInputModule на объект EventSystem
3. Включить "Auto Enable On World Canvases" ? все World-канвасы настроятся автоматически
```

Если Canvas создаётся в рантайме — добавить `UxrCanvas` вручную:
```
Canvas (GameObject)
    ??? + UxrCanvas  ? обязательно для динамических Canvas!
```

---

## Включение лазерного указателя

В иерархии аватара:
```
BigHandsIntegration
    ??? ForwardLeft
    ?   ??? [LaserPointer GameObject] ? активировать
    ??? ForwardRight
        ??? [LaserPointer GameObject] ? активировать
```

---

## Параметры UxrLaserPointer

| Параметр | Описание |
|---|---|
| `Hand` | Какой контроллер управляет лазером |
| `Click Input` | Кнопка для нажатия на UI элементы |
| `Enable Laser Input` | Кнопка включения лазера |
| `Enable Laser Button Event` | Тип события кнопки (нажатие/удержание) |
| `Optionally Enable Object` | Объект, который активируется вместе с лазером |

---

## Настройка UxrFingerTip (кастомный аватар)

- Добавить `UxrFingerTip` на GameObject у дистальной кости указательного пальца
- Направить **forward вектор** в сторону нажатия (слегка вниз — естественный угол касания)
- UltimateXR фильтрует неестественные углы касания автоматически

---

## Важные замечания

> ?? Частая ошибка: забыть добавить `UxrCanvas` на Canvas-префабы, создаваемые в рантайме.

> ?? `UxrPointerInputModule` должен быть добавлен именно на **EventSystem** GameObject, не на Canvas.
