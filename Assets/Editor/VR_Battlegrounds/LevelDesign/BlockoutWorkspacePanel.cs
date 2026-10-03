using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Настройки, проверки и обслуживание внутри единственного редактора блокаута.</summary>
    public static class BlockoutWorkspacePanel
    {
        public static Action RegistryRebuildRequested, LegacyNormalizeRequested;
        private static int mapScope;
        private static string layoutPath, mapResult, positionResult, error, maintenanceResult;
        private static bool contacts, recipes;
        public static void DrawSettings()
        {
            EditorGUILayout.LabelField("Показ сетки активной карты: меняет только отображение и настройки привязки редактора; геометрия карты сохраняется.",EditorStyles.wordWrappedLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Показать сетку"))Run(BlockoutGrid.EnableActive);
                if(GUILayout.Button("Скрыть сетку"))Run(BlockoutGrid.DisableActive);
            }
            BlockoutGridSettingsPanel.Draw();
            if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
        }

        public static void DrawChecks()
        {
            EditorGUILayout.LabelField("Проверки",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Отчёты измеряют геометрию. Ручная оценка композиции остаётся отдельным решением человека.",MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling))
            {
                mapScope=EditorGUILayout.Popup("Область отчёта",mapScope,new[]{"Активная карта","Все боевые карты"});
                if(GUILayout.Button("Измерить карту по принципам")) Run(()=>mapResult=MapPrinciplesReport.Run(mapScope==0?SceneManager.GetActiveScene().name:null));
                DrawResult(mapResult,MapPrinciplesReport.OutputFolder,"Открыть папку отчётов карты");
                EditorGUILayout.Space();EditorGUILayout.LabelField("Влияние позиций активной карты",EditorStyles.boldLabel);
                layoutPath=EditorGUILayout.TextField("JSON разметки",layoutPath??"");
                if(GUILayout.Button("Выбрать JSON разметки"))
                {
                    string selected=EditorUtility.OpenFilePanel("Разметка позиций активной карты","Docs/level-design/maps","json");
                    if(!string.IsNullOrEmpty(selected)) layoutPath=selected;
                }
                using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(layoutPath)||!File.Exists(layoutPath)))
                    if(GUILayout.Button("Измерить влияние позиций")) Run(()=>positionResult=PositionImpactReport.Run(SceneManager.GetActiveScene(),layoutPath));
                DrawResult(positionResult,"Temp/LevelDesign","Открыть отчёты влияния позиций");
                contacts=EditorGUILayout.Foldout(contacts,"Оценка выбранной контактной композиции",true);
                if(contacts) ContactCatalogReviewWindow.DrawEmbedded();
            }
            if(!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error,MessageType.Error);
        }
        private static void Run(Action action)
        {
            try {action();error=null;} catch(Exception exception) {error=exception.Message;}
        }
        private static void DrawResult(string result,string output,string label)
        {
            if(string.IsNullOrEmpty(result)) return;
            EditorGUILayout.TextArea(result,GUILayout.MinHeight(55),GUILayout.MaxHeight(160));
            if(GUILayout.Button(label)) EditorUtility.RevealInFinder(Path.GetFullPath(output));
        }
        public static void DrawMaintenance()
        {
            EditorGUILayout.LabelField("Обслуживание",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Эти операции меняют общие префабы и служебные ассеты проекта, поэтому затрагивают все карты. Это не редактирование выбранного экземпляра и не сцены. Автоматического запекания окклюзии нет.",MessageType.Warning);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling))
            {
                EditorGUILayout.LabelField("Публикация пяти канонических форм: обновляет активный реестр, сохраняя исходные GUID и ссылки существующих объектов. Сцены не меняются.",EditorStyles.wordWrappedLabel);
                using(new EditorGUI.DisabledScope(RegistryRebuildRequested==null))
                    if(GUILayout.Button("Опубликовать канонический реестр")) Maintenance(RegistryRebuildRequested,"Канонический реестр опубликован.");
                EditorGUILayout.LabelField("Нормализация активной карты (Undo): привязывает опорные углы к сетке; прежние префабы могут получить угол кратно 90°, масштаб источника и нормализованные родители. Меняет геометрию всей активной карты; сцену не сохраняет.",EditorStyles.wordWrappedLabel);
                using(new EditorGUI.DisabledScope(LegacyNormalizeRequested==null))
                    if(GUILayout.Button("Нормализовать прежнюю геометрию")) Maintenance(LegacyNormalizeRequested,"Операция нормализации завершена.");
                EditorGUILayout.LabelField("Миграция размеров: применяет сохранённый общий паспорт ко всем исходным префабам алфавита и метровым UV.",EditorStyles.wordWrappedLabel);
                if(GUILayout.Button("Применить сохранённый паспорт к алфавиту")) Maintenance(BlockoutAlphabetGridMigration.Run,"Миграция размеров завершена.");
            }
            if(!string.IsNullOrEmpty(maintenanceResult)) EditorGUILayout.HelpBox(maintenanceResult,MessageType.Info);
        }
        private static void Recipe(string title,string scope,Action action)
        {
            EditorGUILayout.LabelField(scope,EditorStyles.wordWrappedLabel);
            if(GUILayout.Button(title)) Maintenance(action,"Рецепт завершён: "+title+".");
        }
        private static void Historical(string typeName,string methodName)
        {
            // Старые рецепты находятся в Assembly-CSharp-Editor; независимая LD-сборка использует их исходный API.
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(assembly=>assembly.GetType("VrBattlegrounds.Editor."+typeName)).FirstOrDefault(candidate=>candidate!=null);
            var method=type?.GetMethod(methodName,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
            if(method==null) throw new InvalidOperationException("Исходный рецепт недоступен: "+typeName+"."+methodName);
            try {method.Invoke(null,null);} catch(System.Reflection.TargetInvocationException exception) {throw exception.InnerException??exception;}
        }
        private static void Maintenance(Action action,string success)
        {
            try {action();maintenanceResult=success;} catch(Exception exception) {maintenanceResult="Операция остановлена: "+exception.Message;}
        }
    }
}
