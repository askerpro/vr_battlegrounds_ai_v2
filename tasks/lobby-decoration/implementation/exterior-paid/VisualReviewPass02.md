# Внешнее окружение: фактический visual-review pass02

09.10.2026. Через view_image фактически просмотрены все17 PNG из
`reports/implementation/visual-review/pass-02/`:12 nominal player views и5 reference-angle captures.
Capture metadata UTC18:14:26.499; eye player views y1.639926, края z±6.700001,
окна x±7.3/z±6. Эти PNG — реальные native captures ведущего, не concept renders.
В этом аудите нет C#/asset/scene/Unity mutations. Guard свежий и успешный.

## Вывод

Перестановка hero props и снижение горизонта видны. К визуальному принятию внешняя часть
ещё не готова: доминирует пустой нетекстурированный грунт, дальний лес остаётся разреженным,
гребни выглядят полосой/стеной, boundary bags — прямоугольным prototype.
Известное отсутствие Interior-placement не исправлялось и не подменялось внешним декором.

| Проблема/наблюдение | Конкретные кадры | Решение root/owner |
|---|---|---|
| Большая одноцветная коричневая плоскость занимает большую часть внешнего поля; резкий прямой стык с светлым floor, texture только малыми отдельными островками | center-north/south, оба edge-outward, оба window | Общий ground owner: согласовать матовый textured soil/gravel и переход к floor; дополнительные props не решают материал основания |
| Дальний лес читается отдельными небольшими пучками с широкими пустыми просветами; полноценной dense forest layer нет | center-north/south, edge-outward, ref-forest/ref-mountain-composition | Root: сохранить tree budget, сначала перераспределить ширину/глубину far islands и overlap силуэтов; подтвердить новый кадр до наращивания количества |
| Листва бледная серо-оливковая и сильно дробится мелким контрастным pixel noise | east/west-window, north/south-edge-left/right | Материал/LOD/AA owner: проверить фактическую текстуру/tint/alpha и XR MSAA; наличие0 error shaders само по себе цвет/алиасинг не проверяет |
| Новые гребни низкие, крупных валунов больше нет; однако форма пока похожа на широкую серую стену/ступенчатую полосу, её резкое окончание видно сбоку | south-edge-outward/right, ref-forest/ref-forest-low | Root art: решить taper/скрытие концов/атмосферу и видимый layered silhouette; текущий профиль — first pass, не natural mountains final |
| Мешки±Z выглядят как непрерывный прямоугольный бордюр; задние дополнительные мешки — как зубцы, почти без тканевой формы | north/south-edge-left/right, ref-forest-low, ref-desert-composition | Root: либо принять временный CC0 blockout, либо выделить реальный mesh/cloth treatment; не объявлять paid bag quality |
| Передняя+Z линия и маленькие flank cases визуально низкие, не перекрывают открытое поле на уровне глаз | center-north, north-edge-left/right/outward | Сохранить низкий силуэт; shooting rays/hands sweep проверить native geometry отдельно |
| Большой яркий white panel в направлении+Z закрывает заметную часть центрально-правой перспективы перед малыми мишенями | center-north, north-edge-outward, ref-interior | Gameplay/UI owner должен определить объект/назначение и проверить отдельные aim rays; по PNG не называю его ошибочно декором или зеркалом |
| Paid tower и vehicle читаются настоящими военными объектами; tower видна с заднего левого взгляда, vehicle с center-south | south-edge-left, center-south | Хорошие фланговые акценты; source dimensions/grounding/potential tree overlap нужны отдельному native inspector |
| Лагерь−Z распределён между левым/правым флангами, в окнах тоже есть рабочие/складские группы; равномерной одинаковой площадки везде нет | south-edge-left/right, east/west-window | Сохранить распределение; связность улучшать ground/transitions и background depth |

Полные имена файлов таблицы имеют prefix `player-` и suffix `.png`, кроме `ref-*`.
Общая полоса+Z на уровне глаз в основном свободна от наших высоких camp props. Это визуальное
наблюдение, не passed raycast/Quest test: камер нет в руках реального игрока, muzzle poses,
коллайдеры и полный список мишеней по PNG не устанавливаются.

На кадрах north/south outward линия находится у ближнего края кадра; значений z7.48 из pixels
не измерял. Сокращённый шатёр и полосы лучше читаются в reference captures, но фактическое
совпадение границ, размеры2 м и physical safety подтверждаются measurement владельца.
Нет самостоятельного изменения света: лампы/яркие панели уже принадлежат общей сцене.

Приоритет для следующего art pass: ground material/transition → forest silhouettes/material
appearance → natural tapered low horizon → качество boundary bags. Дополнительные случайные
props в центре и перед мишенями не предлагаются. Root принимает арт-решения; текущие факты
не являются финальным пользовательским принятием.
