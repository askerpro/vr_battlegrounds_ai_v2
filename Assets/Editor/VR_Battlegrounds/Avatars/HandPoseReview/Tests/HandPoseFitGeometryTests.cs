using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    public class HandPoseFitGeometryTests
    {
        static FitTriangle Plane(float z = 0) => new FitTriangle(new Vector3(-1,-1,z), new Vector3(1,-1,z), new Vector3(0,1,z));

        [Test] public void DistanceToSurfaceUsesTriangleInterior()
        { Assert.That(new FitSurface(new[] { Plane() }).Distance(new Vector3(0,0,0.007f)), Is.EqualTo(0.007f).Within(1e-7)); }

        [Test] public void CrossingTrianglesAreDetected()
        { Assert.That(HandPoseFitGeometry.Intersects(Plane(), new FitTriangle(new Vector3(0,0,-1),new Vector3(0,0,1),Vector3.right)), Is.True); }

        [Test] public void CoplanarOverlapDetectedButDisjointNotDetected()
        {
            Assert.That(HandPoseFitGeometry.Intersects(Plane(), new FitTriangle(Vector3.zero,Vector3.right,Vector3.up)), Is.True);
            Assert.That(HandPoseFitGeometry.Intersects(Plane(), new FitTriangle(new Vector3(4,4,0),new Vector3(5,4,0),new Vector3(4,5,0))), Is.False);
        }

        [Test] public void ClosedCubeSupportsInsideAndOutside()
        {
            var cube = Cube(); var topology = HandPoseFitGeometry.Topology(cube); var surface = new FitSurface(cube);
            Assert.That(topology.CanDetermineInside, Is.True);
            Assert.That(HandPoseFitGeometry.IsInside(Vector3.zero,surface,topology), Is.True);
            Assert.That(HandPoseFitGeometry.IsInside(Vector3.one*2,surface,topology), Is.False);
        }

        [Test] public void OpenAndReversedMeshesReturnUnknown()
        {
            var open = Cube().Take(11).ToArray();
            Assert.That(HandPoseFitGeometry.IsInside(Vector3.zero,new FitSurface(open),HandPoseFitGeometry.Topology(open)), Is.Null);
            var bad = Cube(); var t = bad[0]; bad[0] = new FitTriangle(t.A,t.C,t.B);
            Assert.That(HandPoseFitGeometry.Topology(bad).CanDetermineInside, Is.False);
        }

        [Test] public void AreaSamplingIsDeterministicAndPreservesTotalArea()
        {
            var ts = new[] { Plane(), new FitTriangle(Vector3.zero,Vector3.right,Vector3.up) };
            var a = HandPoseFitGeometry.Sample(ts,4000,42); var b = HandPoseFitGeometry.Sample(ts,4000,42);
            Assert.That(a.Count, Is.EqualTo(4000));
            Assert.That(a.Sum(s=>s.Area), Is.EqualTo(ts.Sum(t=>t.Area)).Within(1e-4));
            Assert.That(a[100].Position, Is.EqualTo(b[100].Position));
            Assert.That(a.Count(s=>s.Triangle==0), Is.InRange(3100,3300));
        }

        [Test] public void DegenerateTriangleStillHasFiniteUnsignedDistance()
        {
            var line=new FitTriangle(Vector3.zero,Vector3.zero,Vector3.up);
            Assert.That(new FitSurface(new[]{line}).Distance(new Vector3(.003f,.5f,0)),Is.EqualTo(.003f).Within(1e-7));
        }

        [Test] public void ClosedCavityIsOutsideSolid()
        {
            var inner=Cube().Select(t=>new FitTriangle(t.A*.5f,t.C*.5f,t.B*.5f));
            var hollow=Cube().Concat(inner).ToArray();var top=HandPoseFitGeometry.Topology(hollow);var s=new FitSurface(hollow);
            Assert.That(top.CanDetermineInside,Is.True);
            Assert.That(HandPoseFitGeometry.IsInside(Vector3.zero,s,top),Is.False);
            Assert.That(HandPoseFitGeometry.IsInside(new Vector3(.75f,0,0),s,top),Is.True);
        }

        [Test] public void FullyContainedMeshHasNoSurfaceCrossingButInsideSamples()
        {
            var shell=Cube();var surface=new FitSurface(shell);var inner=Cube().Select(t=>new FitTriangle(t.A*.1f,t.B*.1f,t.C*.1f)).ToArray();
            Assert.That(inner.Sum(surface.IntersectionCount),Is.Zero);
            Assert.That(HandPoseFitGeometry.IsInside(inner[0].A,surface,HandPoseFitGeometry.Topology(shell)),Is.True);
        }

        [Test] public void NearestPointOutsideTriangleAndScaledUnitsAreCorrect()
        {
            Assert.That(new FitSurface(new[]{Plane()}).Distance(new Vector3(2,-1,0)),Is.EqualTo(1).Within(1e-6));
            var t=Plane();var scaled=new FitTriangle(t.A*.01f,t.B*.01f,t.C*.01f);
            Assert.That(new FitSurface(new[]{scaled}).Distance(new Vector3(0,0,.005f)),Is.EqualTo(.005f).Within(1e-7));
        }

        [Test] public void ContactPairsIncludeHalfAndTwoMillimetersButExcludeTwoAndHalf()
        {
            var method=typeof(HandPoseFitGeometry).GetMethod("ContactCandidates");Assert.That(method,Is.Not.Null,"Нет расчёта пар контакта.");
            var weapon=new FitSurface(new[]{Plane()});var topology=HandPoseFitGeometry.Topology(weapon.Triangles);
            var near=new FitTriangle(new Vector3(-.01f,-.01f,.0005f),new Vector3(.01f,-.01f,.0005f),new Vector3(0,.01f,.0005f));
            var far=new FitTriangle(near.A+Vector3.forward*.002f,near.B+Vector3.forward*.002f,near.C+Vector3.forward*.002f);
            var boundary=new FitTriangle(near.A+Vector3.forward*.0015f,near.B+Vector3.forward*.0015f,near.C+Vector3.forward*.0015f);
            var included=(System.Collections.IList)method.Invoke(null,new object[]{new[]{near},weapon,weapon,topology,100,42,0f,2f});
            var excluded=(System.Collections.IList)method.Invoke(null,new object[]{new[]{far},weapon,weapon,topology,100,42,0f,2f});
            var exact=(System.Collections.IList)method.Invoke(null,new object[]{new[]{boundary},weapon,weapon,topology,100,42,0f,2f});
            var lowerBound=(System.Collections.IList)method.Invoke(null,new object[]{new[]{near},weapon,weapon,topology,100,42,.6f,2f});
            Assert.That(included.Count,Is.EqualTo(100));Assert.That(excluded.Count,Is.Zero);Assert.That(lowerBound.Count,Is.Zero);
            Assert.That(exact.Count,Is.EqualTo(100));
            object marker=included[0];var type=marker.GetType();
            Assert.That(((Vector3)type.GetField("TargetPosition").GetValue(marker)).z,Is.EqualTo(0));
            Assert.That((float)type.GetField("DistanceMm").GetValue(marker),Is.EqualTo(.5f).Within(1e-5));
            Assert.That(type.GetField("Inside").GetValue(marker),Is.Null);
        }

        [Test] public void NearestPointReturnsEndpointOnWeaponSurface()
        {
            var method=typeof(FitSurface).GetMethod("NearestPoint");Assert.That(method,Is.Not.Null,"Нет ближайшей точки поверхности.");
            var p=(Vector3)method.Invoke(new FitSurface(new[]{Plane()}),new object[]{new Vector3(0,0,.007f)});
            Assert.That(p,Is.EqualTo(Vector3.zero));
        }

        public static FitTriangle[] Cube()
        {
            var p = new[] {new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};
            int[] ix = {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
            return Enumerable.Range(0,12).Select(i=>new FitTriangle(p[ix[i*3]],p[ix[i*3+1]],p[ix[i*3+2]])).ToArray();
        }

        [Test] public void NearestHitPreservesFaceNormalAndIndex()
        {
            var hit=new FitSurface(new[]{Plane(),Plane(1)}).NearestHit(new Vector3(0,0,.001f));
            Assert.That(hit.Triangle,Is.EqualTo(0));Assert.That(hit.Normal,Is.EqualTo(Vector3.forward));
        }

        [Test] public void ContactNormalOppositionSeparatesFacingAndParallelSurfaces()
        {
            var weapon=new FitSurface(new[]{Plane()});var top=HandPoseFitGeometry.Topology(weapon.Triangles);var t=Plane(.001f);
            var facing=new FitTriangle(t.A,t.C,t.B);var a=HandPoseFitGeometry.ContactCandidates(new[]{facing},weapon,weapon,top,10,42,0,2);
            Assert.That(a[0].NormalOppositionDot,Is.EqualTo(1f).Within(1e-6));
            var b=HandPoseFitGeometry.ContactCandidates(new[]{t},weapon,weapon,top,10,42,0,2);
            Assert.That(b[0].NormalOppositionDot,Is.EqualTo(-1f).Within(1e-6));
        }

        [Test] public void ContactPatchesJoinAdjacentFacesAndKeepIsolatedFace()
        {
            var ts=new[]{new FitTriangle(Vector3.zero,Vector3.right,Vector3.up),new FitTriangle(Vector3.right,new Vector3(1,1,0),Vector3.up),new FitTriangle(Vector3.one*5,Vector3.one*5+Vector3.right,Vector3.one*5+Vector3.up)};
            var pairs=new System.Collections.Generic.List<FitContactCandidate>{new FitContactCandidate{Triangle=0,AreaMm2=10},new FitContactCandidate{Triangle=1,AreaMm2=20},new FitContactCandidate{Triangle=2,AreaMm2=40}};
            var result=HandPoseFitGeometry.ContactPatches(ts,pairs);Assert.That(result.Count,Is.EqualTo(2));
            Assert.That(result.Select(p=>p.AreaMm2).OrderBy(x=>x).ToArray(),Is.EqualTo(new[]{30f,40f}));
        }

        [Test] public void SelfCrossingSkipsSharedVerticesButDetectsSeparateCrossing()
        {
            var crossing=new FitTriangle(new Vector3(0,0,-1),new Vector3(0,0,1),new Vector3(.2f,0,0));
            Assert.That(HandPoseFitGeometry.SelfIntersectionPairs(new[]{Plane(),crossing}),Is.EqualTo(1));
            Assert.That(HandPoseFitGeometry.SelfIntersectionPairs(new[]{Plane(),Plane()}),Is.EqualTo(0));
        }
    }
}
