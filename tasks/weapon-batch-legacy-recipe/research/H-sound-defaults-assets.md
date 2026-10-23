# H. Звуковые дефолты — ассеты вместо констант (реализация 2026-10-23)

Решение пользователя 2026-10-23: дефолты звука — данные в ассетах, на которые ссылаются компоненты (или Resources);
подстановка в момент проигрывания. Порядок везде один: свой клип объекта (нарезка пака, `AnchorSoundOverride`,
свои клипы префаба) — оверрайд → дефолт ассета.

Только код, без Unity. Офлайн-компиляция всех сборок и тестов (`offline_build.py --tests`): **код 0, 0 ошибок,
0 предупреждений** в изменённых файлах. Тесты не запускались и не менялись; ассеты не создавались.

## Файлы

Новые (с `.meta`):
- `Assets/Scripts/Interaction/ImpactSoundDefaults.cs` — ассет дефолтов удара (`ImpactSoundKind { Weapon, Magazine }`).
- `Assets/Editor/VR_Battlegrounds/Gameplay/SoundDefaultsCleanup.cs` — `DryRun()`/`Apply()`: снимает с префабов копии дефолтов.

Изменённые:
- Runtime: `Weapons/WeaponSystem/WeaponFeedbackDefaults.cs`, `WeaponEjecta.cs`, `WeaponEjectaPool.cs`,
  `WeaponEjectionExecutor.cs`; `Interaction/AnchorSound.cs`, `ImpactSound.cs`, `MagazineAnchorSoundDefaults.cs` (только комментарии).
- Editor: `Gameplay/HandsPackBuildDefaults.cs`, `HandsPackWeaponBuilder.cs`, `HandsPackAudio.cs`, `ManualLoadingAuthoring.cs`,
  `ImpactSoundInstaller.cs`, `WeaponSystemAuthoring.cs`; `Arsenal/ArsenalBuildPreflight.cs`.
- `KinemationWeaponBuilder.cs` не менялся: у KINEMATION снятие и вставка — всегда свои нарезки, звуковых констант нет.

## Схема: ассет → поля → кто читает и когда

| Ассет | Поля | Читает | Когда |
|---|---|---|---|
| `Assets/Data/WeaponSystem/{Rifle,Shotgun,Pistol,Sniper}FeedbackDefaults.asset` (`WeaponFeedbackDefaults`, ссылка — `WeaponSystem._feedbackDefaults` хоста) | `_magazineInsert`, `_magazineTakeOut` (новые) | `AnchorSound` гнезда магазина и окна приёма патрона (`DefaultInsertClip`/`DefaultTakeOutClip`) | в момент `Placed`/`Removed`/`ItemExtracted` |
| то же | `_ejectaImpacts` (новое, массив) | `WeaponEjecta` без своих клипов; ссылку на массив ставит пул при выпуске (`WeaponEjectaPool.Spawn(..., defaultImpacts)` из `WeaponEjectionExecutor`) | первое касание вылета |
| то же | `_audio._dryFire` (было) | исполнитель хоста — щелчок отказа (как раньше); **сборщик** копирует клип в `_shotAudioNoAmmo` спуска SDK (`HandsPackBuildDefaults.NewTrigger(..., category)`) | отказ — в рантайме; поле SDK — при сборке |
| `Assets/Resources/ImpactSoundDefaults.asset` (`ImpactSoundDefaults`, Resources) | `_weapon`, `_magazine` (массивы) | `ImpactSound` без своих клипов по `_kind` (`Clips`) | в момент удара (после порогов скорости и паузы) |
| `Assets/Resources/MagazineAnchorSoundDefaults.asset` (было) | роли, общий `_insertClip` | `AnchorSound` после категории | в момент события |

Цепочка `AnchorSound`: `AnchorSoundOverride` предмета → свой клип якоря (`source.clip` / `_takeOutClip`) → **для приёмника
боеприпасов ствола** (роль `WeaponMagazine` или `CartridgeIntake.Intake`) — `WeaponFeedbackDefaults` категории по ссылке
хоста `WeaponSystem` (поиск `GetComponentInParent`, без аллокаций) → дефолт роли (`MagazineAnchorSoundDefaults`; окно приёма
спрашивает роль гнезда `WeaponMagazine`) → общий звук вставки (у снятия — тишина). Новые публичные свойства:
`OwnInsertClip`, `OwnTakeOutClip`, `DefaultInsertClip`, `DefaultTakeOutClip`, `IsWeaponAmmoReceiver`; `InsertClip`/`TakeOutClip`
по-прежнему итоговые.

