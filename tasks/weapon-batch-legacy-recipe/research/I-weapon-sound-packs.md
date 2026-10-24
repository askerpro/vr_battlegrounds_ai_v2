# I. Паки звуков оружия с нарезкой по событиям (веб-исследование, 2026-10-24)

Цель: заменить у оружия из Hands Weapons Animations Pack (у каждого ствола один выстрел и одна целая
перезарядка) звуки на заранее нарезанные файлы по событиям: выстрел с хвостом, затвор назад, затвор вперёд,
магазин вынут, магазин вставлен, сухой щелчок; для помпы ещё цевьё назад/вперёд и патрон в трубку; для
болтовой снайперки цикл затвора. Классы: пистолет (PPK, Hi-Power, револьвер), ПП (Uzi, MP5K), автомат
(AR-15, SCAR-L), снайперская винтовка (AX-50 bolt и auto), помпа (FABARM SDASS).

Ничего не покупалось, не скачивалось как ассет, никаких входов в аккаунты.
Метки: **[стр.]** — проверено на странице магазина или в опубликованном списке файлов;
**[вывод]** — моё предположение, не проверено.
Цены Asset Store взяты из данных страницы (`originalPrice`, `finalPrice`, `discount`) на 2026-10-24;
у всех проверенных паков Asset Store скидка 0 %.

## Кандидаты (лучшие первыми)

### 1. Gamemaster Audio — Gun Sound Pack
- Asset Store: https://assetstore.unity.com/packages/audio/sound-fx/weapons/gun-sound-pack-59420 ;
  сайт: https://www.gamemasteraudio.com/product/gun-sound-pack/ ;
  список файлов: https://drive.google.com/file/d/0B5dvEuuBmNhIOEdVX29DN2ZPcVU
- Цена: $19, скидки нет [стр.]. Pro Sound Collection за $47 включает этот пак и другие [стр.].
- Лицензия (EULA сайта, редакция от октября 2026) [стр.]: коммерческое использование, без атрибуции,
  неограниченное число проектов. Лицензия на одного пользователя: каждому, кто работает с исходными
  .wav напрямую, нужна своя. Если покупать в Asset Store, действует EULA Asset Store.
  Запрещено обучать ИИ на этих звуках и загружать их в онлайн-ИИ-сервисы.
- Формат: WAV, 48 и 96 kHz, 16 и 24 bit, обе версии в комплекте [стр.]. 266 звуков оружия и 150 бонусных.
  Загрузка в Asset Store около 54 МБ [стр.].
- Что есть в файлах (имена из списка) [стр.]:
  - пистолет: `gun_pistol_shot` ×5, `remove_mag` ×6, `insert_mag` ×5, `slide_fast` ×6, `slide_slow` ×6,
    `cock` ×7, `dry_fire` ×6, `load_bullet`, `safety_switch`, `general_handling` ×10;
  - револьвер: `shot` ×4, `cock` ×5, `cylinder_open`, `cylinder_close`, `cylinder_spin`, `dry_fire` ×5,
    `load_bullet` ×5;
  - ПП: `submachine_auto_shot` (одиночные, `first`, `last_with_tail`, `tail_only`), `magazine_unload`,
    `magazine_load`, `cock`, `dry_fire`;
  - автомат: `rifle_*` и `semi_auto_rifle_*` (shot, `first`, `last_with_tail`, `tail_only`), `magazine_load`,
    `magazine_unload`, `cock`, `dry_fire`;
  - снайперская: `rifle_sniper_shot` ×4, `cock` ×4, `dry_fire` ×2, `load_bullet` ×5, `scope_zoom_lens`;
  - дробовик: `shotgun_shot` ×4, `cock` ×5, `load_bullet` ×8 (патрон в трубку), `dry_fire` ×2, `pickup`.
- Пробелы:
  - `cock` — это, по-видимому, полный цикл затвора или помпы, а не отдельные «назад» и «вперёд» [вывод];
  - отдельной слайдовой пары «назад/вперёд» нет;
  - у снайперской нет файлов магазина;
  - нет звуков падения оружия и магазина и нет кобуры.
- Сколько хвоста у одиночных выстрелов, по списку не определить. У автоматического огня
  хвост вынесен в отдельный файл [стр.].

