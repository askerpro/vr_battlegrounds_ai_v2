using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Диагностика выбранного блока после редактирования; не сканирует сцену на каждом Repaint.</summary>
    [InitializeOnLoad]
    public static class BlockoutEditDiagnostics
    {
        public static event System.Action Changed;
        private sealed class Result
        {
            public string warning;
            public string lastLoggedWarning;
            public Object conflictTarget;
        }
        private static readonly Dictionary<GameObject,Result> results=new Dictionary<GameObject,Result>();

        static BlockoutEditDiagnostics()
        {
            Selection.selectionChanged+=RefreshSelected;
            Undo.undoRedoPerformed+=RefreshSelected;
            ObjectChangeEvents.changesPublished+=ObjectsChanged;
        }

        public static string Get(GameObject target)
        {
            if(target==null)return null;
            return results.TryGetValue(target,out var result)?result.warning:Refresh(target);
        }
        public static Object ConflictTarget(GameObject target)
            => target!=null&&results.TryGetValue(target,out var result)?result.conflictTarget:null;

        public static string Refresh(GameObject target,bool writeLog=false)
        {
            if(target==null||!target.scene.IsValid()||EditorApplication.isPlayingOrWillChangePlaymode)return null;
            foreach(var missing in results.Keys.Where(k=>k==null).ToArray())results.Remove(missing);
            if(!results.TryGetValue(target,out var result))results[target]=result=new Result();
            var previous=result.warning;
            result.warning=BlockoutPublishedSceneSync.InspectWarning(target,out var conflict);
            result.conflictTarget=conflict?.target;
            if(string.IsNullOrEmpty(result.warning))result.lastLoggedWarning=null;
            else if(writeLog&&result.lastLoggedWarning!=result.warning)
            {
                result.lastLoggedWarning=result.warning;
                GameLog.Debug.Warning($"Блокаут: {target.name} (ID {target.GetInstanceID()}). Действие применено. {result.warning}",target);
            }
            if(previous!=result.warning)Changed?.Invoke();
            return result.warning;
        }

        private static void RefreshSelected()
        {
            var target=Selection.activeGameObject;
            if(target!=null&&BlockoutRegistryFactory.TryDefinition(target,out _))Refresh(target);
        }

        private static void RefreshChangedSelection()
        {
            var target=Selection.activeGameObject;
            if(target!=null&&BlockoutRegistryFactory.TryDefinition(target,out _))Refresh(target,true);
        }

        private static void ObjectsChanged(ref ObjectChangeEventStream stream)
        {
            // Один отложенный расчёт после изменения сцены, включая соседнее препятствие или штатный Transform.
            // Preview-объекты имеют HideAndDontSave и не запускают новый цикл диагностики.
            bool changed=false;
            for(int i=0;i<stream.length&&!changed;i++)
            {
                if(stream.GetEventType(i)!=ObjectChangeKind.ChangeGameObjectOrComponentProperties)continue;
                stream.GetChangeGameObjectOrComponentPropertiesEvent(i,out var change);
                var edited=EditorUtility.InstanceIDToObject(change.instanceId);
                var go=edited as GameObject;
                if(go==null&&edited is Component component)go=component.gameObject;
                changed=go!=null&&go.scene.IsValid()&&!EditorSceneManager.IsPreviewScene(go.scene)
                    &&!EditorUtility.IsPersistent(go)&&(go.hideFlags&HideFlags.HideAndDontSave)==0;
            }
            if(!changed)return;
            EditorApplication.delayCall-=RefreshChangedSelection;
            EditorApplication.delayCall+=RefreshChangedSelection;
        }
    }
}
