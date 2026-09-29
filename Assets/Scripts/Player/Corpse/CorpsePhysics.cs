using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// С чем сталкивается труп (T-35): только со статичным миром — слои <c>Default</c> и <c>Ground</c>.
    ///
    /// <para>
    /// <b>Почему не с игроками.</b> В VR игрок ходит по реальной комнате: коллайдер трупа его не
    /// остановит, а толкание рэгдолла телом у каждого клиента своё — трупы разъехались бы. Пули
    /// тоже проходят насквозь: маска попадания оружия — <c>Default|Ground</c>, слоя трупа в ней нет.
    /// </para>
    ///
    /// <para>
    /// <b>Хитбоксы аватаров</b> лежат на том же <c>Default</c>, что и геометрия карт, — слоями их не
    /// разделить. Поэтому труп при появлении выключает столкновения с телами игроков попарно
    /// (<see cref="Corpse"/>), а замёрзнув, выключает коллайдеры совсем: к возрождению (закупка)
    /// трупы из боя давно лежат.
    /// </para>
    /// </summary>
    public static class CorpsePhysics
    {
        public const string LayerName = "Corpse";

        public static int Layer => LayerMask.NameToLayer(LayerName);

        /// <summary>Сталкивается ли труп со слоем. Правило — проверяется тестом.</summary>
        public static bool CollidesWith(int layer) =>
            layer == LayerMask.NameToLayer("Default") || layer == LayerMask.NameToLayer("Ground");

        /// <summary>
        /// Прописывает матрицу столкновений слоя трупа в настройки проекта. Только редактор
        /// (сборщик трупов): в рантайме матрицу не трогаем — в редакторе это правило бы проект.
        /// </summary>
        public static void ConfigureLayerCollisions()
        {
            int corpse = Layer;
            if (corpse < 0) return;

            for (int layer = 0; layer < 32; layer++)
                Physics.IgnoreLayerCollision(corpse, layer, !CollidesWith(layer));
        }
    }
}
