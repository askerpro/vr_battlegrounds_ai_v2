using System.Runtime.CompilerServices;

// EditMode-тесты проверяют внутренний контракт запуска карты (MapRunAuthority, IMapRunHost, MapReferee.InitializeRun)
// напрямую, без рефлексии по приватным полям. Других потребителей internal-API у сборки нет.
[assembly: InternalsVisibleTo("VrBattlegrounds.Tests.EditMode")]