### 2. WOW Sound — Snipers Pack 01 и Rifles Pack 01 (лучшая нарезка, но дорого)
- Snipers: https://wowsound.itch.io/snipers-sound-effects-pack-01 (также https://wowsound.com/p/snipers-sound-effects-pack-01/)
  - Цена: от $59, скидки на странице нет [стр.]. WAV 48 kHz, 16 bit, стерео, 351 звук, 44 МБ [стр.].
  - Модели: Remington 700, Savage 110, Barrett M99 (болтовые), PSL и Tiger Dragunov (самозарядные).
    Тип затвора отнесён по самим моделям, страница его не указывает [вывод].
  - События [стр.]: Charging Bolt Back, Charging Bolt Forward, Charging Bolt Down, Bolt Up,
    Empty Bolt Open/Close, Chamber Check Handle Pull/Push, Mag In, Mag Out, Dry Fire, Loading Bullet,
    Trigger Pull/Release, Selector, Bipod, Weapon Grab/Pickup.
    Это единственный найденный пак с проверенной раскладкой цикла болтового затвора.
- Rifles: https://wowsound.itch.io/rifles-sound-effects-pack-01
  - Цена: от $59 [стр.]. WAV 48 kHz, 16 bit, стерео, 801 звук, 173 МБ [стр.].
  - Модели: AK-12, HK G3, LMT M4 (аналог AR-15), Marlin 1894, M1903A3.
  - События [стр.]: Charging Back, Charging Forward, Bolt Release, Lock Bolt Open, Mag In, Mag Out, Mag Slap,
    Dry Fire, Trigger Pull/Release, Selector, Weapon Grab/Pickup.
- Лицензия: «WOW Sound Game License», текст не прочитан, сайт не ответил по таймауту [вывод: для игр
  коммерческое использование разрешено].
- Пробелы: у WOW Sound нет паков пистолетов, ПП и дробовиков (проверен список паков на itch);
  нет падений и кобуры. Про хвосты выстрелов страница не пишет.

### 3. Weapons Foley SFX (только фоли, дешёвый)
- Asset Store: https://assetstore.unity.com/packages/audio/sound-fx/weapons/weapons-foley-sfx-253902 ;
  Fab (Mechanics Mechanics, есть формат Unity): https://www.fab.com/listings/2815fc87-3429-4d50-9a61-4aa1ccb47911 ;
  список файлов: https://drive.google.com/file/d/1f0K7-CSVpdvJ-C0OeiH0Gqe1Xo57C9oC
- Цена: $4.99, скидки нет [стр.]. Лицензия: EULA Asset Store или Fab Standard [стр.].
- Формат: WAV 96 kHz, 24 bit, моно [стр.]. 141 файл, около 13 МБ [стр.]. Выстрелов нет [стр.].
- Список файлов [стр.]:
  - пистолет: `PullOutTheClip` ×5, `Insert_The_Clip` ×5, `Shutter` ×13, `Insert_Ammo`, `EmptyClipShells`;
  - винтовка: `PullOutTheClip` ×6, `Insert_The_Clip` ×6, `Shutter` ×9;
  - снайперская: `Clip` ×3, `Insert_Ammo` ×8, `Shutter` ×9;
  - дробовик: `Insert_Ammo` ×7, `Shutter` ×5, `Empty` ×2;
  - револьвер: `Open`, `Close`, `Insert_Ammo` ×16, `No_AmmoInClip` ×2, `Trigger` ×4;
  - гильзы: `Shells` ×8.
- Пробелы:
  - `Shutter` не помечен как «назад» или «вперёд»; возможно, в вариантах есть оба движения [вывод];
  - сухой щелчок есть только у револьвера (и, видимо, `Shotgun_Empty`);
  - отдельной категории ПП нет.

### 4. Cyrex Studios — Gun Handling Sound Pack (фоли AR, AK, пистолета)
- Asset Store: https://assetstore.unity.com/packages/audio/sound-fx/weapons/gun-handling-sound-pack-272684 ;
  itch: https://cyrex-studios.itch.io/gun-handling-sound-pack ;
  Fab: https://www.fab.com/listings/eb882fe3-6fba-4f46-a068-18d1e8825bfc
- Цена: $9.99 на всех трёх площадках, скидки нет [стр.]. 439 звуков, WAV 44.1 kHz, 16 bit, стерео,
  около 31 МБ [стр.].
- Лицензия:
  - на itch и в описании на Fab указано CC BY 4.0 с обязательным указанием автора «Nathan Gibson» [стр.];
  - при покупке в Asset Store действует EULA Asset Store, атрибуция, вероятно, не нужна [вывод].
- События [стр.]: Magazine Load/Unload, Slide Pull/Release (раздельно), Weapon Cocking/Bolt Release,
  Hammer Pull/Drop, Trigger Pull (подходит как сухой щелчок), Safety, Raise/Lower,
  Pistol Holster/Unholster, Vest Magazine Pull/Return, Full Reload.
- Пробелы: нет дробовика, снайперской, ПП и выстрелов.

