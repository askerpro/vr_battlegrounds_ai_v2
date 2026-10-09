using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json;
using VrBattlegrounds.Editor.HandRigQuality;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    /// <summary>Математические контракты диагностики; не закрепляют новую игровую механику.</summary>
    public class HandRigQualityDiagnosticTests
    {
        static Type MathType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("VrBattlegrounds.Editor.HandRigQuality.HandRigQualityMath"))
            .FirstOrDefault(t => t != null);

        static object Call(string method, params object[] args)
        {
            Assert.That(MathType, Is.Not.Null, "Отсутствует реализация математического ядра анализатора.");
            return MathType.GetMethod(method, BindingFlags.Static | BindingFlags.Public).Invoke(null, args);
        }

        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);

        static readonly Vector3[] Cube = {
            new Vector3(-.01f,-.02f,-.01f),new Vector3(.01f,-.02f,-.01f),
            new Vector3(.01f,-.02f,.01f),new Vector3(-.01f,-.02f,.01f),
            new Vector3(-.01f,.02f,-.01f),new Vector3(.01f,.02f,-.01f),
            new Vector3(.01f,.02f,.01f),new Vector3(-.01f,.02f,.01f)
        };
        static readonly int[] Sides = {0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7};

        [Test] public void ClosedSectionMeasuresAreaAndCenterInMeters()
        {
            var section=Call("Section", Cube, Sides, Vector3.zero, Vector3.up);
            Assert.That(Field<bool>(section,"Valid"), Is.True);
            Assert.That(Field<float>(section,"Area"), Is.EqualTo(.0004f).Within(1e-8f));
            Assert.That(Field<Vector3>(section,"Center").magnitude, Is.LessThan(1e-6f));
        }

        [Test] public void SectionRejectsOpenSurfaceInsteadOfInventingRadius()
        {
            var section=Call("Section", Cube, Sides.Take(6).ToArray(), Vector3.zero, Vector3.up);
            Assert.That(Field<bool>(section,"Valid"), Is.False);
            Assert.That(Field<string>(section,"Reason"), Is.Not.Empty);
        }

        [Test] public void JointOffsetRemainsObservableInSectionCenter()
        {
            var offset=new Vector3(.004f,0,0);
            var section=Call("Section", Cube.Select(v=>v+offset).ToArray(), Sides, Vector3.zero, Vector3.up);
            Assert.That(Field<bool>(section,"Valid"), Is.True);
            Assert.That(Field<Vector3>(section,"Center").x, Is.EqualTo(.004f).Within(1e-6f));
        }

        [Test] public void MultipleSectionLoopsAreAmbiguous()
        {
            var vertices=Cube.Concat(Cube.Select(v=>v+Vector3.right*.05f)).ToArray();
            var indices=Sides.Concat(Sides.Select(i=>i+Cube.Length)).ToArray();
            Assert.That(Field<bool>(Call("Section",vertices,indices,Vector3.zero,Vector3.up),"Valid"), Is.False);
        }

        [Test] public void FingerSectionSelectsOwnedLoopAndPreservesJointOffset()
        {
            var vertices=Cube.Select(v=>v+Vector3.right*.004f).Concat(Cube.Select(v=>v+Vector3.right*.05f)).ToArray();
            var indices=Sides.Concat(Sides.Select(i=>i+Cube.Length)).ToArray();
            var owners=Enumerable.Repeat(1f,Cube.Length).Concat(Enumerable.Repeat(0f,Cube.Length)).ToArray();
            var section=Call("FingerSection",vertices,indices,owners,owners,Vector3.zero,Vector3.up);
            Assert.That(Field<bool>(section,"Valid"),Is.True);
            Assert.That(Field<float>(section,"Area"),Is.EqualTo(.0004f).Within(1e-8f));
            Assert.That(Field<Vector3>(section,"Center").x,Is.EqualTo(.004f).Within(1e-6f));
        }

        [Test] public void FingerSectionRejectsTwoOwnedSurfaces()
        {
            var vertices=Cube.Concat(Cube.Select(v=>v+Vector3.right*.05f)).ToArray();
            var indices=Sides.Concat(Sides.Select(i=>i+Cube.Length)).ToArray();
            var owners=Enumerable.Repeat(1f,vertices.Length).ToArray();
            Assert.That(Field<bool>(Call("FingerSection",vertices,indices,owners,owners,Vector3.zero,Vector3.up),"Valid"),Is.False);
        }

        [Test] public void FingerSectionRejectsOpenOwnedContour()
        {
            var owners=Enumerable.Repeat(1f,Cube.Length).ToArray();
            Assert.That(Field<bool>(Call("FingerSection",Cube,Sides.Take(6).ToArray(),owners,owners,Vector3.zero,Vector3.up),"Valid"),Is.False);
        }

        [Test] public void FingerSectionWeightUsesOnlySelectedContour()
        {
            var vertices=Cube.Concat(Cube.Select(v=>v+Vector3.right*.05f)).ToArray();
            var indices=Sides.Concat(Sides.Select(i=>i+Cube.Length)).ToArray();
            var owners=Enumerable.Repeat(1f,Cube.Length).Concat(Enumerable.Repeat(0f,Cube.Length)).ToArray();
            var child=Cube.Select(v=>v.y<0?0f:1f).Concat(Enumerable.Repeat(1f,Cube.Length)).ToArray();
            var section=Call("FingerSection",vertices,indices,child,owners,Vector3.zero,Vector3.up);
            Assert.That(Field<bool>(section,"Valid"),Is.True);
            Assert.That(Field<float>(section,"MeanWeight"),Is.EqualTo(.5f).Within(1e-6f));
        }

        [Test] public void FingerSupportIntegratesThresholdCrossingAndIgnoresSubdivision()
        {
            var vertices=Cube.Select(v=>Vector3.Scale(v,new Vector3(1,1,.1f))).ToArray();
            var owner=vertices.Select(v=>v.x<0?0f:.2f).ToArray();var child=Enumerable.Repeat(1f,vertices.Length).ToArray();
            var coarse=Call("FingerSection",vertices,Sides,child,owner,Vector3.zero,Vector3.up);
            var refined=vertices.ToList();var refinedOwner=owner.ToList();var indices=new System.Collections.Generic.List<int>();
            for(int i=0;i<Sides.Length;i+=3)
            {
                int a=Sides[i],b=Sides[i+1],c=Sides[i+2],m=refined.Count;
                refined.Add((vertices[a]+vertices[b])*.5f);refinedOwner.Add((owner[a]+owner[b])*.5f);
                indices.AddRange(new[]{a,m,c,m,b,c});
            }
            var fine=Call("FingerSection",refined.ToArray(),indices.ToArray(),Enumerable.Repeat(1f,refined.Count).ToArray(),refinedOwner.ToArray(),Vector3.zero,Vector3.up);
            Assert.That(Field<bool>(coarse,"Valid"),Is.False);Assert.That(Field<bool>(fine,"Valid"),Is.False);
            Assert.That(Field<float>(coarse,"FingerSupportFraction"),Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(Field<float>(fine,"FingerSupportFraction"),Is.EqualTo(.5f).Within(1e-5f));
        }

        [Test] public void CoincidentRendererContoursPreserveSurfaceIdentity()
        {
            var vertices=Cube.Concat(Cube).ToArray();var indices=Sides.Concat(Sides.Select(i=>i+Cube.Length)).ToArray();
            var weights=Enumerable.Repeat(1f,vertices.Length).ToArray();
            var surfaces=Enumerable.Repeat(0,Cube.Length).Concat(Enumerable.Repeat(1,Cube.Length)).ToArray();
            Assert.That(Field<bool>(Call("FingerSectionBySurface",vertices,indices,weights,weights,surfaces,Vector3.zero,Vector3.up),"Valid"),Is.False);
            var owners=Enumerable.Repeat(1f,Cube.Length).Concat(Enumerable.Repeat(0f,Cube.Length)).ToArray();
            Assert.That(Field<bool>(Call("FingerSectionBySurface",vertices,indices,weights,owners,surfaces,Vector3.zero,Vector3.up),"Valid"),Is.True);
        }

        [Test] public void CoincidentFacesWithinRendererRemainAmbiguous()
        {
            var vertices=Cube.Concat(Cube).ToArray();var indices=Sides.Concat(Sides.Select(i=>i+Cube.Length)).ToArray();
            var weights=Enumerable.Repeat(1f,vertices.Length).ToArray();
            Assert.That(Field<bool>(Call("FingerSection",vertices,indices,weights,weights,Vector3.zero,Vector3.up),"Valid"),Is.False);
        }

        [Test] public void ConflictingSeamWeightsDoNotBecomeOrderDependentAverage()
        {
            var vertices=Sides.Select(i=>Cube[i]).ToArray();var indices=Enumerable.Range(0,vertices.Length).ToArray();
            var child=Enumerable.Range(0,vertices.Length).Select(i=>i/6==0?.2f:.8f).ToArray();
            var owners=Enumerable.Repeat(1f,vertices.Length).ToArray();
            var section=Call("FingerSection",vertices,indices,child,owners,Vector3.zero,Vector3.up);
            Assert.That(Field<bool>(section,"Valid"),Is.True);
            Assert.That(float.IsNaN(Field<float>(section,"MeanWeight")),Is.True);
            var reverse=Call("FingerSection",vertices,indices.Reverse().ToArray(),child,owners,Vector3.zero,Vector3.up);
            Assert.That(Field<bool>(reverse,"Valid"),Is.True);Assert.That(float.IsNaN(Field<float>(reverse,"MeanWeight")),Is.True);
        }

        [Test] public void SampledTransitionRequiresBracketedSignChange()
        {
            var result=Call("SampledWeightCrossing",new float?[]{.4f,.5f,.6f},-.001f,.001f);
            Assert.That(Field<bool>(result,"Valid"),Is.True);Assert.That(Field<float>(result,"PositionMeters"),Is.EqualTo(0).Within(1e-7f));
        }

        [Test] public void SampledTransitionRejectsTouchWithoutSignChange()
        {Assert.That(Field<bool>(Call("SampledWeightCrossing",new float?[]{.6f,.5f,.6f},-.001f,.001f),"Valid"),Is.False);}

        [Test] public void SampledTransitionRejectsPlateau()
        {Assert.That(Field<bool>(Call("SampledWeightCrossing",new float?[]{.4f,.5f,.5f,.6f},-.001f,.001f),"Valid"),Is.False);}

        [Test] public void SampledTransitionRejectsUnknownIntervals()
        {Assert.That(Field<bool>(Call("SampledWeightCrossing",new float?[]{.4f,null,.6f},-.001f,.001f),"Valid"),Is.False);}

        [Test] public void WeightEnergyDetectsDiscontinuityAndIgnoresUniformScale()
        {
            var smooth=Cube.Select(_=>new[]{.5f,.5f}).ToArray();
            var rough=Cube.Select((_,i)=>i%2==0?new[]{1f,0f}:new[]{0f,1f}).ToArray();
            Assert.That((float)Call("WeightEnergy",Cube,Sides,smooth), Is.Zero);
            float energy=(float)Call("WeightEnergy",Cube,Sides,rough);
            Assert.That(energy, Is.GreaterThan(0));
            Assert.That((float)Call("WeightEnergy",Cube.Select(v=>v*2).ToArray(),Sides,rough), Is.EqualTo(energy).Within(1e-7f));
        }

        [Test] public void CuffLeakageUsesSurfaceAreaInsteadOfVertexCount()
        {
            var vertices=new[]{new Vector3(0,.04f,0),new Vector3(.02f,.04f,0),new Vector3(0,.04f,.02f),
                new Vector3(0,.06f,0),new Vector3(.01f,.06f,0),new Vector3(0,.06f,.01f)};
            var result=Call("CuffLeakage",vertices,new[]{0,1,2,3,4,5},new[]{1f,1f,1f,0f,0f,0f},Vector3.zero,Vector3.up,.02f,.08f,.05f);
            Assert.That(Field<bool>(result,"Valid"), Is.True);
            Assert.That(Field<float>(result,"WeightMean"), Is.EqualTo(.8f).Within(1e-6f));
        }

        [Test] public void EmptyCuffIsUnavailableNotZeroLeakage()
        {
            var result=Call("CuffLeakage",Cube,Sides,Cube.Select(_=>0f).ToArray(),Vector3.zero,Vector3.up,.05f,.08f,.05f);
            Assert.That(Field<bool>(result,"Valid"), Is.False);
        }

        [Test] public void PalmFrameDoesNotRotateWithTriangleSubdivision()
        {
            var vertices=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
            for(int strip=0;strip<3;strip++)
            {
                float a=-.03f+strip*.02f,b=a+.02f;int offset=vertices.Count;
                vertices.AddRange(new[]{new Vector3(a,0,-.05f),new Vector3(b,0,-.05f),new Vector3(b,0,.05f),new Vector3(a,0,.05f)});
                triangles.AddRange(new[]{offset,offset+1,offset+2,offset,offset+2,offset+3});
            }
            Assert.That(MathType,Is.Not.Null);
            var args=new object[]{vertices.ToArray(),triangles.ToArray(),vertices.Select(_=>1f).ToArray(),Vector3.forward,Vector3.up,Vector3.zero,Vector3.zero};
            Assert.That((bool)MathType.GetMethod("PalmAxes").Invoke(null,args),Is.True);
            Assert.That(Vector3.Angle((Vector3)args[5],Vector3.forward),Is.LessThan(.1f),"Точная площадная PCA прямоугольника направлена вдоль его длинной стороны.");
        }

        [Test] public void SquarePalmHasNoUniqueLongitudinalAxis()
        {
            var vertices=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
            for(int x=0;x<2;x++)for(int z=0;z<2;z++)
            {
                float a=-.02f+x*.02f,b=-.02f+z*.02f;int o=vertices.Count;
                vertices.AddRange(new[]{new Vector3(a,0,b),new Vector3(a+.02f,0,b),new Vector3(a+.02f,0,b+.02f),new Vector3(a,0,b+.02f)});
                triangles.AddRange(new[]{o,o+1,o+2,o,o+2,o+3});
            }
            var args=new object[]{vertices.ToArray(),triangles.ToArray(),vertices.Select(_=>1f).ToArray(),Vector3.forward,Vector3.up,Vector3.zero,Vector3.zero};
            Assert.That((bool)MathType.GetMethod("PalmAxes").Invoke(null,args),Is.False);
        }

        [Test] public void CalibrationRewardsGoodBandAndRejectsNonFiniteBounds()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VrBattlegrounds.Editor.HandRigQuality.HandRigQualityCalibration")).First(t=>t!=null);
            var method=type.GetMethod("Score");
            Assert.That((double)method.Invoke(null,new object[]{2d,2d,6d}),Is.EqualTo(100));
            Assert.That((double)method.Invoke(null,new object[]{6d,2d,6d}),Is.Zero);
            var failure=Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,new object[]{2d,double.NaN,6d}));
            Assert.That(failure.InnerException,Is.TypeOf<ArgumentException>());
        }

        [Test] public void ReportSerializesNullableUnityVectorsWithoutPropertiesRecursion()
        {
            var snapshot=new HandRigSnapshot {MeshForward=Vector3.forward,MeshUp=Vector3.up,Forearm=null};
            string json=JsonConvert.SerializeObject(snapshot,HandRigJsonConverter.Settings);
            var restored=JsonConvert.DeserializeObject<HandRigSnapshot>(json,HandRigJsonConverter.Settings);
            Assert.That(restored.MeshForward,Is.EqualTo(Vector3.forward));
            Assert.That(restored.MeshUp,Is.EqualTo(Vector3.up));Assert.That(restored.Forearm,Is.Null);
            Assert.That(json,Does.Not.Contain("normalized"));
        }
    }

    /// <summary>Штатный Unity TestRunner на главном потоке для синхронных диагностических тестов.</summary>
    public static class HandRigQualityUnityTestRun
    {
        public static object Run()
        {
            if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
                throw new System.InvalidOperationException("Редактор занят.");
            string directory=System.IO.Path.GetFullPath("Docs/tasks/report/hand-rig-quality");
            System.IO.Directory.CreateDirectory(directory);
            var api=ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>();
            var callbacks=new ResultCallbacks(directory,System.Threading.Thread.CurrentThread.ManagedThreadId);
            api.RegisterCallbacks(callbacks);
            try
            {
                var filter=new UnityEditor.TestTools.TestRunner.Api.Filter {
                    testMode=UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                    assemblyNames=new[]{"VrBattlegrounds.HandPoseReview.Tests"}
                };
                api.Execute(new UnityEditor.TestTools.TestRunner.Api.ExecutionSettings(filter){runSynchronously=true});
                if(!callbacks.Completed)throw new System.InvalidOperationException("Синхронный Unity TestRunner не вернул результат.");
                return callbacks.Summary;
            }
            finally{api.UnregisterCallbacks(callbacks);UnityEngine.Object.DestroyImmediate(api);}
        }

        sealed class ResultCallbacks : UnityEditor.TestTools.TestRunner.Api.ICallbacks
        {
            readonly string _directory;
            readonly int _callerThread;
            bool _sameThread=true;
            public bool Completed;
            public object Summary;
            public ResultCallbacks(string directory,int callerThread){_directory=directory;_callerThread=callerThread;}
            public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor testsToRun){}
            public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test)
            {_sameThread&=System.Threading.Thread.CurrentThread.ManagedThreadId==_callerThread;}
            public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result){}
            public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
            {
                string xmlPath=System.IO.Path.Combine(_directory,"unity-tests.xml");
                UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.SaveResultToFile(result,xmlPath);
                int total=result.PassCount+result.FailCount+result.SkipCount+result.InconclusiveCount;
                Summary=new {passed=result.PassCount==89&&result.FailCount==0&&result.SkipCount==0&&result.InconclusiveCount==0&&_sameThread,
                    passedTests=result.PassCount,failed=result.FailCount,skipped=result.SkipCount,inconclusive=result.InconclusiveCount,total,
                    resultState=result.ResultState,sameThread=_sameThread,mode="Unity_TestRunner_EditMode_synchronous",reportPath=xmlPath};
                System.IO.File.WriteAllText(System.IO.Path.Combine(_directory,"unity-tests-summary.json"),JsonConvert.SerializeObject(Summary,Formatting.Indented));
                Completed=true;
            }
        }
    }
}
