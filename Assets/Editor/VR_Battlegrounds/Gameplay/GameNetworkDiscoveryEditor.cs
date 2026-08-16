using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Network.Editor
{
    [CustomEditor(typeof(GameNetworkDiscovery))]
    public class GameNetworkDiscoveryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Управление сервером доступно только в режиме Play.", MessageType.Info);
                return;
            }

            var discovery = (GameNetworkDiscovery)target;
            
            // Чтобы получить значение CurrentRole, мы можем использовать Reflection или просто 
            // проверить активен ли сервер/клиент через NetworkManager.
            bool isServerActive = Mirror.NetworkServer.active;
            bool isClientActive = Mirror.NetworkClient.active;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Управление сетью (Debug)", EditorStyles.boldLabel);

            if (!isServerActive && !isClientActive)
            {
                if (GUILayout.Button("Запустить как Server", GUILayout.Height(30)))
                {
                    InvokeApplyRole(discovery, "Server");
                }
                
                if (GUILayout.Button("Запустить как Host", GUILayout.Height(30)))
                {
                    InvokeApplyRole(discovery, "Host");
                }
                
                if (GUILayout.Button("Запустить как Client", GUILayout.Height(30)))
                {
                    InvokeApplyRole(discovery, "Client");
                }
            }
            else
            {
                if (GUILayout.Button("Остановить соединение", GUILayout.Height(40)))
                {
                    InvokeStopCurrent(discovery);
                }
            }
        }

        private void InvokeApplyRole(GameNetworkDiscovery discovery, string roleName)
        {
            // GameNetworkDiscovery.AppRole_ enum
            var enumType = discovery.GetType().GetNestedType("AppRole", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (enumType != null)
            {
                object roleValue = System.Enum.Parse(enumType, roleName);
                var method = discovery.GetType().GetMethod("ApplyRole", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(discovery, new object[] { roleValue });
                }
            }
        }

        private void InvokeStopCurrent(GameNetworkDiscovery discovery)
        {
            var method = discovery.GetType().GetMethod("StopCurrent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(discovery, null);
            }
        }
    }
}
