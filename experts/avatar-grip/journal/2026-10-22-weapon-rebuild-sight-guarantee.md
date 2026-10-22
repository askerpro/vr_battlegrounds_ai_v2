# weapon-rebuild-sight-guarantee: перенос хватов при пересборке, позы и snap-точки `_orig` магазинов, 2026-10-22

Сюда перенесена оружейная часть работы сессии «Ворктри по хватам»: подсветка островами меша, `PackOrigPoses` и
`Docs/grip-extraction.md`. Перенятые решения — в `tasks/weapon-rebuild-sight-guarantee/Details.md`.

Для зоны:
- `WeaponGripCarryOver.Restore(…, droppedNodes, droppedAvatars)`: пропускает записи аватаров-сэмплов SDK и записи,
  чьи пути проходят через удалённую геометрию базы. Ручная доводка хватов Cyborg (dev) сохраняется.
- Точку хвата Cyborg на магазин не переносим. У магазина — такая же snap-точка `Grabs/Orig/<поза>/Left|Right`, как у
  оружия. Позы магазина экспортируются вместе с остальными (`HandsPack_<ствол>_Magazine_orig`).
- `PackOrigPoses.WriteSnaps` пишет через `LoadPrefabContents`/`SaveAsPrefabAsset` от устоявшегося вида. Сверка YAML
  допускает только объекты Orig. Хвостовая пустая строка последнего документа больше не даёт ложного «изменён».
- Окно арсенала: «Экспортировать позы рук» и «… (все стволы)».
- Известные красные: `GrabPoseCoverageTests` у магазинов Cyborg — до отдельной регистрации поз.
