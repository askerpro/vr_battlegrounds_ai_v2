using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.EditorScripts
{
    public static class HUDPrefabGenerator
    {
        [MenuItem("VrBattlegrounds/Tools/Generate Elimination HUD")]
        public static void GenerateHUD()
        {
            GameObject root = new GameObject("EliminationHUD");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(800, 600);
            
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();

            GameObject rootPanel = new GameObject("Root");
            rootPanel.transform.SetParent(root.transform, false);
            RectTransform rootRt = rootPanel.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;

            GameObject statsObj = new GameObject("StatsAndTimers");
            statsObj.transform.SetParent(rootPanel.transform, false);
            RectTransform statsRt = statsObj.AddComponent<RectTransform>();
            statsRt.anchorMin = Vector2.zero; statsRt.anchorMax = Vector2.one;
            statsRt.offsetMin = Vector2.zero; statsRt.offsetMax = Vector2.zero;

            Font arial = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (arial == null) arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            Color bgColor = new Color(0, 0, 0, 0.5f);

            GameObject hpObj = CreateWidgetContainer("HP", statsObj.transform, new Vector2(0.05f, 0.05f), new Vector2(0.3f, 0.15f), bgColor);
            GameObject hpValue = CreateUIText("Value", hpObj.transform, arial, "100 HP");
            HUDWidget_Health healthWidget = hpObj.AddComponent<HUDWidget_Health>();
            SerializePrivateField(healthWidget, "_healthText", hpValue.GetComponent<Text>());

            GameObject timersObj = new GameObject("Timers");
            timersObj.transform.SetParent(statsObj.transform, false);
            RectTransform timersRt = timersObj.AddComponent<RectTransform>();
            timersRt.anchorMin = new Vector2(0.35f, 0.85f); timersRt.anchorMax = new Vector2(0.65f, 0.95f);
            timersRt.offsetMin = Vector2.zero; timersRt.offsetMax = Vector2.zero;

            // --- Match Timer ---
            GameObject matchTimerObj = CreateWidgetContainer("MatchTimer", timersObj.transform, new Vector2(0f, 0.55f), new Vector2(1f, 1f), bgColor);
            GameObject matchTimerValue = CreateUIText("Value", matchTimerObj.transform, arial, "00:00");
            // Make the text slightly smaller for Match Timer compared to Round Timer
            matchTimerValue.GetComponent<Text>().fontSize = 20; 
            HUDWidget_GameplayTimer matchTimerWidget = matchTimerObj.AddComponent<HUDWidget_GameplayTimer>();
            SerializePrivateField(matchTimerWidget, "_timerText", matchTimerValue.GetComponent<Text>());

            // --- Round Timer ---
            GameObject roundTimerObj = CreateWidgetContainer("RoundTimer", timersObj.transform, new Vector2(0f, 0f), new Vector2(1f, 0.45f), bgColor);
            GameObject roundTimerValue = CreateUIText("Value", roundTimerObj.transform, arial, "00:00");
            HUDWidget_RoundTimer timerWidget = roundTimerObj.AddComponent<HUDWidget_RoundTimer>();
            SerializePrivateField(timerWidget, "_timerText", roundTimerValue.GetComponent<Text>());

            GameObject scoreObj = CreateWidgetContainer("TeamScore", statsObj.transform, new Vector2(0.7f, 0.85f), new Vector2(0.95f, 0.95f), bgColor);
            GameObject scoreValue = CreateUIText("Value", scoreObj.transform, arial, "Score: 0 - 0");
            HUDWidget_TeamScore scoreWidget = scoreObj.AddComponent<HUDWidget_TeamScore>();
            SerializePrivateField(scoreWidget, "_scoreText", scoreValue.GetComponent<Text>());

            // --- Game Notifications ---
            GameObject notifyObj = CreateWidgetContainer("GameNotifications", rootPanel.transform, new Vector2(0.2f, 0.6f), new Vector2(0.8f, 0.8f), new Color(0, 0, 0, 0.5f));
            CanvasGroup cGroup = notifyObj.AddComponent<CanvasGroup>();
            cGroup.alpha = 0f;
            cGroup.blocksRaycasts = false;
            
            GameObject notifyValue = CreateUIText("Value", notifyObj.transform, arial, "");
            Text notifyText = notifyValue.GetComponent<Text>();
            notifyText.fontSize = 40;
            notifyText.color = Color.yellow;
            
            HUDWidget_GameNotification notifyWidget = notifyObj.AddComponent<HUDWidget_GameNotification>();
            SerializePrivateField(notifyWidget, "_notificationText", notifyText);

            if (!System.IO.Directory.Exists("Assets/Prefabs/UI/HUD"))
            {
                System.IO.Directory.CreateDirectory("Assets/Prefabs/UI/HUD");
            }
            
            string path = "Assets/Prefabs/UI/HUD/EliminationHUD.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            GameObject.DestroyImmediate(root);
            Debug.Log($"[HUDPrefabGenerator] {path} created successfully!");
        }

        private static GameObject CreateWidgetContainer(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color bgColor)
        {
            GameObject container = new GameObject(name);
            container.transform.SetParent(parent, false);
            RectTransform rt = container.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(container.transform, false);
            RectTransform bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            Image img = bg.AddComponent<Image>();
            img.color = bgColor;

            return container;
        }

        private static GameObject CreateUIText(string name, Transform parent, Font font, string defaultText)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Text txt = go.AddComponent<Text>();
            txt.font = font;
            txt.text = defaultText;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 24;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private static void SerializePrivateField(Object target, string fieldName, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning($"[HUDPrefabGenerator] Could not find {fieldName} on {target.GetType()}- wait, does the field have [SerializeField]?");
            }
        }
    }
}
