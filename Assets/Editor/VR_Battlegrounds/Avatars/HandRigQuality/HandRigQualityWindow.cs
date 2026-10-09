using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandRigQuality
{
    public sealed class HandRigQualityWindow : EditorWindow
    {
        GameObject _target,_reference;
        readonly HandRigQualityRequest _settings=new HandRigQualityRequest();
        HandRigQualityReport _report;
        Vector2 _scroll;

        [MenuItem("Tools/VR Battlegrounds/Avatars/Hand Rig Quality")]
        public static void Open()=>GetWindow<HandRigQualityWindow>("Hand Rig Quality");

        void OnEnable()
        {
            if(!_target)_target=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Optimized_MEF_Player.prefab");
            if(!_reference)_reference=AssetDatabase.LoadAssetAtPath<GameObject>(HandRigQualityCalibration.Cyborg);
        }

        void OnGUI()
        {
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("Статический риг и скиннинг. Оранжевый — цель, голубой — эталон. Динамика запястья и контакт с оружием требуют отдельных прогонов.",MessageType.Info);
            _target=(GameObject)EditorGUILayout.ObjectField("Исследуемый prefab",_target,typeof(GameObject),false);
            _reference=(GameObject)EditorGUILayout.ObjectField("Эталон prefab",_reference,typeof(GameObject),false);
            _settings.Side=(UxrHandSide)EditorGUILayout.EnumPopup("Кисть",_settings.Side);
            _settings.EyeHeightMeters=EditorGUILayout.FloatField("Рост до глаз, м",_settings.EyeHeightMeters);
            _settings.CommonSdkPose=EditorGUILayout.Toggle("Общая поза SDK Default",_settings.CommonSdkPose);
            using(new EditorGUI.DisabledScope(_settings.CommonSdkPose))
            {
                PoseField("Поза цели",_target,ref _settings.PoseName);PoseField("Поза эталона",_reference,ref _settings.ReferencePoseName);
            }
            _settings.Blend=EditorGUILayout.Slider("Blend",_settings.Blend,0,1);
            bool sensor=_settings.Frame=="sensor";
            sensor=EditorGUILayout.Toggle("Рамка датчика S",sensor);_settings.Frame=sensor?"sensor":"wrist";
            if(sensor)
            {
                _settings.SensorPath=EditorGUILayout.TextField("Путь S цели",_settings.SensorPath);
                _settings.ReferenceSensorPath=EditorGUILayout.TextField("Путь S эталона",_settings.ReferenceSensorPath);
                EditorGUILayout.HelpBox("Пути относительно корня prefab. Пустой путь допустим только при единственном активном sensor Transform. Это авторская рамка, без проверки устройства.",MessageType.Info);
            }
            EditorGUILayout.LabelField("Область манжеты",EditorStyles.boldLabel);
            _settings.CuffStartMeters=EditorGUILayout.FloatField("Начало от запястья, м",_settings.CuffStartMeters);
            _settings.CuffEndMeters=EditorGUILayout.FloatField("Конец, м",_settings.CuffEndMeters);
            _settings.CuffRadiusMeters=EditorGUILayout.FloatField("Радиус, м",_settings.CuffRadiusMeters);
            _settings.CuffRegionReviewed=EditorGUILayout.Toggle("Область проверена",_settings.CuffRegionReviewed);
            _settings.RenderImages=EditorGUILayout.Toggle("Четыре PNG вида",_settings.RenderImages);
            EditorGUILayout.LabelField("Калибровка оценки на SDK",EditorStyles.boldLabel);
            _settings.CalibrationPath=EditorGUILayout.TextField("JSON калибровки",_settings.CalibrationPath);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
                if(GUILayout.Button("Откалибровать на Cyborg и BigHands (общая SDK-поза)"))
                {
                    var calibration=HandRigQualityCalibration.CalibrateSdk(_settings.EyeHeightMeters);
                    if(calibration.Status=="calibrated_static_sdk")_settings.CalibrationPath=calibration.Path;
                    else ShowNotification(new GUIContent(calibration.Error??calibration.Status));
                }
            using(new EditorGUI.DisabledScope(!_target || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
                if(GUILayout.Button("Измерить и сохранить пакет"))
                {
                    _settings.AvatarPath=AssetDatabase.GetAssetPath(_target);_settings.ReferenceAvatarPath=AssetDatabase.GetAssetPath(_reference);
                    _report=HandRigQualityAnalyzer.Analyze(_settings);
                }
            if(_report!=null)
            {
                EditorGUILayout.HelpBox(_report.Error??(_report.Status+" · числовых метрик: "+_report.Metrics.Count(m=>m.Value.HasValue)),_report.Error!=null?MessageType.Error:MessageType.Info);
                EditorGUILayout.LabelField("Статическая оценка",_report.StaticScore.HasValue?_report.StaticScore.Value.ToString("F1")+" / 100 · "+_report.ScoredMetrics+" измерений":_report.CalibrationStatus);
                if(!string.IsNullOrEmpty(_report.OutputDirectory)&&GUILayout.Button("Открыть папку отчёта"))EditorUtility.RevealInFinder(_report.OutputDirectory);
                foreach(var metric in _report.Metrics)
                    EditorGUILayout.LabelField(metric.Id+" · "+metric.Subject,metric.Value.HasValue?metric.Value.Value.ToString("G5")+" "+metric.Unit+" · "+metric.Status:metric.Status);
            }
            EditorGUILayout.EndScrollView();
        }

        static void PoseField(string label, GameObject prefab, ref string name)
        {
            var avatar=prefab?prefab.GetComponent<UxrAvatar>():null;
            var names=new[]{"Авторский Rest"}.Concat(avatar?avatar.GetHandPoses().Select(p=>p.name).Distinct().OrderBy(p=>p):Enumerable.Empty<string>()).ToArray();
            int selected=string.IsNullOrEmpty(name)?0:System.Array.IndexOf(names,name);
            selected=EditorGUILayout.Popup(label,Mathf.Max(0,selected),names);name=selected==0?"":names[selected];
        }
    }
}
