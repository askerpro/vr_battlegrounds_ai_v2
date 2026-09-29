// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Runtime.Core
{
    public struct KTwoBoneIkData
    {
        public KTransform root;
        public KTransform mid;
        public KTransform tip;
        public KTransform target;
        public KTransform hint;

        public float posWeight;
        public float rotWeight;
        public float hintWeight;

        public bool allowStretching;
        public float startStretchRatio;
        public float maxStretchScale;

        public bool hasValidHint;
    }
    
    public class KTwoBoneIK
    {
        public static void Solve(ref KTwoBoneIkData ikData)
        {
            Vector3 rootPosition = ikData.root.position;
            Vector3 jointPosition = ikData.mid.position;
            Vector3 endPosition = ikData.tip.position;

            Vector3 effectorPosition = Vector3.Lerp(endPosition, ikData.target.position, ikData.posWeight);
            Quaternion effectorRotation = Quaternion.Slerp(ikData.tip.rotation, ikData.target.rotation, ikData.rotWeight);

            float upperLimbLength = (jointPosition - rootPosition).magnitude;
            float lowerLimbLength = (endPosition - jointPosition).magnitude;
            float referenceLength = Mathf.Max(upperLimbLength, lowerLimbLength, 1f);

            Vector3 jointTarget = BuildJointTarget(ikData, effectorPosition, referenceLength);

            SolveTwoBoneIK(ref ikData.root, ref ikData.mid, ref ikData.tip, jointTarget, effectorPosition,
                upperLimbLength, lowerLimbLength, ikData.allowStretching, ikData.startStretchRatio,
                Mathf.Max(ikData.maxStretchScale, 1f));

            ikData.tip.rotation = effectorRotation;
        }

        private static void SolveTwoBoneIK(ref KTransform root, ref KTransform joint, ref KTransform end,
            Vector3 jointTarget, Vector3 effector, float upperLimbLength, float lowerLimbLength,
            bool allowStretching, float startStretchRatio, float maxStretchScale)
        {
            Vector3 rootPosition = root.position;
            Vector3 jointPosition = joint.position;
            Vector3 endPosition = end.position;

            SolveTwoBoneIK(rootPosition, jointPosition, jointTarget, effector, 
                out Vector3 outJointPos, out Vector3 outEndPos, upperLimbLength, lowerLimbLength, allowStretching, 
                startStretchRatio, maxStretchScale);

            Quaternion rootDelta = FindBetweenNormals(jointPosition - rootPosition, outJointPos - rootPosition);
            root.rotation = rootDelta * root.rotation;
            root.position = rootPosition;

            Quaternion jointDelta = FindBetweenNormals(endPosition - jointPosition, outEndPos - outJointPos);
            joint.rotation = jointDelta * joint.rotation;
            joint.position = outJointPos;

            end.position = outEndPos;
        }

        private static void SolveTwoBoneIK(Vector3 rootPos, Vector3 jointPos, Vector3 jointTarget,
            Vector3 effector, out Vector3 outJointPos, out Vector3 outEndPos, float upperLimbLength,
            float lowerLimbLength, bool allowStretching, float startStretchRatio, float maxStretchScale)
        {
            Vector3 desiredPos = effector;
            Vector3 desiredDelta = desiredPos - rootPos;
            float desiredLength = desiredDelta.magnitude;

            Vector3 desiredDir;
            if (desiredLength < KMath.FloatMin)
            {
                desiredLength = KMath.FloatMin;
                desiredDir = Vector3.right;
            }
            else
            {
                desiredDir = desiredDelta / desiredLength;
            }

            Vector3 jointTargetDelta = jointTarget - rootPos;
            Vector3 jointBendDir;

            if (jointTargetDelta.sqrMagnitude < KMath.SqrEpsilon)
            {
                jointBendDir = Vector3.up;
            }
            else
            {
                Vector3 jointPlaneNormal = Vector3.Cross(desiredDir, jointTargetDelta);

                if (jointPlaneNormal.sqrMagnitude < KMath.SqrEpsilon)
                {
                    FindBestAxisVectors(desiredDir, out jointPlaneNormal, out jointBendDir);
                }
                else
                {
                    jointPlaneNormal.Normalize();
                    jointBendDir = jointTargetDelta - Vector3.Dot(jointTargetDelta, desiredDir) * desiredDir;

                    if (jointBendDir.sqrMagnitude < KMath.SqrEpsilon)
                    {
                        FindBestAxisVectors(desiredDir, out jointPlaneNormal, out jointBendDir);
                    }
                    else
                    {
                        jointBendDir.Normalize();
                    }
                }
            }

            float maxLimbLength = lowerLimbLength + upperLimbLength;
            if (allowStretching)
            {
                float scaleRange = maxStretchScale - startStretchRatio;
                if (scaleRange > KMath.FloatMin && maxLimbLength > KMath.FloatMin)
                {
                    float reachRatio = desiredLength / maxLimbLength;
                    float scalingFactor = (maxStretchScale - 1f) *
                                          Mathf.Clamp01((reachRatio - startStretchRatio) / scaleRange);
                    if (scalingFactor > KMath.FloatMin)
                    {
                        float lengthScale = 1f + scalingFactor;
                        lowerLimbLength *= lengthScale;
                        upperLimbLength *= lengthScale;
                        maxLimbLength *= lengthScale;
                    }
                }
            }

            outEndPos = desiredPos;
            outJointPos = jointPos;

            if (desiredLength >= maxLimbLength)
            {
                outEndPos = rootPos + maxLimbLength * desiredDir;
                outJointPos = rootPos + upperLimbLength * desiredDir;
                return;
            }

            float twoAB = 2f * upperLimbLength * desiredLength;
            float cosAngle = twoAB > KMath.FloatMin
                ? ((upperLimbLength * upperLimbLength) + (desiredLength * desiredLength) -
                   (lowerLimbLength * lowerLimbLength)) / twoAB
                : 0f;
            cosAngle = Mathf.Clamp(cosAngle, -1f, 1f);

            bool reverseUpperBone = cosAngle < 0f;
            float angle = Mathf.Acos(cosAngle);
            float jointLineDist = upperLimbLength * Mathf.Sin(angle);

            float projJointDistSqr = (upperLimbLength * upperLimbLength) - (jointLineDist * jointLineDist);
            float projJointDist = projJointDistSqr > 0f ? Mathf.Sqrt(projJointDistSqr) : 0f;
            if (reverseUpperBone)
            {
                projJointDist *= -1f;
            }

            outJointPos = rootPos + (projJointDist * desiredDir) + (jointLineDist * jointBendDir);
        }

        private static Vector3 BuildJointTarget(in KTwoBoneIkData ikData, Vector3 effectorPos, float targetDistance)
        {
            Vector3 rootPos = ikData.root.position;
            Vector3 jointPos = ikData.mid.position;
            Vector3 endPos = ikData.tip.position;
            
            Vector3 desiredDir = GetDesiredDirection(rootPos, effectorPos, endPos);
            Vector3 bendDir = GetCurrentBendDirection(rootPos, jointPos, endPos, desiredDir) * targetDistance;
            bool canProject = TryProjectToBendPlane(ikData.hint.position - rootPos, desiredDir, 
                out Vector3 hintDir);

            if (ikData is {hasValidHint: true, hintWeight: > KMath.FloatMin} && canProject)
            {
                float clampedHintWeight = Mathf.Clamp01(ikData.hintWeight);
                bendDir = bendDir.sqrMagnitude < KMath.SqrEpsilon
                    ? hintDir
                    : Vector3.Slerp(bendDir, hintDir, clampedHintWeight).normalized;
            }

            if (bendDir.sqrMagnitude < KMath.SqrEpsilon)
            {
                FindBestAxisVectors(desiredDir, out _, out bendDir);
            }

            return jointPos + bendDir;
        }

        private static Vector3 GetDesiredDirection(Vector3 rootPos, Vector3 effectorPos, Vector3 endPos)
        {
            /*
            Vector3 desiredDelta = effectorPos - rootPos;
            if (desiredDelta.sqrMagnitude >= KMath.SqrEpsilon)
            {
                return desiredDelta.normalized;
            }*/

            Vector3 fallback = endPos - rootPos;
            return fallback.normalized;
        }

        public static Vector3 GetCurrentBendDirection(Vector3 rootPos, Vector3 jointPos, Vector3 endPos,
            Vector3 desiredDir)
        {
            if (TryProjectToBendPlane(jointPos - rootPos, desiredDir, out Vector3 bendDir))
            {
                return bendDir;
            }

            Vector3 upper = jointPos - rootPos;
            Vector3 lower = endPos - jointPos;
            Vector3 planeNormal = Vector3.Cross(upper, lower);
            if (planeNormal.sqrMagnitude >= KMath.SqrEpsilon)
            {
                bendDir = Vector3.Cross(planeNormal.normalized, desiredDir);
                if (bendDir.sqrMagnitude >= KMath.SqrEpsilon)
                {
                    return bendDir.normalized;
                }
            }

            FindBestAxisVectors(desiredDir, out _, out bendDir);
            return bendDir;
        }

        private static bool TryProjectToBendPlane(Vector3 vector, Vector3 planeNormal, out Vector3 projectedDir)
        {
            Vector3 projected = vector - Vector3.Dot(vector, planeNormal) * planeNormal;
            if (projected.sqrMagnitude < KMath.SqrEpsilon)
            {
                projectedDir = Vector3.zero;
                return false;
            }

            projectedDir = projected.normalized;
            return true;
        }

        private static void FindBestAxisVectors(Vector3 direction, out Vector3 axis1, out Vector3 axis2)
        {
            Vector3 basis = Mathf.Abs(direction.y) < 0.999f ? Vector3.up : Vector3.right;

            axis1 = Vector3.Cross(direction, basis);
            if (axis1.sqrMagnitude < KMath.SqrEpsilon)
            {
                basis = Vector3.forward;
                axis1 = Vector3.Cross(direction, basis);
            }

            axis1.Normalize();
            axis2 = Vector3.Cross(axis1, direction).normalized;
        }

        private static Quaternion FindBetweenNormals(Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude < KMath.SqrEpsilon || to.sqrMagnitude < KMath.SqrEpsilon)
            {
                return Quaternion.identity;
            }

            return KMath.FromToRotation(from.normalized, to.normalized);
        }
    }
}