### 5. Reload! (Guy Cockcroft)
- https://assetstore.unity.com/packages/audio/sound-fx/weapons/reload-19674
- Цена: $10, скидки нет [стр.]. 73 МБ, версия 2015 года [стр.]. Формат на странице не указан.
- Модели: .44 Magnum, .357 revolver, Assault Rifle, Beretta, Desert Eagle, FAL, Lee Enfield (болтовая),
  SPAS-12 (помпа), Uzi. У каждой модели полный набор фоли (reloading, handling, chambering, bolt action)
  в версиях от первого и третьего лица; выстрелы для улицы и помещения [стр.].
- Насколько фоли нарезаны по событиям, не проверено: списка файлов нет [вывод: вероятно, нарезаны].
  Пак старый.

### 6. Medartimus — Huge Pack of Real Firearm Sounds Effects
- Asset Store: https://assetstore.unity.com/packages/audio/sound-fx/weapons/huge-pack-of-real-firearm-sounds-effects-28536 ;
  Fab: https://www.fab.com/listings/e70b3806-7629-4905-a58f-e6c204d68641
- Цена: $34.99 в Asset Store, $44.99 на Fab [стр.]. WAV 44.1 kHz, 16 bit, 618 файлов, около 92 МБ, версия 1.6 от 2025-03 [стр.].
- 35 стволов; у каждого 5 одиночных выстрелов, очереди, reloads, magazine eject и insert, handling,
  гильзы; отдельно рикошеты и попадания по поверхностям [стр.].
- Пробелы: затвор и сухой щелчок не упомянуты, конкретные модели не перечислены.

### 7. Catastic — Weapon Sound Pack (по одному стволу или томами)
- Пример страницы: https://assetstore.unity.com/packages/audio/sound-fx/weapons/weapon-sound-pack-smg-mp5-146521 ;
  все 36 стволов: https://assetstore.unity.com/packages/audio/sound-fx/weapons/weapon-sound-pack-volume-1-2-3-4-5-6-182061
- Цена [стр.]: один ствол $4.99, том $16.99, все 36 стволов $90.99 (836 МБ). Стерео 44 kHz.
- Среди моделей [стр.]: Uzi, MP5, Scar, M16, Remington 870, Spas-12, Arctic Warfare, AS50, Barrett,
  Colt Python и другие.
- Состав [стр.]: выстрелы, гильзы, handling, reloading, aiming. У MP5 отдельно «Reloading Clip» и
  «Cocking Clip» [стр.].
- Насколько нарезано по событиям, не проверено [вывод].

### 8. Magic Sound Effects — Real Guns Sound Pack 2 (только как источник выстрелов)
- Asset Store: https://assetstore.unity.com/packages/audio/sound-fx/weapons/real-guns-sound-pack-2-231826 ;
  itch: https://magicsoundeffects.itch.io/real-guns-sound-pack-2
- Цена: $29.99 / $30 [стр.]. WAV 44.1 kHz, 16 bit, моно и стерео [стр.].
- У каждого из 18 стволов выстрелы с хвостом и без [стр.].
- Перезарядка — 2 целых файла на ствол и 1 «пустой магазин», то есть та же проблема, что у HWAP [стр.].
- Есть 50 файлов падения и подбора оружия [стр.]. Это единственный проверенный источник падений.

### 9. PlaceHolder — Gun Sounds Pack Vol 1
- Asset Store $15: https://assetstore.unity.com/packages/audio/sound-fx/weapons/gun-sounds-pack-vol-1-289021 ;
  itch и Fab: $9.99.
- 74 выстрела, 53 фоли («reloading, ammo, handling, pumping, dry fire»), WAV 44.1 kHz, 16 bit, стерео, 13 МБ [стр.].
- Фоли по классам не разложены, список файлов на страницах не открылся.

