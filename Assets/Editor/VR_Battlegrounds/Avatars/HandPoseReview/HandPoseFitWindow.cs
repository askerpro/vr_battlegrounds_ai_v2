using System;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    public sealed class HandPoseFitWindow : EditorWindow
    {
        HandPoseFitRequest _request=new HandPoseFitRequest();
        HandPoseFitReport _report;
        Vector2 _scroll;
        bool _advanced;
        UxrGrabber _runtimeGrabber;
        GameObject _runtimeRoot;
        int _seriesCount=1,_seriesFrames=30;
        readonly List<HandPoseFitReport> _seriesReports=new List<HandPoseFitReport>();
        public static void Open() => GetWindow<HandPoseFitWindow>("Посадка кисти");

        /// <summary>Открывает существующую проверку с явным аватаром; анализ и запись поз не запускает.</summary>
        public static void OpenFor(UxrAvatar avatar)
        {
            var window = GetWindow<HandPoseFitWindow>("Посадка кисти");
            window._request.AvatarPrefab = avatar;
            window._report = null;
            window.Repaint();
        }

        void OnGUI()
        {
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Посадка кисти на предмет",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Анализ поверхностей из статической копии или фактического игрового хвата. Общий балл качества и автоматическая подгонка отсутствуют. Непроверенные маски и неизвестный знак отмечаются в отчёте.",MessageType.Info);
            EditorGUILayout.LabelField("Фактический игровой хват",EditorStyles.boldLabel);
            _runtimeGrabber=(UxrGrabber)EditorGUILayout.ObjectField("Живой граббер",_runtimeGrabber,typeof(UxrGrabber),true);
            _runtimeRoot=(GameObject)EditorGUILayout.ObjectField("Корень геометрии (необязательно)",_runtimeRoot,typeof(GameObject),true);
            _request.StateName=EditorGUILayout.TextField("Состояние действия",_request.StateName);
            _request.SeriesName=EditorGUILayout.TextField("Название серии",_request.SeriesName);
            _seriesCount=EditorGUILayout.IntSlider("Снимков в серии",_seriesCount,1,30);
            _seriesFrames=EditorGUILayout.IntSlider("Интервал, кадры",_seriesFrames,1,600);
            using(new EditorGUI.DisabledScope(!EditorApplication.isPlaying||EditorApplication.isPaused||!_runtimeGrabber||HandPoseFitRuntimeCapture.Busy)) {
                if(GUILayout.Button("Снять игровой хват после SDK update")) {
                    try {HandPoseFitRuntimeCapture.Request(_runtimeGrabber,_request,report=>{_report=report;_seriesReports.Add(report);Repaint();},_runtimeRoot,_seriesCount,_seriesFrames);}
                    catch(Exception e) {_report=new HandPoseFitReport{Status="invalid_configuration",Error=e.Message};}
                }
            }
            if(HandPoseFitRuntimeCapture.Busy) {EditorGUILayout.LabelField("Ожидается SDK update / обрабатывается серия…");if(GUILayout.Button("Отменить оставшиеся снимки")) HandPoseFitRuntimeCapture.Cancel();}
            if(_seriesReports.Count>0) {EditorGUILayout.LabelField("Сохранено игровых отчётов: "+_seriesReports.Count);foreach(var item in _seriesReports.TakeLast(5)) if(!string.IsNullOrEmpty(item.JsonPath)&&GUILayout.Button(item.StateName+" · кадр "+item.CaptureFrame)) EditorUtility.RevealInFinder(item.JsonPath);}
            EditorGUILayout.Space();EditorGUILayout.LabelField("Статическая конфигурация",EditorStyles.boldLabel);
            _request.AvatarPrefab=(UxrAvatar)EditorGUILayout.ObjectField("Аватар",_request.AvatarPrefab,typeof(UxrAvatar),false);
            GameObject previous=_request.ObjectPrefab;
            _request.ObjectPrefab=(GameObject)EditorGUILayout.ObjectField("Предмет",_request.ObjectPrefab,typeof(GameObject),false);
            if(previous!=_request.ObjectPrefab) {_request.GrabbableIndexPath="";_request.GrabPoint=0;}
            if(_request.ObjectPrefab) {
                var components=_request.ObjectPrefab.GetComponentsInChildren<UxrGrabbableObject>(true);
                string[] paths=components.Select(c=>HandPoseFitSnapshot.IndexPath(c.transform,_request.ObjectPrefab.transform)).ToArray();
                string[] labels=components.Select(c=>c.name+" ["+HandPoseFitSnapshot.HumanPath(c.transform,_request.ObjectPrefab.transform)+"]").ToArray();
                if(paths.Length>0) {int index=Math.Max(0,Array.IndexOf(paths,_request.GrabbableIndexPath));index=EditorGUILayout.Popup("Хватаемый объект",index,labels);_request.GrabbableIndexPath=paths[index];}
                else EditorGUILayout.HelpBox("В предмете нет UxrGrabbableObject.",MessageType.Warning);
            }
            _request.GrabPoint=EditorGUILayout.IntField("Точка хвата",_request.GrabPoint);
            _request.Side=(UxrHandSide)EditorGUILayout.EnumPopup("Рука",_request.Side);
            _request.PoseOverride=(UxrHandPoseAsset)EditorGUILayout.ObjectField("Кандидат позы (необязательно)",_request.PoseOverride,typeof(UxrHandPoseAsset),false);
            _request.OverrideBlend=EditorGUILayout.Toggle("Задать Blend вручную",_request.OverrideBlend);
            if(_request.OverrideBlend) _request.Blend=EditorGUILayout.Slider("Blend",_request.Blend,0,1);
            _request.AllowDefaultGrip=EditorGUILayout.Toggle("Разрешить SDK default",_request.AllowDefaultGrip);
            _request.AlignmentMode=EditorGUILayout.Popup("Выравнивание",_request.AlignmentMode=="controller_reference"?1:0,new[]{"Сохранённый трансформ хвата","С учётом контроллера (пока недоступно)"})==0?"grip_reference":"controller_reference";
            string[] profiles={"exploratory","main_grip","foregrip","magazine","pump","hand_support"};
            string[] names={"Исследование всей поверхности","Основная рукоять","Цевьё","Магазин","Помпа","Поддержка другой рукой (пока недоступно)"};
            int pi=Array.IndexOf(profiles,_request.Profile);_request.Profile=profiles[EditorGUILayout.Popup("Тип контакта",Math.Max(0,pi),names)];
            _request.PalmarOnly=EditorGUILayout.Toggle("Ладонная маска по нормалям",_request.PalmarOnly);
            if(_request.PalmarOnly) {
                _request.PalmNormalSign=EditorGUILayout.Popup("Направление нормали",_request.PalmNormalSign<0?0:1,new[]{"-Up костей","+Up костей"})==0?-1:1;
                _request.PalmarMinDot=EditorGUILayout.Slider("Минимальный dot нормали",_request.PalmarMinDot,-1,1);
            }
            _request.ContactBoundsEnabled=EditorGUILayout.Toggle("Ограничить область предмета",_request.ContactBoundsEnabled);
            if(_request.ContactBoundsEnabled) _request.ContactBounds=EditorGUILayout.BoundsField("Область, метры в осях предмета",_request.ContactBounds);
            _request.MaskReviewed=EditorGUILayout.Toggle("Маски проверены по снимкам",_request.MaskReviewed);
            EditorGUILayout.LabelField("Контактные области предмета",EditorStyles.boldLabel);
            var regions=_request.ContactRegions.ToList();
            for(int i=0;i<regions.Count;i++) {
                var region=regions[i];EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                region.Name=EditorGUILayout.TextField("Назначение области",region.Name);region.Bounds=EditorGUILayout.BoundsField("Границы в осях предмета, м",region.Bounds);
                region.AllowedZones=Split(EditorGUILayout.TextField("Разрешённые пальцы через запятую",string.Join(",",region.AllowedZones)));
                region.AllowedSegments=Split(EditorGUILayout.TextField("Фаланги (например index/distal)",string.Join(",",region.AllowedSegments)));
                region.RendererPaths=Split(EditorGUILayout.TextField("Пути Renderer (пусто — все)",string.Join(",",region.RendererPaths)));
                region.Required=EditorGUILayout.Toggle("Ожидается контакт",region.Required);region.Forbidden=EditorGUILayout.Toggle("Контакт запрещён",region.Forbidden);region.Reviewed=EditorGUILayout.Toggle("Разметка проверена",region.Reviewed);
                if(GUILayout.Button("Удалить область")) {regions.RemoveAt(i);i--;}
                EditorGUILayout.EndVertical();
            }
            if(GUILayout.Button("Добавить контактную область")) regions.Add(new FitContactRegion());_request.ContactRegions=regions.ToArray();
            _request.ContactDistanceMinMm=EditorGUILayout.FloatField("Контакт от, мм (включительно)",_request.ContactDistanceMinMm);
            _request.ProximityMm=EditorGUILayout.FloatField("Контакт до, мм (включительно)",_request.ProximityMm);
            _request.ShowContactMarkers=EditorGUILayout.Toggle("Подсветить точки контакта",_request.ShowContactMarkers);
            EditorGUILayout.LabelField("По умолчанию 0–2 мм. Жёлтые точки: близость есть, знак неизвестен; красные: внутри предмета. Диапазон не задаёт норму качества хвата.",EditorStyles.wordWrappedMiniLabel);
            _advanced=EditorGUILayout.Foldout(_advanced,"Выборка, кадрирование и диагностика");
            if(_advanced) {
                _request.SampleCount=EditorGUILayout.IntField("Число точек",_request.SampleCount);
                _request.Seed=EditorGUILayout.IntField("Seed",_request.Seed);
                _request.MaxContactMarkers=EditorGUILayout.IntField("Максимум видимых пар",_request.MaxContactMarkers);
                _request.ContactMarkerRadiusPixels=EditorGUILayout.Slider("Радиус точки, пиксели",_request.ContactMarkerRadiusPixels,1,10);
                _request.RenderImages=EditorGUILayout.Toggle("Сохранить 24 PNG",_request.RenderImages);
                _request.ImageSize=EditorGUILayout.IntPopup("Размер PNG",_request.ImageSize,new[]{"512","1024","2048"},new[]{512,1024,2048});
                _request.FrameSizeMeters=EditorGUILayout.FloatField("Высота кадра, м",_request.FrameSizeMeters);
                _request.OverrideFrameCenter=EditorGUILayout.Toggle("Зафиксировать центр кадра",_request.OverrideFrameCenter);
                if(_request.OverrideFrameCenter) _request.FrameCenterMeters=EditorGUILayout.Vector3Field("Центр в осях предмета, м",_request.FrameCenterMeters);
                _request.HandOffsetMm=EditorGUILayout.Vector3Field("Временное смещение кисти, мм",_request.HandOffsetMm);
                _request.IncludeOtherHand=EditorGUILayout.Toggle("Снять другую кисть",_request.IncludeOtherHand);
                _request.CheckHandSelfIntersections=EditorGUILayout.Toggle("Самопересечения кисти",_request.CheckHandSelfIntersections);
                _request.CheckSamplingConvergence=EditorGUILayout.Toggle("Проверить двойной выборкой",_request.CheckSamplingConvergence);
                Mesh volume=null;try {volume=HandPoseFitSnapshot.ResolveVolumeMesh(_request);}catch(ArgumentException e){EditorGUILayout.HelpBox(e.Message,MessageType.Warning);}
                Mesh previousVolume=volume;volume=(Mesh)EditorGUILayout.ObjectField("Локальный объём для знака",volume,typeof(Mesh),false);
                _request.AnalysisVolumeMeshPath=volume?AssetDatabase.GetAssetPath(volume):null;
                if(volume) {AssetDatabase.TryGetGUIDAndLocalFileIdentifier(volume,out _request.AnalysisVolumeMeshGuid,out _request.AnalysisVolumeMeshLocalFileId);}else {_request.AnalysisVolumeMeshGuid=null;_request.AnalysisVolumeMeshLocalFileId=0;}
                if(volume!=previousVolume) _request.AnalysisVolumeReviewed=false;
                if(volume) {_request.AnalysisVolumePositionMeters=EditorGUILayout.Vector3Field("Объём: позиция, м",_request.AnalysisVolumePositionMeters);_request.AnalysisVolumeEulerDegrees=EditorGUILayout.Vector3Field("Объём: поворот, градусы",_request.AnalysisVolumeEulerDegrees);_request.AnalysisVolumeScale=EditorGUILayout.Vector3Field("Объём: масштаб",_request.AnalysisVolumeScale);_request.AnalysisVolumeReviewed=EditorGUILayout.Toggle("Совпадение объёма проверено",_request.AnalysisVolumeReviewed);}
                _request.ReferenceReportPath=EditorGUILayout.TextField("Эталонный report.json",_request.ReferenceReportPath);_request.ReferenceAccepted=EditorGUILayout.Toggle("Эталон принят пользователем",_request.ReferenceAccepted);
            }
            using(new EditorGUI.DisabledScope(!_request.AvatarPrefab||!_request.ObjectPrefab||EditorApplication.isPlayingOrWillChangePlaymode)) {
                if(GUILayout.Button("Выполнить анализ")) {
                    try {EditorUtility.DisplayProgressBar("Посадка кисти","Изолированный snapshot, измерения и изображения",.5f);_report=HandPoseFitAnalyzer.Analyze(_request);}
                    finally {EditorUtility.ClearProgressBar();}
                }
            }
            if(_report!=null) {
                EditorGUILayout.Space();EditorGUILayout.LabelField(_report.Status,EditorStyles.boldLabel);
                if(!string.IsNullOrEmpty(_report.Error)) EditorGUILayout.HelpBox(_report.Error,MessageType.Error);
                EditorGUILayout.LabelField($"Кисть: {_report.HandTriangles} граней; предмет: {_report.ObjectTriangles}; касаются/пересекаются: {_report.IntersectingHandTriangles} граней кисти.",EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField($"Пары в диапазоне контакта: {_report.ContactCandidateCount}; выбрано для изображения: {_report.ContactMarkers.Count}.",EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField($"Источник: {_report.CaptureSource}; состояние: {_report.StateName}; кадр: {_report.CaptureFrame}. Самопересечения: {_report.HandSelfIntersectionPairs}; с другой рукой: {_report.OtherHandIntersectionPairs}.",EditorStyles.wordWrappedLabel);
                foreach(var zone in _report.Zones) EditorGUILayout.LabelField($"{zone.Zone}: P50 {zone.DistanceP50Mm:F2} мм · P95 {zone.DistanceP95Mm:F2} мм · близость {zone.NearSurfaceFraction:P1} · знак неизвестен: {zone.UnknownSignSamples}",EditorStyles.wordWrappedLabel);
                foreach(var region in _report.Regions.Where(x=>x.Samples>0)) EditorGUILayout.LabelField($"{region.Name} / {region.FingerSegment}: {region.NearAreaMm2:F1} мм² близости · ожидается: {region.Expected}",EditorStyles.wordWrappedMiniLabel);
                foreach(var finding in _report.Findings) EditorGUILayout.LabelField(finding,EditorStyles.wordWrappedMiniLabel);
                foreach(var reliability in _report.Reliability) EditorGUILayout.LabelField(reliability.Metric+": "+reliability.Status+" — "+reliability.Reason,EditorStyles.wordWrappedMiniLabel);
                if(!string.IsNullOrEmpty(_report.JsonPath)) {
                    if(GUILayout.Button("Показать каталог отчёта")) EditorUtility.RevealInFinder(_report.JsonPath);
                    if(File.Exists(Path.Combine(_report.OutputDirectory,"index.html"))&&GUILayout.Button("Открыть изображения")) Application.OpenURL(new Uri(Path.Combine(_report.OutputDirectory,"index.html")).AbsoluteUri);
                }
                foreach(string limitation in _report.Limitations) EditorGUILayout.LabelField("• "+limitation,EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
        static string[] Split(string value) => value.Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
    }
}
