using System;
using System.Collections.Generic;
using System.IO;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Одноразовые/серийные снимки после SDK. Не управляет хватом или игровыми действиями.</summary>
    [InitializeOnLoad]
    public static class HandPoseFitRuntimeCapture
    {
        sealed class Job {public UxrGrabber Grabber;public GameObject Root;public HandPoseFitRequest Settings;public Action<HandPoseFitReport> Completed;public int Remaining,Interval,NextFrame,Index;public double Deadline;public string Directory;}
        sealed class Frozen {public HandPoseFitSnapshot Snapshot;public HandPoseFitReport Report;public Action<HandPoseFitReport> Completed;}
        static readonly List<Job> Jobs=new List<Job>();
        static readonly Queue<Frozen> Snapshots=new Queue<Frozen>();
        public static bool Busy => Jobs.Count>0||Snapshots.Count>0;
        public static int LastCapturedFrame {get;private set;}=-1;
        static HandPoseFitRuntimeCapture()
        {
            UxrManager.AvatarsUpdated+=HandleAvatarsUpdated;
            EditorApplication.update+=HandleEditorUpdate;
            EditorApplication.playModeStateChanged+=HandlePlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload+=HandleReload;
        }

        public static void Request(UxrGrabber grabber,HandPoseFitRequest settings,Action<HandPoseFitReport> completed,GameObject geometryRoot=null,int count=1,int framesApart=30)
        {
            if(!Application.isPlaying||EditorApplication.isPaused||!grabber||!grabber.GrabbedObject) throw new ArgumentException("В Play Mode выберите руку с уже схваченным предметом; паузу нужно снять до SDK update.");
            if(count<1||count>30||framesApart<1||framesApart>600||Jobs.Count>=4) throw new ArgumentException("Серия: 1..30 снимков, интервал 1..600 кадров, максимум четыре задания.");
            var r=settings.Copy();HandPoseFitAnalyzer.ValidateSettings(r);
            if(r.HandOffsetMm!=Vector3.zero||r.PoseOverride||!string.IsNullOrEmpty(r.PoseOverridePath)||r.OverrideBlend) throw new ArgumentException("Игровой снимок читает фактическую позу: отключите override и временное смещение.");
            string directory=r.OutputDirectory;
            if(string.IsNullOrEmpty(directory)) directory=Path.Combine("Temp/HandPoseFit",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-runtime-"+Guid.NewGuid().ToString("N").Substring(0,6));
            Jobs.Add(new Job{Grabber=grabber,Root=geometryRoot,Settings=r,Completed=completed,Remaining=count,Interval=framesApart,NextFrame=Time.frameCount,Deadline=EditorApplication.timeSinceStartup+30,Directory=directory});
        }

        static void HandleAvatarsUpdated()
        {
            for(int i=Jobs.Count-1;i>=0;i--) {
                var job=Jobs[i];job.Deadline=EditorApplication.timeSinceStartup+30;if(Time.frameCount<job.NextFrame||Snapshots.Count>=4) continue;
                var r=job.Settings.Copy();r.OutputDirectory=Path.Combine(job.Directory,"frame-"+job.Index.ToString("D3"));
                var report=new HandPoseFitReport{CreatedUtc=DateTime.UtcNow.ToString("O"),UnityVersion=Application.unityVersion,Settings=r};
                try {
                    // Здесь BakeMesh и координаты копируются до следующего игрового кадра.
                    var snapshot=new HandPoseFitSnapshot(r,report,job.Grabber,job.Root);
                    Snapshots.Enqueue(new Frozen{Snapshot=snapshot,Report=report,Completed=job.Completed});LastCapturedFrame=Time.frameCount;
                    job.Index++;job.Remaining--;job.NextFrame=Time.frameCount+job.Interval;job.Deadline=EditorApplication.timeSinceStartup+30;
                    if(job.Remaining==0) Jobs.RemoveAt(i);
                } catch(Exception e) {Jobs.RemoveAt(i);report.Status="invalid_runtime_context";report.Error=e.Message;Snapshots.Enqueue(new Frozen{Report=report,Completed=job.Completed});}
            }
        }

        static void HandleEditorUpdate()
        {
            for(int i=Jobs.Count-1;i>=0;i--) if(EditorApplication.timeSinceStartup>Jobs[i].Deadline) {
                var job=Jobs[i];Jobs.RemoveAt(i);Snapshots.Enqueue(new Frozen{Report=new HandPoseFitReport{Status="runtime_capture_timeout",Error="Не получено обновление SDK за 30 секунд. Проверьте фокус/паузу/активный аватар."},Completed=job.Completed});
            }
            if(Snapshots.Count==0) return;
            var frozen=Snapshots.Dequeue();
            try {if(frozen.Snapshot!=null) HandPoseFitAnalyzer.AnalyzeCaptured(frozen.Snapshot,frozen.Report);}
            finally {frozen.Snapshot?.Dispose();}
            frozen.Completed?.Invoke(frozen.Report);
        }

        public static void Cancel()
        {
            Jobs.Clear();while(Snapshots.Count>0) Snapshots.Dequeue().Snapshot?.Dispose();
        }
        static void HandlePlayModeChanged(PlayModeStateChange state) {if(state==PlayModeStateChange.ExitingPlayMode||state==PlayModeStateChange.EnteredEditMode) Cancel();}
        static void HandleReload() {Cancel();UxrManager.AvatarsUpdated-=HandleAvatarsUpdated;EditorApplication.update-=HandleEditorUpdate;EditorApplication.playModeStateChanged-=HandlePlayModeChanged;AssemblyReloadEvents.beforeAssemblyReload-=HandleReload;}
    }
}
