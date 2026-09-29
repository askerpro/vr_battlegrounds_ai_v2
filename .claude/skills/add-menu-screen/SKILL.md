---
name: add-menu-screen
description: Добавить новый экран (или раздел, или шаг мастера) в меню VR-планшета по дизайн-системе T-32 — префаб-вариант Screen_Base, скрипт-наследник MenuScreen на MenuKit, место в навигации, превью с фейковыми данными, зелёные тесты раскладки. Использовать, когда пользователь просит «добавить экран/вкладку/раздел в меню», «новое меню на планшете», «сделать настройку/мастер в планшете», «показать X в планшете».
---

# Новый экран планшета

Задача: $ARGUMENTS

Дизайн-система и почему она такая — `Docs/ui-design-system.md`; архитектура меню —
`Docs/ui-menu-architecture.md`. Прочитай первый файл, если не работал с меню в этой сессии.

## 0. Где экран в навигации

```
┌─ колонка ─────┬─ контент (скролл лучом и стиком) ──────────────┐
│ разделы       │ Заголовок экрана                                │
│ (≤ 6, всегда  │ …строки, таблицы, плитки…                       │
│  видны)       │                                                 │
│───────────────│                 [второе действие] [ГЛАВНОЕ]     │
│ Назад/Закрыть │                                                 │
└───────────────┴─────────────────────────────────────────────────┘
```

Реши, что это:

| Что | Как |
|---|---|
| **Раздел** (глобальная категория, виден всегда) | Игровой раздел режима — строка в `GameModeData.menuTabs` нужного режима (`Assets/Data/GameModes/`): экран, подпись, видимость (`Everyone` / `AdminOnly` / `DebugOnly`). Раздел для всех режимов по умолчанию — `MenuTabs.DefaultModeTabs()`, системный — `MenuTabs.SystemTabs()`. Всего не больше 6 (`MenuTabsTests`). |
| **Вложенный экран** (провал вглубь) | Кнопка в содержимом родителя → `Push(MenuScreenType.X)`. «Назад» вернёт на один уровень сам. |
| **Шаг мастера** («Далее» → следующий шаг) | Отдельный экран на шаг; на шаге `SetPrimary("Далее", () => Push(следующий))`, на последнем — `SetPrimary("Готово", …)`. Заголовок с номером: «Новая серия · шаг 2 из 3». |
| **Шаги внутри одного экрана** (как «команда → скин») | Один экран, override `HandleBack()` и `HasInnerBack`. |

Глубже 6 уровней — предупреждение в логе: повод упростить.

## 1. Данные — отдельно от отрисовки

Всё, что можно посчитать без Unity, — чистый класс/метод под EditMode-тестом (образцы:
`MapQueue`, `SeriesStatsTable`, `OverviewBuilder`, `MenuMatchManager.PrimaryCommand`). Экран
только рисует. Тест — красный до реализации.

## 2. Скрипт экрана

`Assets/Scripts/UI/Menu/Menu<Имя>.cs`, наследник `MenuScreen`:

```csharp
public class MenuFoo : MenuScreen
{
    private string _lastSnapshot = "";

    public override void Show()
    {
        base.Show();                                 // первой строкой
        SetPrimary("Начать", OnStart, CanStart());   // главное действие — только так, справа внизу
        _lastSnapshot = "";
        Rebuild();
    }

    private void Update()
    {
        if (RefreshDue()) Rebuild();                 // раз в 0,5 с
    }

    public override void BuildPreview()              // превью в редакторе на образцовых данных
    {
        base.Show();
        Render(SampleData());
    }

    private void Rebuild()
    {
        var data = ReadLiveData();
        string snapshot = data.Key;                  // перестраивать ТОЛЬКО при изменении —
        if (snapshot == _lastSnapshot) return;       // пересоздание кнопок сбивает луч в VR
        _lastSnapshot = snapshot;
        Render(data);
    }

    private void Render(FooData data)
    {
        MenuKit.Clear(Content);
        MenuKit.Title(Content, "Заголовок");
        MenuKit.Section(Content, "Группа");
        RectTransform row = MenuKit.Row(Content);
        MenuKit.Label(row, data.Text);
        MenuKit.Button(row, "Действие", OnAction);
        // таблица: MenuKit.TableRow(Content, cells, weights, header: true)
        // плитки:  var grid = MenuKit.Grid(Content, 3, 0.6f); MenuKit.Tile(grid, name, sprite, onClick)
        // пусто:   MenuKit.EmptyState(Content, "Почему пусто")
    }
}
```

**Запрещено** (ловят `MenuDesignRulesTests`): `new GameObject` + свои `TextMeshProUGUI`/`Button`
в экране, 3D `TextMeshPro`, литералы цвета и размера, свои кнопки «Назад»/«Закрыть», кнопки
разделов в содержимом. Цвет — только роль (`MenuColorRole`), размер — только роль
(`MenuTextRole`). Символы — только те, что есть в Roboto Condensed: `·`, `—`, `×`, `−`, `…`
есть; `→`, `‹`, `›`, `✕`, `✓` — нет (пустой квадрат; ловит тест превью).

## 3. Значение в `MenuScreenType`

Новое значение **в конец** enum с явным номером — номера сериализованы в префабах.

## 4. Префаб

```csharp
// execute_code (CodeDom): префаб-вариант Screen_Base + пересборка планшетов
VrBattlegrounds.EditorTools.UI.MenuKitBuilder.CreateScreen<VrBattlegrounds.UI.Menu.MenuFoo>(
    "Screen_Foo", VrBattlegrounds.UI.Menu.MenuScreenType.Foo);
```

Префаб ложится в `Assets/Prefabs/UI/Menu/Screens/`, сборщик кладёт **все** экраны этой папки в
каждый планшет реестра (`Tablet.prefab`). Руками в `Tablet.prefab` экраны не добавлять —
следующая пересборка их удалит. Если нужны ссылки на ассеты (реестры) — `[SerializeField]` на
скрипте, назначить в префабе экрана.

После правки токенов темы или каркаса — `Tools/VR Battlegrounds/UI/Rebuild Menu Frame`.

## 5. Проверка — сам, до пользователя

1. `AndroidCompileGate.Run()` → PASS.
2. `run_tests(EditMode, group_names=["MenuDesignRulesTests","MenuContainmentTests","MenuWiringTests","MenuKitLogicTests"])`
   + свои тесты данных → 0 failed. Экран подхватывается тестами сам (они идут по реестру).
3. Снимок глазами:
   ```csharp
   return VrBattlegrounds.EditorTools.UI.MenuPreview.RenderSnapshots(@"<scratchpad>\menu");
   ```
   → `Read` PNG `Tablet__Screen_Foo.png`. Проверить: ничего не обрезано, текст читается,
   главное действие справа внизу.
4. Если экран — раздел или вход в него ограничен правами — строка в `MenuWiringTests`.

Шлем нужен только для того, что редактор не показывает: скролл лучом/стиком, читаемость на
расстоянии. Это — пользователю, с конкретным списком «что проверить».

## 6. Документация

- Строка экрана в `Docs/ui-menu-architecture.md` (таблица экранов), класс — в `Docs/README.md`.
- `Docs/CHANGELOG.md`.
