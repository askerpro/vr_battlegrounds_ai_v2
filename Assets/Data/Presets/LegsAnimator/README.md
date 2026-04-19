# Legs Animator Presets

Use this folder to store reusable presets for avatar leg setup.

Recommended assets in this folder:
- LegsAnimator_VR_Stable.preset
- LegsAnimatorUxrBridge_VR_Stable.preset

How to create presets from a tuned avatar:
1. Select the avatar prefab instance with tuned components.
2. Open component menu on Legs Animator.
3. Click Save as Preset.
4. Save as LegsAnimator_VR_Stable.preset in this folder.
5. Repeat for LegsAnimatorUxrBridge component.

How to apply on another avatar:
1. Add both components to the new avatar.
2. Apply both presets from this folder.
3. Verify rig links (Hips, legs bones, base transform) and fix if needed.
4. Enter Play Mode and validate:
   - no feet sliding on idle
   - no over-rotation on small head movement
   - stable stepping on forward lean and turns

Troubleshooting quick notes:
- If legs react too late: reduce StepMoveDuration, reduce GlueRangeThreshold.
- If idle jitter appears: increase movementThreshold a bit and/or movementSmoothing.
- If body twists too much: reduce Rotation Stability module power or disable it.
