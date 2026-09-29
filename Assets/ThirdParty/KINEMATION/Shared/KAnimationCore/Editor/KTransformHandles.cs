// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Editor
{
    public static class KTransformHandles
    {
        [Serializable]
        public struct PositionAxisSettings
        {
            public Color color;
            public float length;
            public float thickness;
            public float tipSize;
            public float pickPadding;

            public static PositionAxisSettings DefaultX => new PositionAxisSettings
            {
                color = Handles.xAxisColor,
                length = 1f,
                thickness = 4f,
                tipSize = 0.16f,
                pickPadding = 4f
            };

            public static PositionAxisSettings DefaultY => new PositionAxisSettings
            {
                color = Handles.yAxisColor,
                length = 1f,
                thickness = 4f,
                tipSize = 0.16f,
                pickPadding = 4f
            };

            public static PositionAxisSettings DefaultZ => new PositionAxisSettings
            {
                color = Handles.zAxisColor,
                length = 1f,
                thickness = 4f,
                tipSize = 0.16f,
                pickPadding = 4f
            };
        }

        [Serializable]
        public struct PositionPlaneSettings
        {
            public Color color;
            public float size;
            public float fillAlpha;
            public float outlineThickness;
            public float pickPadding;

            public static PositionPlaneSettings DefaultXY => new PositionPlaneSettings
            {
                color = Handles.zAxisColor,
                size = 0.25f,
                fillAlpha = 0.18f,
                outlineThickness = 1.5f,
                pickPadding = 2f
            };

            public static PositionPlaneSettings DefaultXZ => new PositionPlaneSettings
            {
                color = Handles.yAxisColor,
                size = 0.25f,
                fillAlpha = 0.18f,
                outlineThickness = 1.5f,
                pickPadding = 2f
            };

            public static PositionPlaneSettings DefaultYZ => new PositionPlaneSettings
            {
                color = Handles.xAxisColor,
                size = 0.25f,
                fillAlpha = 0.18f,
                outlineThickness = 1.5f,
                pickPadding = 2f
            };
        }

        [Serializable]
        public struct PositionHandleSettings
        {
            public float size;
            public float hoverBrightness;
            public float hoverThicknessMultiplier;
            public Color pickHighlightColor;
            public PositionAxisSettings xAxis;
            public PositionAxisSettings yAxis;
            public PositionAxisSettings zAxis;
            public PositionPlaneSettings xyPlane;
            public PositionPlaneSettings xzPlane;
            public PositionPlaneSettings yzPlane;

            public static PositionHandleSettings Default => new PositionHandleSettings
            {
                size = 1f,
                hoverBrightness = 0.35f,
                hoverThicknessMultiplier = 1.5f,
                pickHighlightColor = Color.yellow,
                xAxis = PositionAxisSettings.DefaultX,
                yAxis = PositionAxisSettings.DefaultY,
                zAxis = PositionAxisSettings.DefaultZ,
                xyPlane = PositionPlaneSettings.DefaultXY,
                xzPlane = PositionPlaneSettings.DefaultXZ,
                yzPlane = PositionPlaneSettings.DefaultYZ
            };
        }

        [Serializable]
        public struct RotationAxisSettings
        {
            public Color color;
            public float radius;
            public float thickness;
            public float backAlpha;
            public float pickPadding;

            public static RotationAxisSettings DefaultX => new RotationAxisSettings
            {
                color = Handles.xAxisColor,
                radius = 1f,
                thickness = 3f,
                backAlpha = 0.08f,
                pickPadding = 5f
            };

            public static RotationAxisSettings DefaultY => new RotationAxisSettings
            {
                color = Handles.yAxisColor,
                radius = 1f,
                thickness = 3f,
                backAlpha = 0.08f,
                pickPadding = 5f
            };

            public static RotationAxisSettings DefaultZ => new RotationAxisSettings
            {
                color = Handles.zAxisColor,
                radius = 1f,
                thickness = 3f,
                backAlpha = 0.08f,
                pickPadding = 5f
            };
        }

        [Serializable]
        public struct FreeRotationSettings
        {
            public Color color;
            public Color pickColor;
            public float outlineThickness;
            public float fillAlpha;
            public float pickPadding;

            public static FreeRotationSettings Default => new FreeRotationSettings
            {
                color = new Color(1f, 1f, 1f, 0.9f),
                pickColor = Color.yellow,
                outlineThickness = 2f,
                fillAlpha = 0.08f,
                pickPadding = 3f
            };
        }

        [Serializable]
        public struct RotationHandleSettings
        {
            public float size;
            public float hoverBrightness;
            public float hoverThicknessMultiplier;
            public Color pickHighlightColor;
            public int segmentCount;
            public bool enableFreeRotation;
            public FreeRotationSettings freeRotation;
            public RotationAxisSettings xAxis;
            public RotationAxisSettings yAxis;
            public RotationAxisSettings zAxis;

            public static RotationHandleSettings Default => new RotationHandleSettings
            {
                size = 1f,
                hoverBrightness = 0.35f,
                hoverThicknessMultiplier = 1.5f,
                pickHighlightColor = Color.yellow,
                segmentCount = 64,
                enableFreeRotation = true,
                freeRotation = FreeRotationSettings.Default,
                xAxis = RotationAxisSettings.DefaultX,
                yAxis = RotationAxisSettings.DefaultY,
                zAxis = RotationAxisSettings.DefaultZ
            };
        }

        private struct PositionDragState
        {
            public int controlId;
            public Vector3 startPosition;
            public Vector3 axis;
            public float startParameter;
            public float fallbackDistance;
        }

        private struct PositionPlaneDragState
        {
            public int controlId;
            public Vector3 startPosition;
            public Vector3 axisA;
            public Vector3 axisB;
            public Vector3 planeNormal;
            public Vector2 startParameters;
            public float fallbackDistance;
        }

        private struct RotationDragState
        {
            public int controlId;
            public Vector3 center;
            public Vector3 axis;
            public Quaternion currentRotation;
            public Vector3 lastVector;
            public Vector2 lastMousePosition;
            public float worldRadius;
        }

        private struct FreeRotationDragState
        {
            public int controlId;
            public Quaternion currentRotation;
            public Vector3 lastTrackballVector;
            public Vector2 lastMousePosition;
            public float screenRadius;
        }

        private const int PositionXHint = 362101;
        private const int PositionYHint = 362102;
        private const int PositionZHint = 362103;
        private const int PositionXYHint = 362104;
        private const int PositionXZHint = 362105;
        private const int PositionYZHint = 362106;
        private const int RotationXHint = 362201;
        private const int RotationYHint = 362202;
        private const int RotationZHint = 362203;
        private const int RotationFreeHint = 362204;
        private const float MinHandleScale = 0.0001f;
        private const float MinVectorSqrMagnitude = 0.000001f;
        private static PositionDragState s_PositionDragState;
        private static PositionPlaneDragState s_PositionPlaneDragState;
        private static RotationDragState s_RotationDragState;
        private static FreeRotationDragState s_FreeRotationDragState;

        public static Vector3 PositionHandle(Vector3 position, Quaternion orientation)
        {
            return PositionHandle(position, orientation, PositionHandleSettings.Default);
        }

        public static Vector3 PositionHandle(Vector3 position, Quaternion orientation, PositionHandleSettings settings)
        {
            settings = Sanitize(settings);
            orientation = NormalizeQuaternion(orientation);

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;

            try
            {
                Handles.matrix = Matrix4x4.identity;

                Vector3 xAxis = orientation * Vector3.right;
                Vector3 yAxis = orientation * Vector3.up;
                Vector3 zAxis = orientation * Vector3.forward;

                position = DrawPositionPlanes(position, xAxis, yAxis, zAxis, settings);

                position = DrawPositionAxis(position, xAxis, settings.xAxis, settings, PositionXHint);
                position = DrawPositionAxis(position, yAxis, settings.yAxis, settings, PositionYHint);
                position = DrawPositionAxis(position, zAxis, settings.zAxis, settings, PositionZHint);

                return position;
            }
            finally
            {
                Handles.matrix = previousMatrix;
                Handles.color = previousColor;
            }
        }

        public static Quaternion RotationHandle(Vector3 position, Quaternion rotation)
        {
            return RotationHandle(position, rotation, RotationHandleSettings.Default);
        }

        public static Quaternion RotationHandle(Vector3 position, Quaternion rotation, RotationHandleSettings settings)
        {
            settings = Sanitize(settings);
            rotation = NormalizeQuaternion(rotation);

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;

            try
            {
                Handles.matrix = Matrix4x4.identity;

                if (settings.enableFreeRotation)
                {
                    rotation = DrawFreeRotation(position, rotation, settings.freeRotation, settings,
                        RotationFreeHint);
                }

                rotation = DrawRotationAxis(position, rotation, rotation * Vector3.right, rotation * Vector3.up,
                    settings.xAxis, settings, RotationXHint);
                rotation = DrawRotationAxis(position, rotation, rotation * Vector3.up, rotation * Vector3.forward,
                    settings.yAxis, settings, RotationYHint);
                rotation = DrawRotationAxis(position, rotation, rotation * Vector3.forward, rotation * Vector3.right,
                    settings.zAxis, settings, RotationZHint);

                return rotation;
            }
            finally
            {
                Handles.matrix = previousMatrix;
                Handles.color = previousColor;
            }
        }

        private static Vector3 DrawPositionAxis(Vector3 position, Vector3 axis, PositionAxisSettings axisSettings,
            PositionHandleSettings settings, int controlHint)
        {
            Event currentEvent = Event.current;
            int controlId = GUIUtility.GetControlID(controlHint, FocusType.Passive);

            axis = NormalizeVector(axis, Vector3.right);

            float handleScale = Mathf.Max(MinHandleScale, HandleUtility.GetHandleSize(position) * settings.size);
            float axisLength = Mathf.Max(MinHandleScale, handleScale * axisSettings.length);
            float tipSize = Mathf.Max(MinHandleScale, handleScale * axisSettings.tipSize);
            float thickness = Mathf.Max(1f, axisSettings.thickness);
            Vector3 tipPosition = position + axis * axisLength;

            float distance = Mathf.Min(HandleUtility.DistanceToLine(position, tipPosition),
                HandleUtility.DistanceToCircle(tipPosition, tipSize * 0.4f));
            distance = Mathf.Max(0f, distance - axisSettings.pickPadding);

            bool interactionLocked = IsInteractionLocked(controlId);
            bool hovered = !interactionLocked && HandleUtility.nearestControl == controlId;
            bool active = GUIUtility.hotControl == controlId;

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(controlId, interactionLocked ? float.MaxValue : distance);
                    break;

                case EventType.MouseMove:
                    if (hovered || active)
                    {
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseDown:
                    if (!interactionLocked && currentEvent.button == 0 && !currentEvent.alt && hovered)
                    {
                        GUIUtility.hotControl = controlId;
                        Ray mouseRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);

                        s_PositionDragState = new PositionDragState
                        {
                            controlId = controlId,
                            startPosition = position,
                            axis = axis,
                            startParameter = GetAxisParameter(mouseRay, currentEvent.mousePosition, position, axis,
                                handleScale),
                            fallbackDistance = handleScale
                        };

                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && s_PositionDragState.controlId == controlId)
                    {
                        Ray mouseRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
                        float currentParameter = GetAxisParameter(mouseRay, currentEvent.mousePosition,
                            s_PositionDragState.startPosition, s_PositionDragState.axis,
                            s_PositionDragState.fallbackDistance);

                        Vector3 newPosition = s_PositionDragState.startPosition +
                                              s_PositionDragState.axis *
                                              (currentParameter - s_PositionDragState.startParameter);

                        if ((newPosition - position).sqrMagnitude > MinVectorSqrMagnitude)
                        {
                            position = newPosition;
                            GUI.changed = true;
                            SceneView.RepaintAll();
                        }

                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && currentEvent.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        currentEvent.Use();
                    }
                    break;

                case EventType.Ignore:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                    }
                    break;

                case EventType.Repaint:
                    DrawArrow(position, axis, axisLength, tipSize, thickness, axisSettings.color, hovered, active,
                        settings.hoverBrightness, settings.hoverThicknessMultiplier, settings.pickHighlightColor);
                    break;
            }

            return position;
        }

        private static Vector3 DrawPositionPlanes(Vector3 position, Vector3 xAxis, Vector3 yAxis, Vector3 zAxis,
            PositionHandleSettings settings)
        {
            position = DrawPositionPlane(position, xAxis, yAxis, settings.xyPlane, settings, PositionXYHint);
            position = DrawPositionPlane(position, xAxis, zAxis, settings.xzPlane, settings, PositionXZHint);
            position = DrawPositionPlane(position, yAxis, zAxis, settings.yzPlane, settings, PositionYZHint);
            return position;
        }

        private static Vector3 DrawPositionPlane(Vector3 position, Vector3 axisA, Vector3 axisB,
            PositionPlaneSettings planeSettings, PositionHandleSettings settings, int controlHint)
        {
            Event currentEvent = Event.current;
            int controlId = GUIUtility.GetControlID(controlHint, FocusType.Passive);

            axisA = NormalizeVector(axisA, Vector3.right);
            axisB = Vector3.ProjectOnPlane(axisB, axisA);
            axisB = NormalizeVector(axisB, GetPerpendicularVector(axisA));
            Vector3 planeNormal = NormalizeVector(Vector3.Cross(axisA, axisB), GetPerpendicularVector(axisA));

            float handleScale = Mathf.Max(MinHandleScale, HandleUtility.GetHandleSize(position) * settings.size);
            float planeSize = Mathf.Max(MinHandleScale, handleScale * planeSettings.size);
            Vector3[] corners = BuildPositionPlaneCorners(position, axisA, axisB, planeSize);
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            float distance = GetDistanceToPositionPlane(corners, mouseRay, currentEvent.mousePosition, position, axisA,
                axisB, planeNormal, planeSize, planeSettings.pickPadding);

            bool interactionLocked = IsInteractionLocked(controlId);
            bool hovered = !interactionLocked && HandleUtility.nearestControl == controlId;
            bool active = GUIUtility.hotControl == controlId;

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(controlId, interactionLocked ? float.MaxValue : distance);
                    break;

                case EventType.MouseMove:
                    if (hovered || active)
                    {
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseDown:
                    if (!interactionLocked && currentEvent.button == 0 && !currentEvent.alt && hovered)
                    {
                        GUIUtility.hotControl = controlId;
                        s_PositionPlaneDragState = new PositionPlaneDragState
                        {
                            controlId = controlId,
                            startPosition = position,
                            axisA = axisA,
                            axisB = axisB,
                            planeNormal = planeNormal,
                            startParameters = GetPlaneParameters(mouseRay, currentEvent.mousePosition, position,
                                axisA, axisB, planeNormal, handleScale),
                            fallbackDistance = handleScale
                        };

                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && s_PositionPlaneDragState.controlId == controlId)
                    {
                        Vector2 currentParameters = GetPlaneParameters(mouseRay, currentEvent.mousePosition,
                            s_PositionPlaneDragState.startPosition, s_PositionPlaneDragState.axisA,
                            s_PositionPlaneDragState.axisB, s_PositionPlaneDragState.planeNormal,
                            s_PositionPlaneDragState.fallbackDistance);
                        Vector2 parameterDelta = currentParameters - s_PositionPlaneDragState.startParameters;
                        Vector3 newPosition = s_PositionPlaneDragState.startPosition +
                                              s_PositionPlaneDragState.axisA * parameterDelta.x +
                                              s_PositionPlaneDragState.axisB * parameterDelta.y;

                        if ((newPosition - position).sqrMagnitude > MinVectorSqrMagnitude)
                        {
                            position = newPosition;
                            GUI.changed = true;
                            SceneView.RepaintAll();
                        }

                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && currentEvent.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        s_PositionPlaneDragState = default;
                        currentEvent.Use();
                    }
                    break;

                case EventType.Ignore:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        s_PositionPlaneDragState = default;
                    }
                    break;

                case EventType.Repaint:
                    DrawPositionPlaneSquare(corners, planeSettings, hovered, active, settings.hoverBrightness,
                        settings.hoverThicknessMultiplier, settings.pickHighlightColor);
                    break;
            }

            return position;
        }

        private static Quaternion DrawRotationAxis(Vector3 position, Quaternion rotation, Vector3 axis, Vector3 tangent,
            RotationAxisSettings axisSettings, RotationHandleSettings settings, int controlHint)
        {
            Camera sceneCamera = Camera.current ?? SceneView.lastActiveSceneView?.camera;
            if (sceneCamera == null)
            {
                return rotation;
            }

            Event currentEvent = Event.current;
            int controlId = GUIUtility.GetControlID(controlHint, FocusType.Passive);

            axis = NormalizeVector(axis, Vector3.right);
            tangent = Vector3.ProjectOnPlane(tangent, axis);
            tangent = NormalizeVector(tangent, GetPerpendicularVector(axis));

            float handleScale = Mathf.Max(MinHandleScale, HandleUtility.GetHandleSize(position) * settings.size);
            float radius = Mathf.Max(MinHandleScale, handleScale * axisSettings.radius);
            float thickness = Mathf.Max(1f, axisSettings.thickness);
            int segmentCount = Mathf.Max(24, settings.segmentCount);
            ArcData arcData = BuildAxisArcData(position, sceneCamera, axis, tangent, radius, segmentCount);
            float distance = HandleUtility.DistanceToPolyLine(arcData.pickPoints);
            distance = Mathf.Max(0f, distance - axisSettings.pickPadding);

            bool interactionLocked = IsInteractionLocked(controlId);
            bool hovered = !interactionLocked && HandleUtility.nearestControl == controlId;
            bool active = GUIUtility.hotControl == controlId;

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(controlId, interactionLocked ? float.MaxValue : distance);
                    break;

                case EventType.MouseMove:
                    if (hovered || active)
                    {
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseDown:
                    if (!interactionLocked && currentEvent.button == 0 && !currentEvent.alt && hovered)
                    {
                        GUIUtility.hotControl = controlId;
                        Ray mouseRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
                        Vector3 dragVector = GetRotationVector(mouseRay, position, axis, tangent);

                        s_RotationDragState = new RotationDragState
                        {
                            controlId = controlId,
                            center = position,
                            axis = axis,
                            currentRotation = rotation,
                            lastVector = dragVector,
                            lastMousePosition = currentEvent.mousePosition,
                            worldRadius = radius
                        };

                        SceneView.RepaintAll();
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && s_RotationDragState.controlId == controlId)
                    {
                        Vector2 mouseDelta = currentEvent.mousePosition - s_RotationDragState.lastMousePosition;
                        float deltaAngle = GetAxisRotationDeltaFromMouseDelta(mouseDelta, s_RotationDragState.center,
                            s_RotationDragState.axis, s_RotationDragState.lastVector, s_RotationDragState.worldRadius);

                        if (!Mathf.Approximately(deltaAngle, 0f))
                        {
                            Quaternion deltaRotation = Quaternion.AngleAxis(deltaAngle, s_RotationDragState.axis);
                            s_RotationDragState.currentRotation =
                                NormalizeQuaternion(deltaRotation * s_RotationDragState.currentRotation);
                            s_RotationDragState.lastVector =
                                NormalizeVector(deltaRotation * s_RotationDragState.lastVector,
                                    GetPerpendicularVector(s_RotationDragState.axis));
                            rotation = s_RotationDragState.currentRotation;
                            GUI.changed = true;
                            SceneView.RepaintAll();
                        }

                        s_RotationDragState.lastMousePosition = currentEvent.mousePosition;
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && currentEvent.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        s_RotationDragState = default;
                        SceneView.RepaintAll();
                        currentEvent.Use();
                    }
                    break;

                case EventType.Ignore:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        s_RotationDragState = default;
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.Repaint:
                    DrawAxisRing(arcData, thickness, axisSettings.color, axisSettings.backAlpha, hovered, active,
                        settings.hoverBrightness, settings.hoverThicknessMultiplier, settings.pickHighlightColor);
                    break;
            }

            if (GUIUtility.hotControl == controlId && s_RotationDragState.controlId == controlId)
            {
                rotation = s_RotationDragState.currentRotation;
            }

            return rotation;
        }

        private readonly struct ArcData
        {
            public readonly bool hasSplit;
            public readonly Vector3[] frontPoints;
            public readonly Vector3[] backPoints;
            public readonly Vector3[] pickPoints;

            public ArcData(bool hasSplit, Vector3[] frontPoints, Vector3[] backPoints, Vector3[] pickPoints)
            {
                this.hasSplit = hasSplit;
                this.frontPoints = frontPoints;
                this.backPoints = backPoints;
                this.pickPoints = pickPoints;
            }
        }

        private static Quaternion DrawFreeRotation(Vector3 position, Quaternion rotation,
            FreeRotationSettings freeSettings, RotationHandleSettings settings, int controlHint)
        {
            Camera sceneCamera = Camera.current ?? SceneView.lastActiveSceneView?.camera;
            if (sceneCamera == null)
            {
                return rotation;
            }

            Event currentEvent = Event.current;
            int controlId = GUIUtility.GetControlID(controlHint, FocusType.Passive);

            float handleScale = Mathf.Max(MinHandleScale, HandleUtility.GetHandleSize(position) * settings.size);
            float worldRadius = GetFreeRotationWorldRadius(handleScale, settings);
            GetCameraFacingBasis(position, sceneCamera, out Vector3 trackballNormal, out Vector3 trackballUp,
                out Vector3 trackballRight);
            Vector2 screenCenter = HandleUtility.WorldToGUIPoint(position);
            Vector2 screenEdgeUp = HandleUtility.WorldToGUIPoint(position + trackballUp * worldRadius);
            Vector2 screenEdgeRight = HandleUtility.WorldToGUIPoint(position + trackballRight * worldRadius);
            float screenRadius = Mathf.Max(1f, Mathf.Max((screenEdgeUp - screenCenter).magnitude,
                (screenEdgeRight - screenCenter).magnitude));
            float distance = Mathf.Max(0f,
                Vector2.Distance(currentEvent.mousePosition, screenCenter) - (screenRadius + freeSettings.pickPadding));

            bool interactionLocked = IsInteractionLocked(controlId);
            bool hovered = !interactionLocked && HandleUtility.nearestControl == controlId;
            bool active = GUIUtility.hotControl == controlId;

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(controlId, interactionLocked ? float.MaxValue : distance);
                    break;

                case EventType.MouseMove:
                    if (hovered || active)
                    {
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseDown:
                    if (!interactionLocked && currentEvent.button == 0 && !currentEvent.alt && hovered)
                    {
                        GUIUtility.hotControl = controlId;
                        s_FreeRotationDragState = new FreeRotationDragState
                        {
                            controlId = controlId,
                            currentRotation = rotation,
                            lastTrackballVector = ProjectOnTrackball(currentEvent.mousePosition, screenCenter,
                                screenRadius),
                            lastMousePosition = currentEvent.mousePosition,
                            screenRadius = screenRadius
                        };

                        SceneView.RepaintAll();
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && s_FreeRotationDragState.controlId == controlId)
                    {
                        Vector2 mouseDelta = currentEvent.mousePosition - s_FreeRotationDragState.lastMousePosition;
                        Vector3 currentTrackballVector = GetTrackballVectorFromMouseDelta(
                            s_FreeRotationDragState.lastTrackballVector, mouseDelta,
                            s_FreeRotationDragState.screenRadius);
                        Vector3 cameraSpaceAxis = Vector3.Cross(s_FreeRotationDragState.lastTrackballVector,
                            currentTrackballVector);

                        if (cameraSpaceAxis.sqrMagnitude > MinVectorSqrMagnitude)
                        {
                            float angle = Mathf.Atan2(cameraSpaceAxis.magnitude,
                                Mathf.Clamp(Vector3.Dot(s_FreeRotationDragState.lastTrackballVector,
                                    currentTrackballVector), -1f, 1f)) * Mathf.Rad2Deg;

                            Vector3 worldAxis = trackballRight * cameraSpaceAxis.x +
                                                trackballUp * cameraSpaceAxis.y -
                                                trackballNormal * cameraSpaceAxis.z;
                            worldAxis = NormalizeVector(worldAxis, -trackballNormal);
                            s_FreeRotationDragState.currentRotation =
                                NormalizeQuaternion(Quaternion.AngleAxis(-angle, worldAxis) *
                                                    s_FreeRotationDragState.currentRotation);
                            rotation = s_FreeRotationDragState.currentRotation;
                            GUI.changed = true;
                            SceneView.RepaintAll();
                        }

                        s_FreeRotationDragState.lastTrackballVector = currentTrackballVector;
                        s_FreeRotationDragState.lastMousePosition = currentEvent.mousePosition;
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && currentEvent.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        s_FreeRotationDragState = default;
                        SceneView.RepaintAll();
                        currentEvent.Use();
                    }
                    break;

                case EventType.Ignore:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        s_FreeRotationDragState = default;
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.Repaint:
                    DrawFreeRotationDisc(position, sceneCamera, worldRadius, freeSettings, hovered, active,
                        settings.hoverBrightness, settings.hoverThicknessMultiplier, settings.segmentCount);
                    break;
            }

            if (GUIUtility.hotControl == controlId && s_FreeRotationDragState.controlId == controlId)
            {
                AddSceneCursorRect(sceneCamera, MouseCursor.Pan);
                rotation = s_FreeRotationDragState.currentRotation;
            }

            return rotation;
        }

        private static PositionHandleSettings Sanitize(PositionHandleSettings settings)
        {
            if (settings.size <= 0f)
            {
                return PositionHandleSettings.Default;
            }

            settings.size = Mathf.Max(MinHandleScale, settings.size);
            settings.hoverBrightness = Mathf.Clamp01(settings.hoverBrightness);
            settings.hoverThicknessMultiplier = Mathf.Max(1f, settings.hoverThicknessMultiplier);
            settings.pickHighlightColor =
                SanitizeColor(settings.pickHighlightColor, PositionHandleSettings.Default.pickHighlightColor);

            settings.xAxis = Sanitize(settings.xAxis, PositionAxisSettings.DefaultX);
            settings.yAxis = Sanitize(settings.yAxis, PositionAxisSettings.DefaultY);
            settings.zAxis = Sanitize(settings.zAxis, PositionAxisSettings.DefaultZ);
            settings.xyPlane = Sanitize(settings.xyPlane, PositionPlaneSettings.DefaultXY);
            settings.xzPlane = Sanitize(settings.xzPlane, PositionPlaneSettings.DefaultXZ);
            settings.yzPlane = Sanitize(settings.yzPlane, PositionPlaneSettings.DefaultYZ);

            return settings;
        }

        private static RotationHandleSettings Sanitize(RotationHandleSettings settings)
        {
            if (settings.size <= 0f || settings.segmentCount <= 0)
            {
                return RotationHandleSettings.Default;
            }

            settings.size = Mathf.Max(MinHandleScale, settings.size);
            settings.hoverBrightness = Mathf.Clamp01(settings.hoverBrightness);
            settings.hoverThicknessMultiplier = Mathf.Max(1f, settings.hoverThicknessMultiplier);
            settings.pickHighlightColor =
                SanitizeColor(settings.pickHighlightColor, RotationHandleSettings.Default.pickHighlightColor);
            settings.segmentCount = Mathf.Max(24, settings.segmentCount);
            settings.freeRotation = Sanitize(settings.freeRotation);

            settings.xAxis = Sanitize(settings.xAxis, RotationAxisSettings.DefaultX);
            settings.yAxis = Sanitize(settings.yAxis, RotationAxisSettings.DefaultY);
            settings.zAxis = Sanitize(settings.zAxis, RotationAxisSettings.DefaultZ);

            return settings;
        }

        private static PositionAxisSettings Sanitize(PositionAxisSettings settings, PositionAxisSettings defaults)
        {
            settings.color = SanitizeColor(settings.color, defaults.color);
            settings.length = Mathf.Max(MinHandleScale, settings.length);
            settings.thickness = Mathf.Max(1f, settings.thickness);
            settings.tipSize = Mathf.Max(MinHandleScale, settings.tipSize);
            settings.pickPadding = Mathf.Max(0f, settings.pickPadding);
            return settings;
        }

        private static PositionPlaneSettings Sanitize(PositionPlaneSettings settings, PositionPlaneSettings defaults)
        {
            if (settings.size <= 0f && settings.fillAlpha <= 0f && settings.outlineThickness <= 0f &&
                settings.pickPadding <= 0f && IsUnsetColor(settings.color))
            {
                return defaults;
            }

            settings.color = SanitizeColor(settings.color, defaults.color);
            settings.size = settings.size <= 0f ? defaults.size : Mathf.Max(MinHandleScale, settings.size);
            settings.fillAlpha = Mathf.Clamp01(settings.fillAlpha);
            settings.outlineThickness = settings.outlineThickness <= 0f
                ? defaults.outlineThickness
                : Mathf.Max(1f, settings.outlineThickness);
            settings.pickPadding = Mathf.Max(0f, settings.pickPadding);
            return settings;
        }

        private static FreeRotationSettings Sanitize(FreeRotationSettings settings)
        {
            settings.color = SanitizeColor(settings.color, FreeRotationSettings.Default.color);
            settings.pickColor = SanitizeColor(settings.pickColor, FreeRotationSettings.Default.pickColor);
            settings.outlineThickness = Mathf.Max(1f, settings.outlineThickness);
            settings.fillAlpha = Mathf.Clamp01(settings.fillAlpha);
            settings.pickPadding = Mathf.Max(0f, settings.pickPadding);
            return settings;
        }

        private static RotationAxisSettings Sanitize(RotationAxisSettings settings, RotationAxisSettings defaults)
        {
            settings.color = SanitizeColor(settings.color, defaults.color);
            settings.radius = Mathf.Max(MinHandleScale, settings.radius);
            settings.thickness = Mathf.Max(1f, settings.thickness);
            settings.backAlpha = Mathf.Clamp01(settings.backAlpha);
            settings.pickPadding = Mathf.Max(0f, settings.pickPadding);
            return settings;
        }

        private static Color SanitizeColor(Color color, Color fallback)
        {
            return color.a <= 0f && color.r <= 0f && color.g <= 0f && color.b <= 0f ? fallback : color;
        }

        private static bool IsUnsetColor(Color color)
        {
            return color.a <= 0f && color.r <= 0f && color.g <= 0f && color.b <= 0f;
        }

        private static void DrawArrow(Vector3 position, Vector3 axis, float axisLength, float tipSize, float thickness,
            Color color, bool hovered, bool active, float hoverBrightness, float hoverThicknessMultiplier,
            Color pickHighlightColor)
        {
            float drawTipSize = hovered || active ? tipSize * 1.08f : tipSize;
            float tipLength = Mathf.Min(axisLength * 0.35f, drawTipSize * 0.8f);
            Vector3 tipPosition = position + axis * axisLength;
            Vector3 tipBasePosition = tipPosition - axis * tipLength;

            Handles.color = GetHandleColor(color, hovered, active, hoverBrightness, pickHighlightColor);
            float drawThickness = hovered || active ? thickness * hoverThicknessMultiplier : thickness;

            Handles.DrawAAPolyLine(drawThickness, position, tipBasePosition);
            Handles.ConeHandleCap(0, tipBasePosition, Quaternion.LookRotation(axis), drawTipSize, EventType.Repaint);
        }

        private static void DrawPositionPlaneSquare(Vector3[] corners, PositionPlaneSettings planeSettings,
            bool hovered, bool active, float hoverBrightness, float hoverThicknessMultiplier,
            Color pickHighlightColor)
        {
            Color handleColor = GetHandleColor(planeSettings.color, hovered, active, hoverBrightness,
                pickHighlightColor);
            Color fillColor = handleColor;
            fillColor.a = Mathf.Clamp01(planeSettings.fillAlpha);

            Handles.color = Color.white;
            Handles.DrawSolidRectangleWithOutline(corners, fillColor, Color.clear);

            Handles.color = planeSettings.color;
            Handles.DrawAAPolyLine(planeSettings.outlineThickness, corners[1], corners[2], corners[3]);
            if (hovered || active)
            {
                float drawThickness = planeSettings.outlineThickness * hoverThicknessMultiplier;
                Handles.color = handleColor;
                Handles.DrawAAPolyLine(drawThickness, corners[1], corners[2], corners[3]);
            }
        }

        private static void DrawRing(Vector3[] ringPoints, float thickness, Color color, bool hovered, bool active,
            float hoverBrightness, float hoverThicknessMultiplier, Color pickHighlightColor)
        {
            Handles.color = GetHandleColor(color, hovered, active, hoverBrightness, pickHighlightColor);
            float drawThickness = hovered || active ? thickness * hoverThicknessMultiplier : thickness;
            Handles.DrawAAPolyLine(drawThickness, ringPoints);
        }

        private static void DrawAxisRing(ArcData arcData, float thickness, Color color, float backAlpha, bool hovered,
            bool active, float hoverBrightness, float hoverThicknessMultiplier, Color pickHighlightColor)
        {
            if (!arcData.hasSplit)
            {
                DrawRing(arcData.frontPoints, thickness, color, hovered, active, hoverBrightness,
                    hoverThicknessMultiplier, pickHighlightColor);
                return;
            }

            float drawThickness = hovered || active ? thickness * hoverThicknessMultiplier : thickness;
            Color frontColor = GetHandleColor(color, hovered, active, hoverBrightness, pickHighlightColor);

            if (active)
            {
                Handles.color = frontColor;
                Handles.DrawAAPolyLine(drawThickness, arcData.backPoints);
                Handles.DrawAAPolyLine(drawThickness, arcData.frontPoints);
                return;
            }

            Color backColor = GetFadedColor(frontColor, backAlpha);

            if (backColor.a > 0f)
            {
                Handles.color = backColor;
                Handles.DrawAAPolyLine(drawThickness, arcData.backPoints);
            }

            Handles.color = frontColor;
            Handles.DrawAAPolyLine(drawThickness, arcData.frontPoints);
        }

        private static void DrawFreeRotationDisc(Vector3 position, Camera sceneCamera, float worldRadius,
            FreeRotationSettings freeSettings, bool hovered, bool active, float hoverBrightness,
            float hoverThicknessMultiplier, int segmentCount)
        {
            GetCameraFacingBasis(position, sceneCamera, out Vector3 discNormal, out _, out _);
            Color outlineColor = GetHandleColor(freeSettings.color, hovered, active, hoverBrightness,
                freeSettings.pickColor);
            Color fillColor = outlineColor;
            fillColor.a = Mathf.Clamp01(freeSettings.fillAlpha * (hovered || active ? 1.8f : 1f));

            Handles.color = fillColor;
            Handles.DrawSolidDisc(position, discNormal, worldRadius);

            Vector3[] ringPoints = BuildBillboardRingPoints(position, sceneCamera, worldRadius,
                Mathf.Max(24, segmentCount));
            Handles.color = outlineColor;
            float outlineThickness = hovered || active
                ? freeSettings.outlineThickness * hoverThicknessMultiplier
                : freeSettings.outlineThickness;
            Handles.DrawAAPolyLine(outlineThickness, ringPoints);
        }

        private static float GetFreeRotationWorldRadius(float handleScale, RotationHandleSettings settings)
        {
            float maxRadius = Mathf.Max(settings.xAxis.radius, Mathf.Max(settings.yAxis.radius, settings.zAxis.radius));
            return Mathf.Max(MinHandleScale, handleScale * maxRadius);
        }

        private static Vector3[] BuildBillboardRingPoints(Vector3 position, Camera sceneCamera, float radius,
            int segmentCount)
        {
            GetCameraFacingBasis(position, sceneCamera, out Vector3 ringNormal, out Vector3 ringUp, out _);
            return BuildRingPoints(position, ringNormal, ringUp, radius, segmentCount);
        }

        private static void GetCameraFacingBasis(Vector3 position, Camera sceneCamera, out Vector3 normal,
            out Vector3 up, out Vector3 right)
        {
            normal = GetHandleViewDirectionToCamera(sceneCamera, position);
            right = Vector3.ProjectOnPlane(sceneCamera.transform.right, normal);

            if (right.sqrMagnitude <= MinVectorSqrMagnitude)
            {
                right = Vector3.ProjectOnPlane(sceneCamera.transform.up, normal);
            }

            right = NormalizeVector(right, GetPerpendicularVector(normal));
            up = NormalizeVector(Vector3.Cross(right, normal), GetPerpendicularVector(normal));
        }

        private static ArcData BuildAxisArcData(Vector3 position, Camera sceneCamera, Vector3 axis, Vector3 tangent,
            float radius, int segmentCount)
        {
            Vector3 viewDirection = GetHandleViewDirectionToCamera(sceneCamera, position);
            Vector3 splitDirection = Vector3.Cross(viewDirection, axis);
            if (splitDirection.sqrMagnitude <= MinVectorSqrMagnitude)
            {
                Vector3[] fullRingPoints = BuildRingPoints(position, axis, tangent, radius, segmentCount);
                return new ArcData(false, fullRingPoints, null, fullRingPoints);
            }

            splitDirection.Normalize();
            float splitAngle = Vector3.SignedAngle(tangent, splitDirection, axis);
            float frontArcSign = GetFrontArcSign(viewDirection, axis, splitDirection);
            float frontStartAngle = splitAngle;
            float frontEndAngle = splitAngle + frontArcSign * 180f;
            float backStartAngle = frontEndAngle;
            float backEndAngle = splitAngle + frontArcSign * 360f;

            Vector3[] frontPoints = BuildArcPoints(position, axis, tangent, radius, frontStartAngle, frontEndAngle,
                segmentCount);
            Vector3[] backPoints = BuildArcPoints(position, axis, tangent, radius, backStartAngle, backEndAngle,
                segmentCount);

            return new ArcData(true, frontPoints, backPoints, frontPoints);
        }

        private static float GetFrontArcSign(Vector3 viewDirection, Vector3 axis, Vector3 splitDirection)
        {
            const float sampleDegrees = 5f;

            Vector3 sampleDirection = Quaternion.AngleAxis(sampleDegrees, axis) * splitDirection;
            bool sampleIsFront = Vector3.Dot(sampleDirection, viewDirection) > 0f;
            return sampleIsFront ? 1f : -1f;
        }

        private static Vector3 GetHandleViewDirectionToCamera(Camera sceneCamera, Vector3 position)
        {
            if (sceneCamera.orthographic)
            {
                return -sceneCamera.transform.forward;
            }

            Vector3 viewDirection = sceneCamera.transform.position - position;
            if (viewDirection.sqrMagnitude <= MinVectorSqrMagnitude)
            {
                return -sceneCamera.transform.forward;
            }

            return viewDirection.normalized;
        }

        private static Vector3[] BuildArcPoints(Vector3 position, Vector3 axis, Vector3 tangent, float radius,
            float startAngle, float endAngle, int segmentCount)
        {
            int arcSegments = Mathf.Max(8, Mathf.RoundToInt(segmentCount * Mathf.Abs(endAngle - startAngle) / 360f));
            Vector3[] points = new Vector3[arcSegments + 1];
            float angleStep = (endAngle - startAngle) / arcSegments;
            Vector3 startVector = tangent.normalized * radius;

            for (int i = 0; i <= arcSegments; i++)
            {
                points[i] = position + Quaternion.AngleAxis(startAngle + angleStep * i, axis) * startVector;
            }

            return points;
        }

        private static void AddSceneCursorRect(Camera sceneCamera, MouseCursor cursor)
        {
            if (sceneCamera == null || sceneCamera.pixelWidth <= 0f || sceneCamera.pixelHeight <= 0f)
            {
                return;
            }

            EditorGUIUtility.AddCursorRect(new Rect(0f, 0f, sceneCamera.pixelWidth, sceneCamera.pixelHeight), cursor);
        }

        private static bool IsInteractionLocked(int controlId)
        {
            int hotControl = GUIUtility.hotControl;
            return hotControl != 0 && hotControl != controlId;
        }

        private static Vector3[] BuildPositionPlaneCorners(Vector3 position, Vector3 axisA, Vector3 axisB,
            float planeSize)
        {
            Vector3 axisAOffset = axisA * planeSize;
            Vector3 axisBOffset = axisB * planeSize;
            return new[]
            {
                position,
                position + axisAOffset,
                position + axisAOffset + axisBOffset,
                position + axisBOffset
            };
        }

        private static float GetDistanceToPositionPlane(Vector3[] corners, Ray mouseRay, Vector2 mousePosition,
            Vector3 origin, Vector3 axisA, Vector3 axisB, Vector3 planeNormal, float planeSize, float pickPadding)
        {
            if (TryGetBoundedPlaneHit(mouseRay, origin, axisA, axisB, planeNormal, planeSize,
                    out float rayDistance))
            {
                return GetPickDepthBias(rayDistance);
            }

            float screenDistance = GetDistanceToScreenQuad(corners, mousePosition, pickPadding);
            return screenDistance <= 0f ? 0.01f : screenDistance;
        }

        private static bool TryGetBoundedPlaneHit(Ray mouseRay, Vector3 origin, Vector3 axisA, Vector3 axisB,
            Vector3 planeNormal, float planeSize, out float rayDistance)
        {
            rayDistance = 0f;
            planeNormal = NormalizeVector(planeNormal, NormalizeVector(Vector3.Cross(axisA, axisB),
                GetPerpendicularVector(axisA)));

            if (Mathf.Abs(Vector3.Dot(mouseRay.direction.normalized, planeNormal)) < 0.0001f)
            {
                return false;
            }

            Plane plane = new Plane(planeNormal, origin);
            if (!plane.Raycast(mouseRay, out rayDistance) || rayDistance < 0f)
            {
                return false;
            }

            Vector3 offset = mouseRay.GetPoint(rayDistance) - origin;
            float axisAParameter = Vector3.Dot(offset, axisA);
            float axisBParameter = Vector3.Dot(offset, axisB);

            return axisAParameter >= 0f && axisAParameter <= planeSize &&
                   axisBParameter >= 0f && axisBParameter <= planeSize &&
                   !float.IsNaN(axisAParameter) && !float.IsInfinity(axisAParameter) &&
                   !float.IsNaN(axisBParameter) && !float.IsInfinity(axisBParameter);
        }

        private static float GetPickDepthBias(float rayDistance)
        {
            return Mathf.Min(0.009f, Mathf.Max(0f, rayDistance) * 0.000001f);
        }

        private static float GetDistanceToScreenQuad(Vector3[] corners, Vector2 mousePosition, float pickPadding)
        {
            if (corners == null || corners.Length < 4)
            {
                return float.MaxValue;
            }

            Vector2[] guiPoints =
            {
                HandleUtility.WorldToGUIPoint(corners[0]),
                HandleUtility.WorldToGUIPoint(corners[1]),
                HandleUtility.WorldToGUIPoint(corners[2]),
                HandleUtility.WorldToGUIPoint(corners[3])
            };

            for (int i = 0; i < guiPoints.Length; i++)
            {
                if (!IsFinite(guiPoints[i]))
                {
                    return float.MaxValue;
                }
            }

            if (Mathf.Abs(GetSignedPolygonArea(guiPoints)) > MinVectorSqrMagnitude &&
                IsPointInsideConvexPolygon(mousePosition, guiPoints))
            {
                return 0f;
            }

            float distance = float.MaxValue;
            for (int i = 0; i < guiPoints.Length; i++)
            {
                Vector2 from = guiPoints[i];
                Vector2 to = guiPoints[(i + 1) % guiPoints.Length];
                distance = Mathf.Min(distance, DistanceToSegment(mousePosition, from, to));
            }

            return Mathf.Max(0f, distance - pickPadding);
        }

        private static float GetSignedPolygonArea(Vector2[] polygon)
        {
            float area = 0f;

            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 from = polygon[i];
                Vector2 to = polygon[(i + 1) % polygon.Length];
                area += from.x * to.y - to.x * from.y;
            }

            return area * 0.5f;
        }

        private static bool IsPointInsideConvexPolygon(Vector2 point, Vector2[] polygon)
        {
            bool hasPositive = false;
            bool hasNegative = false;

            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 from = polygon[i];
                Vector2 to = polygon[(i + 1) % polygon.Length];
                float cross = Cross(to - from, point - from);

                if (cross > MinVectorSqrMagnitude)
                {
                    hasPositive = true;
                }
                else if (cross < -MinVectorSqrMagnitude)
                {
                    hasNegative = true;
                }

                if (hasPositive && hasNegative)
                {
                    return false;
                }
            }

            return true;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            Vector2 segment = to - from;
            float segmentLength = segment.sqrMagnitude;

            if (segmentLength <= MinVectorSqrMagnitude)
            {
                return Vector2.Distance(point, from);
            }

            float parameter = Mathf.Clamp01(Vector2.Dot(point - from, segment) / segmentLength);
            return Vector2.Distance(point, from + segment * parameter);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
        }

        private static float GetAxisParameter(Ray mouseRay, Vector2 mousePosition, Vector3 origin, Vector3 axis,
            float fallbackDistance)
        {
            if (TryGetAxisPlaneParameter(mouseRay, origin, axis, out float planeParameter))
            {
                return planeParameter;
            }

            float alignment = Mathf.Abs(Vector3.Dot(mouseRay.direction.normalized, axis));
            if (alignment < 0.995f && TryGetClosestAxisParameter(mouseRay, origin, axis, out float parameter))
            {
                return parameter;
            }

            return GetScreenProjectedAxisParameter(mousePosition, origin, axis, fallbackDistance);
        }

        private static Vector2 GetPlaneParameters(Ray mouseRay, Vector2 mousePosition, Vector3 origin, Vector3 axisA,
            Vector3 axisB, Vector3 planeNormal, float fallbackDistance)
        {
            if (TryGetPlaneParameters(mouseRay, origin, axisA, axisB, planeNormal, out Vector2 parameters))
            {
                return parameters;
            }

            return GetScreenProjectedPlaneParameters(mousePosition, origin, axisA, axisB, fallbackDistance);
        }

        private static bool TryGetPlaneParameters(Ray mouseRay, Vector3 origin, Vector3 axisA, Vector3 axisB,
            Vector3 planeNormal, out Vector2 parameters)
        {
            planeNormal = NormalizeVector(planeNormal, NormalizeVector(Vector3.Cross(axisA, axisB),
                GetPerpendicularVector(axisA)));
            float rayAlignment = Mathf.Abs(Vector3.Dot(mouseRay.direction.normalized, planeNormal));

            if (rayAlignment < 0.02f)
            {
                parameters = Vector2.zero;
                return false;
            }

            Plane dragPlane = new Plane(planeNormal, origin);
            if (!dragPlane.Raycast(mouseRay, out float enterDistance))
            {
                parameters = Vector2.zero;
                return false;
            }

            Vector3 hitPoint = mouseRay.GetPoint(enterDistance);
            Vector3 offset = hitPoint - origin;
            parameters = new Vector2(Vector3.Dot(offset, axisA), Vector3.Dot(offset, axisB));
            return IsFinite(parameters);
        }

        private static Vector2 GetScreenProjectedPlaneParameters(Vector2 mousePosition, Vector3 origin, Vector3 axisA,
            Vector3 axisB, float fallbackDistance)
        {
            Vector2 screenOrigin = HandleUtility.WorldToGUIPoint(origin);
            Vector2 screenAxisA = HandleUtility.WorldToGUIPoint(origin + axisA * fallbackDistance) - screenOrigin;
            Vector2 screenAxisB = HandleUtility.WorldToGUIPoint(origin + axisB * fallbackDistance) - screenOrigin;
            Vector2 screenDelta = mousePosition - screenOrigin;

            float dotAA = Vector2.Dot(screenAxisA, screenAxisA);
            float dotAB = Vector2.Dot(screenAxisA, screenAxisB);
            float dotBB = Vector2.Dot(screenAxisB, screenAxisB);
            float dotADelta = Vector2.Dot(screenAxisA, screenDelta);
            float dotBDelta = Vector2.Dot(screenAxisB, screenDelta);
            float determinant = dotAA * dotBB - dotAB * dotAB;

            if (Mathf.Abs(determinant) > MinVectorSqrMagnitude)
            {
                float axisAParameter = (dotADelta * dotBB - dotBDelta * dotAB) / determinant;
                float axisBParameter = (dotBDelta * dotAA - dotADelta * dotAB) / determinant;
                return new Vector2(axisAParameter * fallbackDistance, axisBParameter * fallbackDistance);
            }

            return new Vector2(
                GetScreenProjectedAxisParameter(mousePosition, origin, axisA, fallbackDistance),
                GetScreenProjectedAxisParameter(mousePosition, origin, axisB, fallbackDistance));
        }

        private static bool TryGetAxisPlaneParameter(Ray mouseRay, Vector3 origin, Vector3 axis, out float parameter)
        {
            Camera sceneCamera = Camera.current ?? SceneView.lastActiveSceneView?.camera;
            Vector3 viewDirection = sceneCamera != null ? sceneCamera.transform.forward : mouseRay.direction.normalized;
            Vector3 viewUp = sceneCamera != null ? sceneCamera.transform.up : Vector3.up;

            Vector3 guide = Vector3.Cross(viewDirection, axis);
            if (guide.sqrMagnitude <= MinVectorSqrMagnitude)
            {
                guide = Vector3.Cross(viewUp, axis);
            }

            if (guide.sqrMagnitude <= MinVectorSqrMagnitude)
            {
                guide = GetPerpendicularVector(axis);
            }

            Vector3 planeNormal = NormalizeVector(Vector3.Cross(axis, guide), GetPerpendicularVector(axis));
            Plane dragPlane = new Plane(planeNormal, origin);

            if (!dragPlane.Raycast(mouseRay, out float enterDistance))
            {
                parameter = 0f;
                return false;
            }

            Vector3 hitPoint = mouseRay.GetPoint(enterDistance);
            parameter = Vector3.Dot(hitPoint - origin, axis);
            return !float.IsNaN(parameter) && !float.IsInfinity(parameter);
        }

        private static bool TryGetClosestAxisParameter(Ray mouseRay, Vector3 origin, Vector3 axis, out float parameter)
        {
            Vector3 rayDirection = mouseRay.direction.normalized;
            Vector3 offset = origin - mouseRay.origin;

            float axisDotRay = Vector3.Dot(axis, rayDirection);
            float axisDotOffset = Vector3.Dot(axis, offset);
            float rayDotOffset = Vector3.Dot(rayDirection, offset);
            float denominator = 1f - axisDotRay * axisDotRay;

            if (Mathf.Abs(denominator) < 0.0001f)
            {
                parameter = 0f;
                return false;
            }

            parameter = (axisDotRay * rayDotOffset - axisDotOffset) / denominator;
            return !float.IsNaN(parameter) && !float.IsInfinity(parameter);
        }

        private static float GetScreenProjectedAxisParameter(Vector2 mousePosition, Vector3 origin, Vector3 axis,
            float fallbackDistance)
        {
            Vector2 screenOrigin = HandleUtility.WorldToGUIPoint(origin);
            Vector2 screenAxisPoint = HandleUtility.WorldToGUIPoint(origin + axis * fallbackDistance);
            Vector2 screenAxis = screenAxisPoint - screenOrigin;

            if (screenAxis.sqrMagnitude < MinVectorSqrMagnitude)
            {
                return 0f;
            }

            return Vector2.Dot(mousePosition - screenOrigin, screenAxis) / screenAxis.sqrMagnitude * fallbackDistance;
        }

        private static Vector3 GetRotationVector(Ray mouseRay, Vector3 center, Vector3 axis, Vector3 fallbackVector)
        {
            Plane rotationPlane = new Plane(axis, center);
            if (rotationPlane.Raycast(mouseRay, out float enterDistance))
            {
                Vector3 hitPoint = mouseRay.GetPoint(enterDistance);
                Vector3 onPlane = Vector3.ProjectOnPlane(hitPoint - center, axis);
                if (onPlane.sqrMagnitude > MinVectorSqrMagnitude)
                {
                    return onPlane.normalized;
                }
            }

            Vector3 closestPointOnRay = mouseRay.origin + mouseRay.direction *
                Vector3.Dot(center - mouseRay.origin, mouseRay.direction.normalized);
            Vector3 fallbackProjection = Vector3.ProjectOnPlane(closestPointOnRay - center, axis);

            if (fallbackProjection.sqrMagnitude > MinVectorSqrMagnitude)
            {
                return fallbackProjection.normalized;
            }

            return NormalizeVector(Vector3.ProjectOnPlane(fallbackVector, axis), GetPerpendicularVector(axis));
        }

        private static float GetAxisRotationDeltaFromMouseDelta(Vector2 mouseDelta, Vector3 center, Vector3 axis,
            Vector3 currentVector, float worldRadius)
        {
            if (mouseDelta.sqrMagnitude <= MinVectorSqrMagnitude)
            {
                return 0f;
            }

            const float sampleAngle = 2f;

            Vector3 radialVector = NormalizeVector(currentVector, GetPerpendicularVector(axis)) *
                                   Mathf.Max(MinHandleScale, worldRadius);
            Vector2 screenPoint = HandleUtility.WorldToGUIPoint(center + radialVector);
            Vector2 nextScreenPoint =
                HandleUtility.WorldToGUIPoint(center + Quaternion.AngleAxis(sampleAngle, axis) * radialVector);
            Vector2 screenTangent = nextScreenPoint - screenPoint;
            float tangentMagnitude = screenTangent.magnitude;

            if (tangentMagnitude <= MinHandleScale)
            {
                return 0f;
            }

            return Vector2.Dot(mouseDelta, screenTangent / tangentMagnitude) * sampleAngle / tangentMagnitude;
        }

        private static Vector3 GetTrackballVectorFromMouseDelta(Vector3 previousVector, Vector2 mouseDelta,
            float screenRadius)
        {
            if (mouseDelta.sqrMagnitude <= MinVectorSqrMagnitude || screenRadius <= MinHandleScale)
            {
                return previousVector;
            }

            Vector2 normalizedDelta = new Vector2(mouseDelta.x / screenRadius, -mouseDelta.y / screenRadius);
            Vector3 tangentX = NormalizeVector(Vector3.ProjectOnPlane(Vector3.right, previousVector),
                GetPerpendicularVector(previousVector));
            Vector3 tangentY = NormalizeVector(Vector3.ProjectOnPlane(Vector3.up, previousVector),
                NormalizeVector(Vector3.Cross(previousVector, tangentX), GetPerpendicularVector(previousVector)));
            Vector3 currentVector = previousVector + tangentX * normalizedDelta.x + tangentY * normalizedDelta.y;
            return NormalizeVector(currentVector, previousVector);
        }

        private static Vector3 ProjectOnTrackball(Vector2 mousePosition, Vector2 center, float radius)
        {
            Vector2 point = new Vector2((mousePosition.x - center.x) / radius, (center.y - mousePosition.y) / radius);
            float magnitude = point.sqrMagnitude;

            if (magnitude > 1f)
            {
                point.Normalize();
                return new Vector3(point.x, point.y, 0f);
            }

            float z = Mathf.Sqrt(1f - magnitude);
            return new Vector3(point.x, point.y, z);
        }

        private static Vector3[] BuildRingPoints(Vector3 position, Vector3 axis, Vector3 tangent, float radius,
            int segmentCount)
        {
            Vector3[] points = new Vector3[segmentCount + 1];
            tangent = tangent.normalized * radius;
            float step = 360f / segmentCount;

            for (int i = 0; i <= segmentCount; i++)
            {
                points[i] = position + Quaternion.AngleAxis(step * i, axis) * tangent;
            }

            return points;
        }

        private static Color GetHighlightColor(Color color, float brightness)
        {
            Color highlightColor = Color.Lerp(color, Color.white, brightness);
            highlightColor.a = color.a;
            return highlightColor;
        }

        private static Color GetHandleColor(Color color, bool hovered, bool active, float hoverBrightness,
            Color pickHighlightColor)
        {
            if (active)
            {
                Color activeColor = pickHighlightColor;
                activeColor.a = color.a;
                return activeColor;
            }

            return hovered ? GetHighlightColor(color, hoverBrightness) : color;
        }

        private static Color GetFadedColor(Color color, float alphaFactor)
        {
            Color fadedColor = color;
            fadedColor.a *= alphaFactor;
            return fadedColor;
        }

        private static Vector3 GetPerpendicularVector(Vector3 axis)
        {
            Vector3 cross = Vector3.Cross(axis, Vector3.up);
            if (cross.sqrMagnitude > MinVectorSqrMagnitude)
            {
                return cross.normalized;
            }

            return Vector3.Cross(axis, Vector3.right).normalized;
        }

        private static Quaternion NormalizeQuaternion(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z +
                                         rotation.w * rotation.w);

            if (magnitude <= MinHandleScale)
            {
                return Quaternion.identity;
            }

            float inverseMagnitude = 1f / magnitude;
            rotation.x *= inverseMagnitude;
            rotation.y *= inverseMagnitude;
            rotation.z *= inverseMagnitude;
            rotation.w *= inverseMagnitude;
            return rotation;
        }

        private static Vector3 NormalizeVector(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude <= MinVectorSqrMagnitude)
            {
                return fallback.normalized;
            }

            return value.normalized;
        }
    }
}