`ImpactSound`: новое поле `_kind` (пишет `ImpactSoundInstaller`); `Clips` теперь **итоговые** (свои, иначе набор вида из
ассета, не null), свои — `OwnClips`, вид — `Kind`. Так же, как `AnchorSound.InsertClip`: тесты читают итог.

`WeaponEjecta`: `ImpactClips` — свои клипы префаба (без изменений смысла), `DefaultImpactClips` (internal) — от ствола.
Префаб вылета общий для разных категорий (гильза 9 мм — пистолеты и MKR9), поэтому дефолт — от выпустившего ствола.

### Почему Resources для ударов, а ссылка для категории

`ImpactSound` — свойство предмета (оружие, магазин, патрон) на десятках префабов без общего хоста; у магазина нет ни хоста,
ни категории. Ссылка в каждом префабе была бы ещё одной копией того же значения и ещё одним полем, которое надо писать и
проверять. Выбор набора — по виду предмета, не по стволу. Это ровно случай `MagazineAnchorSoundDefaults` (та же зона
Interaction, тот же шаблон `Instance`). Для гнезда, гильз и «нет патронов» дефолт зависит от категории ствола, а ссылка на
ассет категории уже есть у хоста `WeaponSystem` — новых ссылок в префабах не нужно.

### «Нет патронов»

Щелчок отказа по патронам уже играет исполнитель хоста из `_dryFire` категории (запасной звук SDK подавлен). Поле спуска
SDK `_shotAudioNoAmmo` звучит только у `BarrelObstruction` (упёртый ствол) и как запасной SDK. Подставить его в момент
проигрывания без правки SDK/`BarrelObstruction` нельзя, поэтому сборщик копирует клип **из ассета категории** (константа
пути удалена). Громкость поля SDK осталась прежней — `HandsPackBuildDefaults.NoAmmoVolume = 0.3` (у `_dryFire` категории — 1).
Нет строки ствола в `WeaponSystemAuthoring.Weapons`, ассета категории или его `_dryFire` — отказ сборки и preflight до записи.

## Сборщик

- `HandsPackBuildDefaults`: удалены `NoAmmoAudio`, `MagazineTakeOutAudio`, `MagazineSocketByCategory`, `MagazineSocketFallback`;
  добавлены `CategoryDefaults(name)`, `CategoryDefaultsPath(name)`; `ApplyAnchorSound` с пустым `takeOut` оставляет
  `_takeOutClip` пустым; `NewTrigger(triggers, hasAction, category)`.
- `HandsPackWeaponBuilder`: гнездо получает только свои нарезки (`InsertAudio`/`TakeOutAudio`), пусто — поле пустое.
- `HandsPackAudio`: отчёт «снятие/вставка магазина — дефолт категории ствола».
- `ManualLoadingAuthoring.ConfigureIntakeSound`: больше не требует итогового клипа снятия у гнезда (хост с ссылкой на
  категорию ставится позже, в том же `ConfigureWeapon`); копирует свои клипы гнезда (может быть пусто). Окно приёма не
  беззвучно: оно — приёмник боеприпасов, `AnchorSound` берёт ту же цепочку, что у гнезда.
- `ImpactSoundInstaller`: клипы не пишет; пишет `_kind`, источник, громкость (0.9/0.6 как было); свои клипы, совпадающие
  с набором вида в `ImpactSoundDefaults`, снимает (так магазин из шаблона при пересборке теряет старую копию). Нет своих и нет
  набора — `GameLog.Error` «падает молча».
- `WeaponSystemAuthoring`: удалены `BrassImpacts`/`HullImpacts` и `EjectaSpec.Impacts`; новый префаб вылета — без клипов;
  добавлен `DefaultsPathFor(category)`.
- `ArsenalBuildPreflight`: входы — ассет категории вместо `NoAmmoAudio`, `MagazineTakeOutAudio` и папки `Audio/SFX/Impacts`;
  `ValidateCommon` требует ассет категории с `_dryFire`.

## Что сделать в Unity (главный агент), чтобы звучание не изменилось

Порядок: **ассеты → пересборка (этап 7) → очистка**. Без ассетов код работает как сейчас: свои клипы префабов звучат,
пустые поля — дефолт роли/тишина.