### Бесплатные варианты (дополнение, с ручной нарезкой)
- **Sonniss GDC bundles** (https://sonniss.com/gameaudiogdc):
  - Лицензия [стр.]: коммерческая, без атрибуции, без ограничения проектов; нельзя перепродавать и обучать ИИ.
  - По трек-листам [стр.]:
    - 2016: Pole Position «Various Gun Foley & Handling» (G36C mag in & out одним файлом, SAIGA cocking,
      S&W M29 cylinder, Steyr TMP cocking & dry fire), TS Sound «12 Gauge Shotgun» (Shell Load, Quick Pump);
    - 2020: Colt M1911A1 handling и dry fire, K98k и Springfield 1903A3 bolt-action handling, Walther P38.
  - Это сырые записи по несколько дублей в файле, нужно резать; в бандл входят только выборки из библиотек.
  - Состав GDC 2026 (7.47 ГБ, 17 поставщиков) не проверен.
- **Fab SoundLab1**:
  - «Free Weapon Sound Effects», $0: AR, пистолет, дробовик, reload mechanics, handling [стр.];
  - «Realistic Gun Sounds & Reloads», $14.99: 36 стволов, «28 reload mechanics (136 separate audio files)» [стр.].
  - Оба доступны только в формате Unreal (.uasset); для Unity звук пришлось бы экспортировать через UE.
  - Можно ли использовать контент Fab Standard License в Unity: на Fab EULA получен 403, не проверено
  [вывод: стандартная лицензия Fab не привязана к движку].
- **Freesound CC0** [стр.]:
  - NoonerBear «Taurus G2c Handgun Foley» (Dry Trigger Pull, Holster, Magazine Release, Slide Racking):
    https://freesound.org/people/NoonerBear/packs/33129/ ;
  - shelbyshark «Weapons» (racking, загрузка дробовика, 9mm Firing Empty Chamber, заряжание Mosin):
    https://freesound.org/people/shelbyshark/packs/28138/ .
- **itch violencelat gunset1** (цену назначает покупатель, «no credit needed»): bolt cycle, mag in/out,
  bolt release; 1 МБ, нарезка грубая [стр.]. https://violencelat.itch.io/gunset1

### Отброшены
- Modern Weapons ALL-IN-ONE ($349.99): только выстрелы и гильзы, фоли перезарядки нет.
- Epic Stock Media Game Weapons: на itch $96, раньше со скидкой $57.60, акция до 2025-06-15; сайт отдал 403,
  нарезку проверить нельзя.
- War & Weapons (Fab): только Unreal, список событий не дан.
- Humble Bundle: сайт закрыт для чтения, в поиске нет активного звукового бандла с оружием; не проверено.
- Распродажа Asset Store: на 2026-10-24 ни у одного проверенного пака скидки нет.

## Покрытие наших событий (лучшая комбинация)

| Класс | Выстрел | Затвор назад / вперёд | Магазин из / в | Сухой щелчок | Особое |
|---|---|---|---|---|---|
| Пистолет | GMA | Cyrex Slide Pull/Release; GMA slide (цельный) | GMA, Cyrex, WFSFX | GMA, Cyrex trigger | Кобура: Cyrex |
| Револьвер | GMA | — (курок: GMA cock, Cyrex hammer) | Барабан: GMA open/close, патроны: load_bullet | GMA | — |
| ПП | GMA SMG | GMA cock (цельный) [вывод] | GMA | GMA | — |
| Автомат | GMA rifle / semi | Cyrex Cocking / Bolt Release; WOW Rifles Charging Back/Fwd | GMA, Cyrex, WFSFX | GMA | — |
| Снайперская bolt | GMA sniper, WOW | Только WOW Snipers: Bolt Up / Back / Fwd / Down | WOW Mag In/Out; WFSFX Clip | GMA, WOW | — |
| Дробовик-помпа | GMA | Помпа: GMA cock и WFSFX Shutter (цельные, назад/вперёд не разделены) | — | GMA | Патрон: GMA load_bullet, WFSFX Insert_Ammo |
| Падения | — | — | — | — | Гильзы: WFSFX Shells; оружие: Real Guns 2 (Foley drop) |

## Рекомендация
- **Оптимум за ~$34**:
  - Gamemaster Gun Sound Pack ($19) — выстрелы и магазин, затвор (цельный), сухой щелчок по всем шести классам,
    патрон дробовика;
  - Cyrex Gun Handling ($9.99, брать в Asset Store, чтобы не связываться с CC BY) — раздельные затвор/слайд
    «назад» и «вперёд» для пистолета и AR, кобура;
  - Weapons Foley SFX ($4.99) — дополнительные варианты, магазин снайперки, гильзы.
- **Если болтовой затвор AX-50 должен быть разложен по фазам**: добавить WOW Sound Snipers Pack 01 ($59).
  Это единственный проверенный пак с Bolt Up / Back / Forward / Down и Mag In / Out.
  Rifles Pack 01 ($59) нужен только ради ещё более качественного AR (M4).
- **Чего не будет ни в одном варианте**:
  - помпа не разделена на «назад» и «вперёд»: GMA `shotgun_cock` придётся резать по щелчку
    (или проверить, что лежит в WFSFX `Shutter`);
  - нет звуков падения оружия и магазина: только гильзы. Падение оружия есть в Real Guns Sound Pack 2 ($30)
    или во free-записях;
  - нет тембров конкретных моделей (PPK, Hi-Power, Uzi, FABARM): звуки общие по классу;
  - сколько хвоста у одиночных выстрелов GMA, проверить можно только прослушиванием (превью на страницах).
