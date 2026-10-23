// Lobby: подсветка слотов арсенала, ночной пресет для запекания, запуск запекания (execute_code на worker).
// Задача arsenal-slot-lighting. Сохранение сцены — после завершения запекания отдельным вызовом.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", OpenSceneMode.Single);
string report = VrBattlegrounds.Editor.Arsenal.ArsenalSlotLighting.Apply(scene);
VrBattlegrounds.EditorTools.LobbyNightLighting.PrepareBake();
EditorSceneManager.SaveScene(scene);
bool started = Lightmapping.BakeAsync();
return report + " | bake started=" + started;
