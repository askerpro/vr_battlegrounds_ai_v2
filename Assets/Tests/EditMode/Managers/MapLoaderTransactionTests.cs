using NUnit.Framework;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Managers
{
    /// <summary>
    /// Транзакция загрузки карты (<see cref="MapLoader.LoadMap"/>).
    ///
    /// <para>
    /// Что доказывает. Разрушительное действие вызывающего (серия изымает снаряжение) выполняется только под
    /// принятую загрузку: отклонённый или повторный во время загрузки запрос его не вызывает и номер загрузки не
    /// меняет. Принятая загрузка держит <see cref="MapLoader.IsLoading"/>; выключенный загрузчик (корутина
    /// остановлена) отменяет её с событием, а не оставляет IsLoading навсегда. Сама смена сцены Mirror здесь не
    /// выполняется: EditMode не крутит корутины дальше первого шага.
    /// </para>
    /// </summary>
    public class MapLoaderTransactionTests : MirrorTestHarness
    {
        [Test]
        public void Отклонённая_загрузка_не_выполняет_разрушительного_действия()
        {
            SilenceMirrorNoise();
            MapLoader loader = CreateManager<MapLoader>("MapLoader");
            int strips = 0;

            Assert.IsFalse(loader.LoadMap("", () => strips++), "Пустое имя сцены принято.");
            Assert.AreEqual(0, strips, "Отклонённая загрузка изъяла снаряжение.");
            Assert.AreEqual(0UL, loader.LoadGeneration, "Отклонённая загрузка получила номер.");
            Assert.IsFalse(loader.IsLoading);
        }

        [Test]
        public void Повтор_во_время_загрузки_отклоняется_без_побочных_эффектов()
        {
            SilenceMirrorNoise();
            MapLoader loader = CreateManager<MapLoader>("MapLoader");
            int strips = 0;
            string started = null;
            System.Action<string> onStarted = scene => started = scene;
            MapLoader.MapLoadStarted += onStarted;
            try
            {
                Assert.IsTrue(loader.LoadMap("MapA", () => strips++));
                Assert.AreEqual(1, strips, "Принятая загрузка не выполнила действие перед загрузкой.");
                Assert.AreEqual("MapA", started, "Принятая загрузка не объявила MapLoadStarted.");
                Assert.IsTrue(loader.IsLoading);
                ulong generation = loader.LoadGeneration;

                Assert.IsFalse(loader.LoadMap("MapB", () => strips++), "Вторая загрузка поверх первой принята.");
                Assert.AreEqual(1, strips, "Отклонённый повтор изъял снаряжение второй раз.");
                Assert.AreEqual(generation, loader.LoadGeneration, "Отклонённый повтор сменил номер загрузки.");
                Assert.AreEqual("MapA", started, "Отклонённый повтор объявил свою загрузку.");
            }
            finally
            {
                MapLoader.MapLoadStarted -= onStarted;
                InvokeLifecycleMethod(loader, "OnDisable");
            }
        }

        [Test]
        public void Остановленный_загрузчик_отменяет_загрузку_с_событием()
        {
            SilenceMirrorNoise();
            MapLoader loader = CreateManager<MapLoader>("MapLoader");
            string cancelled = null;
            System.Action<string> onCancelled = scene => cancelled = scene;
            MapLoader.MapLoadCancelled += onCancelled;
            try
            {
                Assert.IsTrue(loader.LoadMap("MapA"));
                InvokeLifecycleMethod(loader, "OnDisable");

                Assert.IsFalse(loader.IsLoading, "Остановленная загрузка держит IsLoading — следующая не будет принята.");
                Assert.AreEqual("MapA", cancelled, "Отмена загрузки не объявлена.");
                Assert.IsTrue(loader.LoadMap("MapB"), "После отмены новая загрузка не принимается.");
                Assert.AreEqual(2UL, loader.LoadGeneration);
            }
            finally
            {
                MapLoader.MapLoadCancelled -= onCancelled;
                InvokeLifecycleMethod(loader, "OnDisable");
            }
        }
    }
}