1. Создать `Assets/Resources/ImpactSoundDefaults.asset` (меню `Assets/Create/VR Battlegrounds/Audio/Impact Sound Defaults`):
   - `_weapon`: `Assets/Audio/SFX/Impacts/IMPACT_Metal_Tool_Drop_Hard_Surface_mono.wav`, `IMPACT_Metal_Rod_Drop_Hard_Surface_mono.wav`,
     `IMPACT_Metal_Objects_Drop_Hard_Surface_01_mono.wav`;
   - `_magazine`: `Assets/Audio/SFX/Impacts/IMPACT_Metal_Soft_Plate_01_mono.wav`, `IMPACT_Metal_Soft_Plate_02_mono.wav`.
2. В четырёх `Assets/Data/WeaponSystem/*FeedbackDefaults.asset`:
   - `_magazineTakeOut` = `Assets/Audio/SFX/Universal/Magazine_drop.mp3` (прежний запасной звук снятия сборщика) — во всех
     четырёх: им звучат гнёзда без своей нарезки (Revolver, FABARM и стволы, у которых `Excursion` магазина не найдёт событие);
   - `_magazineInsert` — пусто (как сейчас: общий `MagazineInsert_Default.wav`);
   - `_ejectaImpacts`: Rifle, Pistol, Sniper — `Assets/Audio/SFX/Arsenal/IMPACT_Metal_Cling_Bright_mono.wav`,
     `Assets/Audio/SFX/Impacts/IMPACT_Bullet_Metal_01_mono.wav`; Shotgun — только `IMPACT_Bullet_Metal_01_mono.wav`.
   Громкость касания осталась в префабе вылета (`_impactVolume` 0.3–0.45).
3. Пересборка арсенала (этап 7): гнёзда без нарезки получают пустые поля; магазины теряют копию набора удара.
4. `SoundDefaultsCleanup.DryRun()` → отчёт (ожидается: 8 префабов вылета — `Clear`; `_kind` магазинов — `SetKind`; копии
   `Magazine_drop` на гнёздах непересобранных стволов — `Clear`; нарезки пака — `KeepOverride`) → `SoundDefaultsCleanup.Apply()`.
   Потом `Normalize Network Asset Ids` не нужен (иерархия не пересобирается), `git diff` — только ссылки на клипы и `_kind`.

Скрипт `execute_code` (CodeDom, без локальных функций):

```csharp
using System; using UnityEditor; using UnityEngine;
using VrBattlegrounds.Interaction; using VrBattlegrounds.Weapons; using VrBattlegrounds.Editor.Gameplay;
string I = "Assets/Audio/SFX/Impacts/";
string[] weapon = { I + "IMPACT_Metal_Tool_Drop_Hard_Surface_mono.wav", I + "IMPACT_Metal_Rod_Drop_Hard_Surface_mono.wav", I + "IMPACT_Metal_Objects_Drop_Hard_Surface_01_mono.wav" };
string[] magazine = { I + "IMPACT_Metal_Soft_Plate_01_mono.wav", I + "IMPACT_Metal_Soft_Plate_02_mono.wav" };
string[] brass = { "Assets/Audio/SFX/Arsenal/IMPACT_Metal_Cling_Bright_mono.wav", I + "IMPACT_Bullet_Metal_01_mono.wav" };
string[] hull = { I + "IMPACT_Bullet_Metal_01_mono.wav" };
var drop = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Universal/Magazine_drop.mp3");
var impact = ScriptableObject.CreateInstance<ImpactSoundDefaults>();
var so = new SerializedObject(impact);
foreach (var pair in new[] { Tuple.Create("_weapon", weapon), Tuple.Create("_magazine", magazine) })
{
    var list = so.FindProperty(pair.Item1); list.arraySize = pair.Item2.Length;
    for (int i = 0; i < pair.Item2.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(pair.Item2[i]);
}
so.ApplyModifiedPropertiesWithoutUndo();
AssetDatabase.CreateAsset(impact, "Assets/Resources/ImpactSoundDefaults.asset");
foreach (WeaponFeedbackCategory c in Enum.GetValues(typeof(WeaponFeedbackCategory)))
{
    var d = WeaponSystemAuthoring.LoadDefaults(c); var s = new SerializedObject(d);
    s.FindProperty("_magazineTakeOut").objectReferenceValue = drop;
    string[] set = c == WeaponFeedbackCategory.Shotgun ? hull : brass;
    var list = s.FindProperty("_ejectaImpacts"); list.arraySize = set.Length;
    for (int i = 0; i < set.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(set[i]);
    s.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(d);
}
AssetDatabase.SaveAssets();
return SoundDefaultsCleanup.DryRun().ToString();
```

