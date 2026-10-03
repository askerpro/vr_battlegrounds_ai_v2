using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Русские геометрические подписи палитры; технические ключи и пользовательский текст не меняются.</summary>
    public static class BlockoutDefinitionText
    {
        private struct Text
        {
            public string name, description;
            public Text(string name,string description) {this.name=name;this.description=description;}
        }
        private static readonly Dictionary<string,Text> Texts = new Dictionary<string,Text>
        {
            {"LD_Beam_Low",new Text("Средний горизонтальный цилиндр","Удлинённый цилиндр с горизонтальной осью.")},
            {"LD_Block_Low",new Text("Низкий прямоугольный блок","Удлинённый прямоугольный объём с низким профилем.")},
            {"LD_Can_Mid",new Text("Средний цилиндр","Вертикальный цилиндр с круглым сечением в плане.")},
            {"LD_Tree_Tall",new Text("Высокий цилиндр","Высокий вертикальный цилиндр с круглым сечением в плане.")},
            {"LD_Crate",new Text("Низкий ящик","Сплошной кубический объём без внутренней полости.")},
            {"LD_Crate_Mid",new Text("Средний ящик","Вертикальный прямоугольный ящик с квадратным основанием.")},
            {"LD_Crate_Soft",new Text("Низкий полый ящик","Оболочка ящика с внутренней полостью; полость является частью реальной геометрии.")},
            {"LD_Dorito_Mid",new Text("Ступенчатое укрытие","Широкий нижний объём и более узкий верхний ярус. При высоте 1.2 м верхний ярус отсутствует; при увеличении высоты растёт только верхний ярус. Подходит для положения за широким низом и выхода по сторонам верхнего яруса; пригодность позы подтверждается человеком.")},
            {"LD_Door_Lintel",new Text("Дверная перемычка","Горизонтальная верхняя часть дверного проёма.")},
            {"LD_PillarBox",new Text("Высокий прямоугольный столб","Вертикальный столб с квадратным сечением в плане.")},
            {"LD_Snake_Segment",new Text("Низкий удлинённый сегмент","Длинный прямоугольный объём для составления протяжённой формы из секций.")},
            {"LD_Floor_Tile",new Text("Плита пола","Служебная горизонтальная плита; не входит в строительную палитру игровых форм.")},
            {"LD_Fence_Mid_Hard",new Text("Средняя длинная ограда","Длинная прямоугольная секция ограждения.")},
            {"LD_Fence_Mid_Soft",new Text("Средняя короткая ограда","Короткая прямоугольная секция ограждения.")},
            {"LD_Fence_Vault",new Text("Низкая ограда","Тонкая прямоугольная секция с низким профилем.")},
            {"LD_Net_Tall_Visual",new Text("Высокая тонкая панель","Высокая плоская секция. Геометрия задаёт наружные габариты панели.")},
            {"LD_PalletFence_Set_Soft",new Text("Средняя панель ограждения","Прямоугольный геометрический эталон секции ограждения.")},
            {"LD_PalletFence_Single_Soft",new Text("Средняя панель ограждения","Прямоугольный геометрический эталон секции ограждения.")},
            {"LD_Wall_Mid",new Text("Средняя стена","Прямая стеновая секция; длина, высота и толщина редактируются независимо.")},
            {"LD_Wall_Mid_Soft",new Text("Средняя стена","Прямая стеновая секция; длина, высота и толщина редактируются независимо.")},
            {"LD_Wall_Tall",new Text("Высокая стена","Высокая прямая стеновая секция с независимо редактируемыми размерами.")},
            {"LD_Wall_Tall_Soft",new Text("Высокая стена","Высокая прямая стеновая секция с независимо редактируемыми размерами.")},
            {"LD_Window_Sill",new Text("Оконный подоконник","Нижняя горизонтальная часть оконного проёма.")},
            {"LD_Window_Lintel",new Text("Оконная перемычка","Верхняя горизонтальная часть оконного проёма.")}
        };
        /// <summary>Заполняет только пустые поля, возвращает признак изменения. Сохранение asset выполняет вызывающая операция.</summary>
        public static void SetSteppedText(BlockoutBlockDefinition definition)
        {
            var text=Texts["LD_Dorito_Mid"];
            definition.displayName=text.name;definition.description=text.description;
        }
        public static bool FillMissing(BlockoutBlockDefinition definition)
        {
            if(definition==null) return false;
            if(!Texts.TryGetValue(definition.dimensionsSourceKey??"",out var text))
                text=new Text("Геометрическая форма","Пользовательское определение геометрического блока.");
            if((definition.title??"").Contains("полый")||(definition.title??"").ToLowerInvariant().Contains("hollow"))
                text=Texts["LD_Crate_Soft"];
            bool changed=false;
            if(string.IsNullOrWhiteSpace(definition.displayName)) {definition.displayName=text.name;changed=true;}
            if(string.IsNullOrWhiteSpace(definition.description))
            {
                definition.description=text.description;
                if(definition.geometryPrefab!=null)
                {
                    float height=BlockoutRegistryFactory.GeometryBounds(definition.geometryPrefab).size.y;
                    definition.description+=" Исходная высота: "+height.ToString("0.##",CultureInfo.GetCultureInfo("ru-RU"))+" м.";
                }
                changed=true;
            }
            if(changed) EditorUtility.SetDirty(definition);
            return changed;
        }
    }
}
