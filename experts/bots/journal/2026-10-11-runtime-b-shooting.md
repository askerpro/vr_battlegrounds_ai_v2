# Прогон B: боты стреляют из пистолета, винтовке нужно наведение ствола

2026-10-11, задача `bots-fix`, этап armed-development (технический DEVELOPMENT, не приёмка).

- Дефект `__prefabGuid` закрыт: `BotClipComposition.StampAssetIdentity`; ClipBody после authoring и Play
  неизменны, результат побайтно равен каноническому (ticket413, R `e61a5813`).
- ticket413 (B): движение, экипировка, подготовка, темп клипа, strafe/повороты — PASS у MEF и Cyborg.
  PPK стреляет реальными SDK-выстрелами; AK105 почти нет.
- ticket415 (B2): единый луч `BotGunner.TryGetMuzzleForwardHit`, замер углов дула, исправлен отброс
  событий наблюдателя стенда. Выстрелы подтверждены цепочкой SDK (source-emitted, ledger, ShotSequence,
  патроны). Остаток ошибки дула винтовки ≈+5° вверх/+2° вправо в Idle/Shoot — нужно наведение.
- Zone ACK: manipulation — только DEVELOPMENT, 8 условий до merge (3449); gameplay/weapon-system ждём;
  у network нет адреса в хабе.
- Блокер: способ наведения ствола выбирает пользователь.