## Затронутые тесты (не менялись)

Ожидания, которые теперь проверяют итог с дефолтами (или устарели):
- `ImpactSoundTests.Оружие_и_магазины_реестра_звучат_при_падении` — `Clips` итоговые: после очистки зелёный только при
  заполненном `ImpactSoundDefaults`. После приёмки — проверять `_kind` по роли (оружие/магазин) и непустой набор вида в ассете.
- `AnchorSoundCoverageTests`, `AnchorActivationAudioTests`, `AvatarLoadoutTests.Карманы_звучат…`, `WeaponFeedbackTests`
  (гнездо) — уже читают итоговые `InsertClip`/`TakeOutClip`; теперь в итог входит категория ствола. Зелёные при заполненном
  `_magazineTakeOut` (иначе гнёзда без нарезки — «нет звука выпадения»).
- `WeaponFeedbackTests` (`_shotAudioNoAmmo` спуска) — поле по-прежнему пишется (из ассета категории); стоит проверять
  совпадение с `_dryFire` категории вместо «клип задан».
- `AmmoInsertSoundTests.Гнёзда_магазинов_звучат_общим_звуком` — устарел ещё решением о нарезках (Readme); теперь итог —
  свой → категория → роль → общий. `…MagazineAnchorSoundDefaults с InsertClip` — без изменений.
- `KinemationWeaponTests.Звуки_моно_короткие_не_из_пака` — проверяет итоговые клипы гнезда: если пользователь выберет для
  категории стерео/длинный клип, тест поймает его у стволов без своей нарезки (у KINEMATION нарезки всегда свои).
- `WeaponEjectionTests` — клипы не проверяет; после приёмки — «у префаба вылета нет своих клипов, у каждой категории
  `_ejectaImpacts` непустой» (или свой набор у префаба — оверрайд).
- `WeaponSystemPilotPrefabTests`, `HandsPackWeaponTests` — звуковых ожиданий из этого этапа нет.
- Новые тесты на ассеты (`ImpactSoundDefaults` есть и непустой, `_magazineTakeOut`/`_ejectaImpacts` у четырёх категорий,
  `ManualLoading` окно звучит при пустом клипе гнезда) — после прослушивания пользователем.

## Риски

- **Сеть**: изменений нет. Все три звука локальные: `Placed/Removed` SDK и удары/касания приходят на каждой машине, ссылки и
  ассеты одинаковы у всех (префаб, Resources). Синхронизируемых действий не добавлено.
- До заполнения ассетов пересобранный ствол без нарезки снимает магазин **молча**, а пересобранный магазин стучит, только
  если `ImpactSoundDefaults` есть (без ассета установщик клипы не снимает — копия остаётся). Порядок шагов выше обязателен.
- Щелчок упёртого ствола (`BarrelObstruction`) берёт `_dryFire` категории только при пересборке — правка ассета до него
  доходит после пересборки. Громкость 0.3 против 1 у отказа — как было; уравнять — отдельное решение.
- Окно приёма патрона без своего клипа вставки берёт запасной звук **гнезда магазина** категории (если задан) — семантически
  «магазин встал». У Herrington и FABARM вставка своя (`Herrington_MagIn`, `Reload_1`), поэтому сейчас не звучит.
- `ImpactSoundDefaults.Instance` при отсутствии ассета вызывает `Resources.Load` на каждом слышимом ударе (после порогов
  и паузы 0,2 с) — как `MagazineAnchorSoundDefaults`; с ассетом — один раз.
- Остались звуковые константы вне задачи: `AnchorSoundInstaller` (`PlaceClip`/`TakeClip` — «старая пара» для
  `AnchorSoundRoleDefaultsCleanup`; `MagInClip`/`MagOutClip` — запись якорям без итогового звука); `WeaponSystemAuthoring`
  (`DefaultsSpecs`, `DryFireClip`, `RefusalClip`) — начальные значения при создании отсутствующего ассета категории.
- Документы: `Docs/sound-library.md:47` называет `ImpactSoundInstaller` местом замены клипа удара — теперь
  `Resources/ImpactSoundDefaults.asset`; раздел звуков `add-weapon` SKILL (п. 4 «звуки») — дефолты категории.
- `plan.json` `writes` этапа не включает `Assets/Scripts/Interaction/**`, `Assets/Scripts/Weapons/WeaponSystem/**`,
  `Assets/Resources/**`, `Assets/Data/WeaponSystem/**` — дополнить; ревью зон manipulation (Interaction) и weapon-system.
