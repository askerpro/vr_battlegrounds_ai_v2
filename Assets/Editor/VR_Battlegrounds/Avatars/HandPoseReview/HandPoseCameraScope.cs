using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Временное подавление только на время SceneView camera; не меняет Hierarchy/Undo/materials.</summary>
    public sealed class HandPoseCameraScope : IDisposable
    {
        sealed class Change { public Renderer Renderer; public bool Previous, Expected; }
        sealed class Frame { public Camera Camera; public List<Change> Changes = new List<Change>(); }
        readonly List<Frame> _frames = new List<Frame>();
        readonly Dictionary<Renderer,bool> _baseline = new Dictionary<Renderer,bool>();
        public int OwnedCount => _baseline.Count;

        public void Begin(Camera camera, bool sceneView, IEnumerable<Renderer> renderers)
        {
            var frame=new Frame{Camera=camera};
            foreach(var renderer in renderers) {
                if(!renderer)continue;
                bool current=renderer.forceRenderingOff;
                if(!_baseline.ContainsKey(renderer))_baseline.Add(renderer,current);
                bool target=sceneView?true:_baseline[renderer];
                if(current==target)continue;
                frame.Changes.Add(new Change{Renderer=renderer,Previous=current,Expected=target});
                renderer.forceRenderingOff=target;
            }
            _frames.Add(frame);
        }

        public void End(Camera camera)
        {
            for(int i=_frames.Count-1;i>=0;i--)if(_frames[i].Camera==camera){
                Restore(_frames[i]);_frames.RemoveAt(i);break;
            }
            if(_frames.Count==0)_baseline.Clear();
        }
        static void Restore(Frame frame)
        {
            for(int i=frame.Changes.Count-1;i>=0;i--){var change=frame.Changes[i];
                if(change.Renderer&&change.Renderer.forceRenderingOff==change.Expected)
                    change.Renderer.forceRenderingOff=change.Previous;
            }
        }
        public void Dispose()
        {
            for(int i=_frames.Count-1;i>=0;i--)Restore(_frames[i]);
            _frames.Clear();_baseline.Clear();
        }
    }
}
