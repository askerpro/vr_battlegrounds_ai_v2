---
title: "Scripting: How do I...?"
---

# Scripting: How do I...?

The goal of this section is to provide through snippets a very quick understanding of which classes are involved in the different aspects of XR development and how exactly UltimateXR can be used to solve specific problems.

Use the menu on the right to navigate to a specific topic.

For a full scripting reference please visit the [API section](/api).

## Animation

How do I animate a material color over time?

```c#
// Start
UxrAnimatedMaterial animation = UxrAnimatedMaterial.AnimateBlinkColor(gameObject, "_BaseColor", startColor, ColorExt.ScaleColorBrightness(startColor, 2.5f));

// Stop method #1
animation.Stop();

// Stop method #2
UxrAnimatedMaterial.Stop(gameObject);
```

How do I animate a material over a limited amount of time, with the maximum flexibility and do something when finished?

```c#
UxrAnimatedMaterial animation = UxrAnimatedMaterial.AnimateInterpolation(gameObject,
                                                                         materialSlotIndex,
                                                                         UxrMaterialMode.InstanceOnly,
                                                                         UxrMaterialParameterType.Float,
                                                                         "ParameterName",
                                                                         Vector4.one * startValue,
                                                                         Vector4.one * endValue,
                                                                         new UxrInterpolationSettings(durationSeconds, delaySeconds, UxrEasing.Linear),
                                                                         () => DoSomethingAtTheEnd());
```

How can I make an object start moving linearly in world space?

```c#
// Will start moving 3 units per second in the world forward direction
UxrAnimatedTransform animation = UxrAnimatedTransform.Translate(gameObject, UxrTransformTranslationSpace.World, Vector3.forward * 3.0f);

// Stop
animation.Stop();
```

How can I make an object start bouncing in local space?

```c#
// Start bouncing looping forever in the local Y direction where from top (above 2 units) to bottom (local zero) it takes 0.5 seconds.
UxrAnimatedTransform animation = UxrAnimatedTransform.PositionInterpolation(gameObject, UxrTransformTranslationSpace.Local, Vector3.zero, Vector3.up * 2.0f, new UxrInterpolationSettings(0.5f, 0.0f, UxrEasing.EaseOutQuart, UxrLoopMode.PingPong));

// Stop
animation.Stop();
```

How can I make an object start facing towards the avatar eye?

```c#
UxrLookAtLocalAvatar.MakeLookAt(gameObject, allowRotateAroundVerticalAxis, allowRotateAroundHorizontalAxis, useInvertedForwardAxis);

// Stop
UxrLookAtLocalAvatar.RemoveLookAt(gameObject);
```
 
## Avatar

How do I access my avatar at any point in the application?

```c#
UxrAvatar myAvatar = UxrAvatar.LocalAvatar;
```

How do I teleport my avatar?

Method 1: Instant teleport:

```c#
UxrManager.Instance.MoveAvatarTo(UxrAvatar.LocalAvatar, Vector3.zero);
```

Method 2: Teleporting with a fadeout and fadein.

```c#

// Quick way

UxrManager.Instance.TeleportLocalAvatar(Vector3.zero, Quaternion.identity, UxrTranslationType.Fade);

// Taking into account re-parenting to destination and/or moving platforms

Transform destination         = ...
bool      parentToDestination = true;
UxrManager.Instance.TeleportLocalAvatarRelative(destination, parentToDestination, destination.position, Quaternion.identity, UxrTranslationType.Fade);
```

Method 3: Teleporting with a fadeout and fadein using async/await

```c#

// Quick way

Debug.Log("Teleport start");
await UxrManager.Instance.TeleportLocalAvatarAsync(Vector3.zero, Quaternion.identity, UxrTranslationType.Fade);
Debug.Log("Teleport finished");

// Taking into account re-parenting to destination and/or moving platforms

Transform destination         = ...
bool      parentToDestination = true;

Debug.Log("Teleport start");
await UxrManager.Instance.TeleportLocalAvatarRelativeAsync(destination, parentToDestination, destination.position, Quaternion.identity, UxrTranslationType.Fade);
Debug.Log("Teleport finished");
```

Method 4: Teleporting with a fadeout and fadein using callbacks:

```c#
void SwitchScenario()
{
    // Enable/Disable objects here, because the screen is completely faded out.
}

void PlaySound()
{
    // Play sound after the screen has faded back in.
}

float teleportDurationSeconds = 0.3f;
UxrManager.Instance.TeleportLocalAvatar(Vector3.zero, Quaternion.identity, UxrTranslationType.Fade, teleportDurationSeconds, SwitchScenario, PlaySound);
```

How can I get notified when the avatar moved or teleported?

```c#
private void OnEnable()
{
    UxrManager.AvatarMoved += UxrManager_AvatarMoved;
}

private void OnDisable()
{
    UxrManager.AvatarMoved -= UxrManager_AvatarMoved;
}

private void UxrManager_AvatarMoved(object sender, UxrAvatarMoveEventArgs e)
{
    Debug.Log($"Avatar moved from {e.OldPosition} to {e.NewPosition}");
}
```

How can I do something with the avatar each frame after UltimateXR finished updating it?

```c#
private void OnEnable()
{
    UxrManager.AvatarsUpdated += UxrManager_AvatarsUpdated;
}

private void OnDisable()
{
    UxrManager.AvatarsUpdated -= UxrManager_AvatarsUpdated;
}

private void UxrManager_AvatarsUpdated()
{
    Debug.Log("Avatars finished updating this frame");
}
```

How do I access bones in my avatar rig?

Example 1: Hand bone

```c#
UxrAvatarHand leftHand = UxrAvatar.LocalAvatar.GetHand(UxrHandSide.Left);
Vector3 thumbTip = leftHand.Thumb.Distal.position;
```

Example 2: Head

```c#
Vector3 headPos = UxrAvatar.LocalAvatar. AvatarRig.Head.Head.position;
```

Example 3: All bones in left hand

```c#
foreach (Transform boneTransform in UxrAvatar.LocalAvatar.GetHand(UxrHandSide.Right))
{
    Debug.Log(boneTransform.name);
}
```

Example 4: All bones in rig

```c#
foreach (Transform boneTransform in UxrAvatar.LocalAvatar.AvatarRig.Transforms)
{
    Debug.Log(boneTransform.name);
}
```

How do I know where the current camera position is?

```c#
Vector3 cameraPos = UxrAvatar.LocalAvatar.CameraPosition;
```

How do I know where the avatar is currently looking?

```c#
Vector3 viewDirection = UxrAvatar.LocalAvatar.CameraForward;
```

How can I prevent other avatars in the scene from updating using code? I want to control them manually because I’m developing a multi-player game.

```c#
avatar.AvatarMode = UxrAvatarMode.UpdateExternally;
```

How do I check if an avatar is peeking through a wall?

```c#
// This requires an UxrCameraWallFade on the avatar’s camera to work, although if it is not present it will just return false
bool isPeeking = UxrCameraWallFade.IsAvatarPeekingThroughGeometry(UxrAvatar.LocalAvatar);
```

How do I fade the view? I want to fade the screen out

```c#
// Enable fade with total black opacity
UxrAvatar.LocalAvatar.CameraFade.EnableFadeColor(Color.black, 1.0f);

// Disable fade
UxrAvatar.LocalAvatar.CameraFade.DisableFadeColor();
```

How can I between showing the avatar and showing a virtual representation of the controllers that I’m currently holding?
*Note*: This feature is currently supported but we are finalizing the legal terms to include the graphics for the controllers.

```c#
// Hide the avatar and show the controllers
UxrAvatar.LocalAvatar.RenderMode = UxrAvatarRenderModes.AllControllers;

// Show hands grabbing the controllers using inverse kinematics and mimicking user input
UxrAvatar.LocalAvatar.ShowControllerHands = true;

// Hide hands and show only the controllers
UxrAvatar.LocalAvatar.ShowControllerHands = false;

// Switch back to showing the avatar
UxrAvatar.LocalAvatar.RenderMode = UxrAvatarRenderModes.Avatar;
```

How can I list the hand pose names in the avatar?

```c#
foreach (UxrHandPoseAsset handPose in UxrAvatar.LocalAvatar.GetAllHandPoses)
{
    Debug.Log(HandPose.name);
}
```

How can I override/restore the current default pose that the avatar has while idling:

```c#
UxrStandardAvatarController avatarController = UxrAvatar.LocalAvatar.AvatarController as UxrStandardAvatarController;

avatarController.LeftHandDefaultPoseNameOverride = "newPoseName";
avatarController.RightHandDefaultPoseNameOverride = "newPoseName";

// Restore
avatarController.LeftHandDefaultPoseNameOverride = null;
avatarController.RightHandDefaultPoseNameOverride = null;
```

How can I change the blend value for a grab hand pose while an object is being grabbed? I want to see how the avatar presses the trigger while holding a gun, and the grab is a pose that blends to the 
trigger being pressed.

```c#
if (UxrGrabManager.Instance.GetGrabbingHand(grabbableGun, 0, out UxrGrabber grabber))
{
    float triggerPressAmount = UxrAvatar.LocalAvatarInput.GetInput1D(grabber.Side, UxrInput1D.Trigger);
    grabbableGun.GetGrabPoint(0).GetGripPoseInfo(grabber.Avatar).PoseBlendValue = triggerPressAmount;
}
```

## Guides

How can I show a guiding arrow that instructs the user to look at a specific object?

```c#
// Enable
UxrCompass.Instance.SetTarget(myObject.transform, UxrCompassDisplayMode.Look);

// Same but when the object is in sight it won't show the blinking eye icon
UxrCompass.Instance.SetTarget(myObject.transform, UxrCompassDisplayMode.OnlyCompass);

// Disable
UxrCompass.Instance.SetTarget(null);
```

How can I instruct the user to go to a certain place?

```c#
// Enable
UxrCompass.Instance.SetTarget(floor.transform, UxrCompassDisplayMode.Location);

// Disable
UxrCompass.Instance.SetTarget(null);
```

How can I instruct the user to grab a certain object?

```c#
// Enable
UxrCompass.Instance.SetTarget(hammer.transform, UxrCompassDisplayMode.Grab);

// Disable
UxrCompass.Instance.SetTarget(null);
```

How can I instruct the user to use a certain tool?

```c#
// Enable
UxrCompass.Instance.SetTarget(tool.transform, UxrCompassDisplayMode.Use);

// Disable
UxrCompass.Instance.SetTarget(null);
```

## Haptics

How can I send a haptic impulse to a specific controller?

```c#
UxrAvatar.LocalAvatar.ControllerInput.SendHapticFeedback(UxrHandSide.Left, UxrHapticClipType.Click, 1.0f);
```

How can I send a haptic impulse only to the hands that are currently grabbing an object?

```c#
UxrAvatar.LocalAvatar.ControllerInput.SendGrabbableHapticFeedback (grabbableObject, UxrHapticClipType.RumbleFreqNormal);
```

How can I send a haptic clip based on an audio file but if the device does not support to play audio files as haptics, play a fallback impulse instead?

```c#
AudioClip audioClip = ...

UxrAvatar.LocalAvatar.ControllerInput.SendHapticFeedback(UxrHandSide.Left, new UxrHapticClip(audioClip, UxrHapticClickType.Click));
```

## Input/Tracking

How can I know the name of the headset that is connected?

```c#
Debug.Log(UxrTrackingDevice.HeadsetDeviceName);
```

How can I know the name of the controllers that are connected?

```c#
Debug.Log(UxrAvatar.LocalAvatarInput.LeftControllerName + ", " + UxrAvatar.LocalAvatarInput.RightControllerName);
```

How can I know if a controller has certain elements?

```c#
bool mainJoystickIsTouchpad = UxrAvatar.LocalAvatarInput.MainJoystickIsTouchpad;
bool hasTriggerAndGrab = UxrAvatar.LocalAvatarInput.HasControllerElements(UxrHandSide.Left, UxrControllerElements.Grip | UxrControllerElements.Trigger);
```

How can I know if a controller button was pressed?

Method1 (direct query):

```c#
bool wasPressed = UxrAvatar.LocalAvatarInput.GetButtonsPressDown(UxrHandSide.Left, UxrInputButtons.Button1);
```

Method2 (direct query):

```c#
bool wasPressed = UxrAvatar.LocalAvatarInput.GetButtonsEvent(UxrHandSide.Left, UxrInputButtons.Button1, UxrButtonEventType.PressDown);
```

Method3 (using event subscription):

```c#
UxrControllerInput.GlobalButtonStateChanged += Input_ButtonStateChanged;

private void Input_ButtonStateChanged(object sender, UxrInputButtonEventArgs e)
{
    Debug.Log($"Pressed {e.HandSide}, {e.Button}, {e.ButtonEventType}");
}
```

How can I know if two controller buttons are being pressed at the same time?

```c#
bool areBeingPressed = UxrAvatar.LocalAvatarInput.GetButtonsPress(UxrHandSide.Left, UxrInputButtons.Button1 | UxrInputButtons.Button2);
```

How can I set the user handedness?

```c#
UxrAvatar.LocalAvatarInput.Handedness = UxrHandedness.Left;
```

How can I get a trigger input based on handedness instead of physical side?

```c#
float trigger = UxrAvatar.LocalAvatarInput.GetInput1D(UxrAvatar.LocalAvatarInput.Handedness, UxrInput1D.Trigger);
```

How can I set to ignore input?

```c#
UxrAvatar.LocalAvatarInput.SetIgnoreControllerInput(UxrHandSide.Left, true);
```

How can I still know if a controller button was pressed on a controller even if it was set to ignore input?

```c#
bool wasPressedWhileIgnoring = UxrAvatar.LocalAvatarInput.GetButtonsPressDown(UxrHandSide.Left, UxrInputButtons.Button1, true);
```

How can I get the forward direction of a controller?

```c#
Vector3 forward = UxrAvatar.LocalAvatar.GetControllerInputForward(UxrHandSide.Left).forward;
```

When the avatar is rendering the controllers (avatar.RenderMode = UxrAvatarRenderModes.AllControllers), how can I make a certain button blink for a tutorial that I’m making?
*Note*: This feature is currently supported but we are finalizing the legal terms to include the graphics for the controllers.

```c#
// Start blinking the trigger and grip buttons 3 times per second for 2 seconds
UxrAvatar.LocalAvatarInput.StartControllerElementsBlinking(UxrHandSide.Left, UxrControllerElements.Trigger | UxrControllerElements.Grip, Color.white, 3.0f, 2.0f);

// Stop trigger blinking
UxrAvatar.LocalAvatarInput.StopControllerElementsBlinking(UxrHandSide.Left, UxrControllerElements.Trigger);

// Stop all remaining 
UxrAvatar.LocalAvatarInput.StopAllBlinking(UxrHandSide.Left);
```

How can I get the speed/velocity of the hands?

```c#
if (UxrKeyboardInput.GetPressDown(UxrKey.Enter))
{
    // Velocity using current-last frame data:
    Vector3 leftInstantVelocity  = UxrAvatar.LocalAvatar.GetGrabber(UxrHandSide.Left).Velocity;
	Vector3 rightInstantVelocity = UxrAvatar.LocalAvatar.GetGrabber(UxrHandSide.Right).Velocity;
	
	// Velocity using multiple frame data:
	Vector3 leftSmoothVelocity   = UxrAvatar.LocalAvatar.GetGrabber(UxrHandSide.Left).SmoothVelocity;
	Vector3 rightSmoothVelocity  = UxrAvatar.LocalAvatar.GetGrabber(UxrHandSide.Right).SmoothVelocity;
}
```

How can I get keyboard input?

```c#
if (UxrKeyboardInput.GetPressDown(UxrKey.Enter))
{
    Debug.Log("Enter key was pressed");
}
```

## Manipulation

How do I know if an object is currently being grabbed?

Method1:

```c#
bool isBeingGrabbed = UxrGrabManager.Instance.IsBeingGrabbed(grabbableObject);
```

Method2:

```c#
bool isBeingGrabbed = grabbableObject.IsBeingGrabbed;
```

How do I get the object if it is being grabbed with a specific hand?

```c#
if (UxrGrabManager.Instance.GetObjectBeingGrabbed(avatar, UxrHandSide.Left, out UxrGrabbableObject grabbableObject)
{
    Debug.Log($"Object {grabbableObject.name} is being grabbed using the left hand");
}
```

How do I get the grabbing hand of an object?

```c#
if (UxrGrabManager.Instance.GetGrabbingHand(grabbableObject, 0, out UxrGrabber grabber))
{
    Debug.Log($"Avatar {grabber.Avatar.name} is grabbing object using the {grabber.Side} hand");
}
```

How do I get the grabbing hands if an object can be grabbed with more than one?

```c#
// Querying a specific grab point
if (UxrGrabManager.Instance.GetGrabbingHands(grabbableObject, 0, out List<UxrGrabber> grabbers))
{
}

// Querying all grab points
if (UxrGrabManager.Instance.GetGrabbingHands(grabbableObject, -1, out List<UxrGrabber> grabbers))
{
}
```

How can I force to keep a UxrGrabbableObject in place so that it can’t be moved?

```c#
grabbableObject.IsLockedInPlace = true;
```

How can I enable/disable a grab point in a grabbable object?

```c#
// Disable so that it can't be used
grabbableObject.SetGrabPointEnabled(0, false);

// Enable so that it can be used again
grabbableObject.SetGrabPointEnabled(0, true);

// Enable all grab points
grabbableObject.EnableAllGrabPoints();
```

How do I force to release a grabbed object?

Method1:

```c#
UxrGrabManager.Instance.ReleaseGrabs(grabbableObject, true);
```

Method2:

```c#
grabbableObject.ReleaseGrabs(true);
```

How do I place a grabbable object on an anchor?

```c#
// Instant placement
UxrGrabManager.Instance.PlaceObject(grabbableObject, GrabbleObjectAnchor, UxrPlacementType.Immediate, true);

// Smooth placement
UxrGrabManager.Instance.PlaceObject(grabbableObject, GrabbleObjectAnchor, UxrPlacementType.Smooth, true);
```

How do I get the velocity of an object being grabbed?

```c#
Vector3 velocity = UxrGrabManager.Instance.GetGrabbedObjectVelocity(grabbableObject);
```

How do I get the angular velocity of an object being grabbed?

```c#
Vector3 angularVelocity = UxrGrabManager.Instance.GetGrabbedObjectAngularVelocity(grabbableObject);
```

How do I create events to detect any object manipulation?

```c#
private void OnEnable()
{
    UxrGrabManager.Instance.ObjectGrabbing           += UxrGrabManager_ObjectGrabbing;
    UxrGrabManager.Instance.ObjectGrabbed            += UxrGrabManager_ObjectGrabbed;
    UxrGrabManager.Instance.ObjectReleasing          += UxrGrabManager_ObjectReleasing;
    UxrGrabManager.Instance.ObjectReleased           += UxrGrabManager_ObjectReleased;
    UxrGrabManager.Instance.ObjectPlacing            += UxrGrabManager_ObjectPlacing;
    UxrGrabManager.Instance.ObjectPlaced             += UxrGrabManager_ObjectPlaced;
    UxrGrabManager.Instance.ObjectRemoving           += UxrGrabManager_ObjectRemoving;
    UxrGrabManager.Instance.ObjectRemoved            += UxrGrabManager_ObjectRemoved;
    UxrGrabManager.Instance.AnchorRangeEntered       += UxrGrabManager_AnchorRangeEntered;
    UxrGrabManager.Instance.AnchorRangeLeft          += UxrGrabManager_AnchorRangeLeft;
    UxrGrabManager.Instance.PlacedObjectRangeEntered += UxrGrabManager_PlacedObjectRangeEntered;
    UxrGrabManager.Instance.PlacedObjectRangeLeft    += UxrGrabManager_PlacedObjectRangeLeft;
}

private void OnDisable()
{
    UxrGrabManager.Instance.ObjectGrabbing           -= UxrGrabManager_ObjectGrabbing;
    UxrGrabManager.Instance.ObjectGrabbed            -= UxrGrabManager_ObjectGrabbed;
    UxrGrabManager.Instance.ObjectReleasing          -= UxrGrabManager_ObjectReleasing;
    UxrGrabManager.Instance.ObjectReleased           -= UxrGrabManager_ObjectReleased;
    UxrGrabManager.Instance.ObjectPlacing            -= UxrGrabManager_ObjectPlacing;
    UxrGrabManager.Instance.ObjectPlaced             -= UxrGrabManager_ObjectPlaced;
    UxrGrabManager.Instance.ObjectRemoving           -= UxrGrabManager_ObjectRemoving;
    UxrGrabManager.Instance.ObjectRemoved            -= UxrGrabManager_ObjectRemoved;
    UxrGrabManager.Instance.AnchorRangeEntered       -= UxrGrabManager_AnchorRangeEntered;
    UxrGrabManager.Instance.AnchorRangeLeft          -= UxrGrabManager_AnchorRangeLeft;
    UxrGrabManager.Instance.PlacedObjectRangeEntered -= UxrGrabManager_PlacedObjectRangeEntered;
    UxrGrabManager.Instance.PlacedObjectRangeLeft    -= UxrGrabManager_PlacedObjectRangeLeft;
}

private void UxrGrabManager_ObjectGrabbing(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} is about to be grabbed by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_ObjectGrabbed(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} was grabbed by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_ObjectReleasing(object sender, UxrManipulationEventArgs e)
{
    // Parameter e.Grabber may be null if the object was released procedurally
    Debug.Log($"Object {e.GrabbableObject.name} is about to be released by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_ObjectReleased(object sender, UxrManipulationEventArgs e)
{
    // Parameter e.Grabber may be null if the object was released procedurally
    Debug.Log($"Object {e.GrabbableObject.name} was released by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_ObjectPlacing(object sender, UxrManipulationEventArgs e)
{
    // Parameter e.Grabber may be null if the object was placed procedurally
    Debug.Log($"Object {e.GrabbableObject.name} is about to be placed on anchor {e.GrabbableAnchor.name} by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_ObjectPlaced(object sender, UxrManipulationEventArgs e)
{
    // Parameter e.Grabber may be null if the object was placed procedurally
    Debug.Log($"Object {e.GrabbableObject.name} was placed on anchor {e.GrabbableAnchor.name} by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_ObjectRemoving(object sender, UxrManipulationEventArgs e)
{
    // Parameter e.Grabber may be null if the object was removed procedurally
    Debug.Log($"Object {e.GrabbableObject.name} was is about to be removed from anchor {e.GrabbableAnchor.name} by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_ObjectRemoved(object sender, UxrManipulationEventArgs e)
{
    // Parameter e.Grabber may be null if the object was removed procedurally
    Debug.Log($"Object {e.GrabbableObject.name} was is was removed from anchor {e.GrabbableAnchor.name} by {e.Grabber.Avatar.name}");
}

private void UxrGrabManager_AnchorRangeEntered(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} grabbed by {e.Grabber.Avatar.name} entered the valid drop range of anchor {e.GrabbableAnchor.name}");
}

private void UxrGrabManager_AnchorRangeLeft(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} grabbed by {e.Grabber.Avatar.name} left the valid drop range of anchor {e.GrabbableAnchor.name}");
}

private void UxrGrabManager_PlacedObjectRangeEntered(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"{e.Grabber.Side} hand of {e.Grabber.Avatar.name} entered the valid grab distance of object {e.GrabbableObject.name} currently placed on anchor {e.GrabbableAnchor.name}");
}

private void UxrGrabManager_PlacedObjectRangeLeft(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"{e.Grabber.Side} hand of {e.Grabber.Avatar.name} left the valid grab distance of object {e.GrabbableObject.name} currently placed on anchor {e.GrabbableAnchor.name}");
}
```

How do I create events to detect manipulation on a specific object?

Method1: If you are dealing with an external UxrGrabbableObject named _grabbableObject

```c#
private void OnEnable()
{
    _grabbableObject.Grabbing            += GrabbableObject_Grabbing;
    _grabbableObject.Grabbed             += GrabbableObject_Grabbed;
    _grabbableObject.Releasing           += GrabbableObject_Releasing;
    _grabbableObject.Released            += GrabbableObject_Released;
    _grabbableObject.Placing             += GrabbableObject_Placing;
    _grabbableObject.Placed              += GrabbableObject_Placed;
    _grabbableObject.ConstraintsApplying += GrabbableObject_ConstraintsApplying;
    _grabbableObject.ConstraintsApplied  += GrabbableObject_ConstraintsApplied;
    _grabbableObject.ConstraintsFinished += GrabbableObject_ConstraintsFinished;
}

private void OnDisable()
{
    _grabbableObject.Grabbing            -= GrabbableObject_Grabbing;
    _grabbableObject.Grabbed             -= GrabbableObject_Grabbed;
    _grabbableObject.Releasing           -= GrabbableObject_Releasing;
    _grabbableObject.Released            -= GrabbableObject_Released;
    _grabbableObject.Placing             -= GrabbableObject_Placing;
    _grabbableObject.Placed              -= GrabbableObject_Placed;
    _grabbableObject.ConstraintsApplying -= GrabbableObject_ConstraintsApplying;
    _grabbableObject.ConstraintsApplied  -= GrabbableObject_ConstraintsApplied;
    _grabbableObject.ConstraintsFinished -= GrabbableObject_ConstraintsFinished;
}

private void GrabbableObject_Grabbing(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} is about to be grabbed by avatar {e.Grabber.Avatar.name}");
}

private void GrabbableObject_Grabbed(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} was grabbed by avatar {e.Grabber.Avatar.name}");
}

private void GrabbableObject_Releasing(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} is about to be released by avatar {e.Grabber.Avatar.name}");
}

private void GrabbableObject_Released(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} was released by avatar {e.Grabber.Avatar.name}");
}

private void GrabbableObject_Placing(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} is about to be placed on anchor {e.GrabbableAnchor.name} by avatar {e.Grabber.Avatar.name}");
}

private void GrabbableObject_Placed(object sender, UxrManipulationEventArgs e)
{
    Debug.Log($"Object {e.GrabbableObject.name} was placed on anchor {e.GrabbableAnchor.name} by avatar {e.Grabber.Avatar.name}");
}

private void GrabbableObject_ConstraintsApplying(object sender, UxrApplyConstraintsEventArgs e)
{
    Debug.Log($"Object {_grabbableObject.name} is about to be constrained (if required)");
}

private void GrabbableObject_ConstraintsApplied(object sender, UxrApplyConstraintsEventArgs e)
{
    Debug.Log($"Object {_grabbableObject.name} was constrained and can now be constrained using user specific code");
}

private void GrabbableObject_ConstraintsFinished(object sender, UxrApplyConstraintsEventArgs e)
{
    Debug.Log($"All constraints on object {_grabbableObject.name} were applied");
}
```

Method2: Remove boilerplate if you are creating a component class that will be added to a GameObject with an UxrgrabbableObject by inheriting from UxrGrabbableObjectComponent<T>

```c#
class MyComponent : UxrGrabbableObjectComponent<MyComponent>
{
    protected override void OnObjectGrabbing(UxrManipulationEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} is about to be grabbed by avatar {e.Grabber.Avatar.name}");
    }

    protected override void OnObjectGrabbed(UxrManipulationEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} was grabbed by avatar {e.Grabber.Avatar.name}");
    }

    protected override void OnObjectReleasing(UxrManipulationEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} is about to be released by avatar {e.Grabber.Avatar.name}");
    }

    protected override void OnObjectReleased(UxrManipulationEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} was released by avatar {e.Grabber.Avatar.name}");
    }

    protected override void OnObjectPlacing(UxrManipulationEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} is about to be placed on anchor {e.GrabbableAnchor.name} by avatar {e.Grabber.Avatar.name}");
    }

    protected override void OnObjectPlaced(UxrManipulationEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} was placed on anchor {e.GrabbableAnchor.name} by avatar {e.Grabber.Avatar.name}");
    }

    protected override void OnObjectConstraintsApplying(UxrApplyConstraintsEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} is about to be constrained (if required)");
    }

    protected override void OnObjectConstraintsApplied(UxrApplyConstraintsEventArgs e)
    {
        Debug.Log($"Object {e.GrabbableObject.name} was constrained and can now be constrained using user specific code");
    }

    protected override void OnObjectConstraintsFinished(UxrApplyConstraintsEventArgs e)
    {
        Debug.Log($"All constraints on object {e.GrabbableObject.name} were applied");
    }
}
```

How do I apply manual constraints or modifications on a grabbable object making sure that I don’t mess up with UltimateXR’s updating?

```c#
private void OnEnable()
{
    _grabbableObject.ConstraintsApplied += grabbableObject_ConstraintsApplied;
}

private void OnDisable()
{
    _grabbableObject.ConstraintsApplied -= grabbableObject_ConstraintsApplied;
}

// This method is called every frame right after UltimateXR updated the grabbable object
// position/rotation and applied all necessary constraints.
// We can now apply our own constraints on it or other effects. This can be used
// to add more complex behavior to a grabbed object. Some examples:
//   -Add recoil to a custom weapon.
//   -Shake an object while holding it.
//   -Apply more complex translation/rotation constraints than the UxrGrabbableObject provides.
private void grabbableObject_ConstraintsApplied(UxrApplyConstraintsEventArgs e)
{
    // Keeps the local y position between 0.8 and 1.2
    Vector3 localPosition = _grabbableObject.transform.localPosition;
    localPosition.y = Mathf.Clamp(localPosition.y, 0.8f, 1.2f);
    _grabbableObject.transform.localPosition = localPosition;
}
```

## Math

There are tons of utilities and extensions in files such as Vector3Ext, QuaternionExt, TransformExt, Matrix4x4Ext, MeshExt... There is also the static UxrInterpolator class and curve classes like UxrSpline.

How can I get rotations and positions smoothed over time?

```c#
float smooth = 0.1f; // value between range [0.0, 1.0] telling how much to smooth the value

float smoothedValue = UxrInterpolator.SmoothDamp(oldValue, newValue, smooth);
Vector3 smoothedPos = UxrInterpolator.SmoothDamp(oldPos, newPos, smooth);
Quaternion smoothedRotation = UxrInterpolator.SmoothDamp(oldRot, newRot, smooth);
```

How can I interpolate between 2 values using different easings?

```c#
float  interpolatedValue = UxrInterpolator.Interpolate(startValue, endValue, t, UxrEasing.Linear);
Vector3 interpolatedPos = UxrInterpolator.Interpolate(startPos, endPos, t, UxrEasing.EaseOutQuad);
Quaternion interpolatedRot = UxrInterpolator.Interpolate(startRot, endRot, t, UxrEasing.InOutCubic);
```

How can I interpolate so that I make a ball bounce?

```c#
// Wait for 1 second, then start bouncing from top to floor taking 0.5 seconds and keep looping using ping-pong for 10 seconds. Use -1 to keep bouncing indefinitely.
Vector3 pos = UxrInterpolator.Interpolate(topPos, floorPos, 0.5f, 1.0f, Time.time, UxrEasing.EaseInQuart, UxrLoopMode.PingPong, 10.0f);
```

How can I move and rotate an object A so that if B was a child, it would make B align with C?

```c#
// Uses Transform extensions defined in TransformExt.cs
A.transform.ApplyAlignment(B, C);
```

How can I mirror an object A using a mirror transform B using its Z axis as mirror normal?

```c#
// Uses Transform extensions defined in TransformExt.cs
A.transform.ApplyMirroring(B.position, B.forward, MirrorType.MirrorYZ);
```

How can I create a spline and interpolate using an arc-length parameter instead of the usual t?

```c#
UxrCatmullRomSpline spline = new UxrCatmullRomSpline();
spline.Create(1.0f, v1, v2, v3, v4, v5);
float halfLength = spline.ArcLength / 2.0f;

if (spline.EvaluateUsingArcLength(halfLength, out Vector3 position, out Vector3 direction))
{
    Debug.Log($"Interpolation in the middle: Pos={position}, Dir={direction}");
}
```
 
## Networking

**[Edit]** Version 1.0.0 will soon be released and is currently available as a preview on [GitHub](https://github.com/VRMADA/ultimatexr-unity/tree/release/v1.0.0). It has native networking capabilities and connectors for the most popular SDKs. Check it out!

UltimateXR doesn’t provide native networking components, but currently provides ways to easily sync the most significant events: avatar events (hand poses, teleportation...) and manipulation (grabs, releases, placing...).

Synchronization can be done using the IUxrStateSync interface, which provides a generic way to subscribe to a state change event and the other way around, given a state change event be able to execute it. The process would be sending the events received in the source computer (IUxrStateSync.StateChanged) to remote computers and execute them there (IUxrStateSync.SyncState).

In addition to the events, we recommend synching the required avatar transforms except for the fingers, which are already taken care of using the avatar hand pose events to leverage bandwidth. Functionality to synchronize transforms is provided by most networking packages.

To identify the same component in both PCs, so that events are processed by the same component in source and destination, we provide the UxrComponent.UniqueId property and the static UxrComponent.TryGetComponentById() method.

The IUxrStateSync interface is implemented for now by the local avatar (UxrAvatar.LocalAvatar) and the grab manager (UxrGrabManager.Instance).

To synchronize one would need to:

- Subscribe to the StateChanged event
- Serialize all events being generated using UxrComponent.UniqueId for properties that reference components, such as the avatar in UxrAvatar events or the grabber/grabbableObject/anchor in manipulation events)
- Send them to the other client PCs
- Deserialize the events in the client PCs, using UxrComponent.TryGetComponentById() to get the components referenced by the event.
- Execute the events using the SyncState method.

## UI

It is often useful to add UxrControlInput components to Unity UI elements such as images and buttons in order to use event handling programatically. 

How do I get notified when a button was clicked?

```c#
[SerializeField] private UxrControlInput _controlInput;

private void OnEnable()
{
    _controlInput.Clicked += Control_Clicked;
}

private void OnDisable()
{
    _controlInput.Clicked -= Control_Clicked;
}

private void Control_Clicked(UxrControlInput controlInput, PointerEventData eventData)
{
    Debug.Log("The button was clicked");
}
```

Are there any more UxrControlInput events that I can subscribe to?

```c#
DragStarted
Dragged
DragEnded
Dropped
Pressed
Released
Clicked
PressHeld
CursorEntered
CursorExited
UpdateSelected
InputSubmitted
```

Are there any static UxrControlInput events that I can subscribe to so that I get notified whenever any UxrControlInput generated the event?

```c#
GlobalPressed
GlobalReleased
GlobalClicked
GlobalDragStarted
GlobalDragged
GlobalDragEnded
```

How can I make a Unity Graphic component (Text, Image...) blink using the alpha value?

```c#
// Blink 8 times per second during 1 second and restore the original color value
UxrColorTween.AnimateBlinkAlpha(textComponent, 8.0f, 1.0f) .SetFinishedActions(UxrTweenFinishedActions.RestoreOriginalValue);
```

How can I fade in a CanvasGroup using tweening?

```c#
UxrCanvasAlphaTween.FadeIn(canvasGroup, fadeSeconds, delaySeconds).gameObject.SetActive(true);
```

How can I fade out a CanvasGroup starting with the current alpha and deactivate the GameObject when finished?

```c#
UxrCanvasAlphaTween.Animate(canvasGroup, canvasGroup.alpha, 0.0f, new UxrInterpolationSettings(fadeSeconds)).SetFinishedActions(UxrTweenFinishedActions.DeactivateGameObject);
```

How can I move Unity Graphic components (Text, Image...) in using tweening?

```c#
// Animate from right to left in cascade using an increasing delay

float startHorizontalOffset = 500.0f;

UxrPositionTween.MoveIn(image1, startHorizontalOffset, 0.0f, new UxrInterpolationSettings(durationSeconds, 0.0f, UxrEasing.EaseOutQuad));
UxrPositionTween.MoveIn(image2, startHorizontalOffset, 0.0f, new UxrInterpolationSettings(durationSeconds, 0.1f, UxrEasing.EaseOutQuad));
UxrPositionTween.MoveIn(image3, startHorizontalOffset, 0.0f, new UxrInterpolationSettings(durationSeconds, 0.2f, UxrEasing.EaseOutQuad));
UxrPositionTween.MoveIn(image4, startHorizontalOffset, 0.0f, new UxrInterpolationSettings(durationSeconds, 0.3f, UxrEasing.EaseOutQuad));
```

How can I create a typewriter effect to print the name of a user in a Unity Text or TextMeshPro component?

```c#
UxrTextContentTween.Animate(textComponent.gameObject, string.Empty, name, new UxrInterpolationSettings(durationSeconds, delaySeconds));
```

How can I create a text effect to print the score in a Unity Text or TextMeshPro component so that the score is increased numerically and rings a bell when it reaches the end?

```c#
UxrTextContentTween.Animate(textComponent.gameObject, new UxrInterpolationSettings(durationSeconds, delaySeconds), () => RingBell(), "Final score: {0:000000}", 0, finalScore);
```
 
## UltimateXR Components

All UltimateXR components ultimately derive from UxrComponent, which inherits from MonoBehaviour.

How can I iterate over all UxrComponents in the scene? Enabled or not

```c#
foreach (UxrComponent component in UxrComponent.AllComponents)
{
    Debug.Log($"{component.name}: {component.GetType().Name}")
}
```

How can I iterate over all the enabled UxrComponents in the scene?

```c#
foreach (UxrComponent component in UxrComponent.EnabledComponents)
{
    Debug.Log($"{component.name}: {component.GetType().Name}")
}
```

How can I iterate over all enabled UxrComponent<T> in the scene?
*Note*: UxrComponent<T> is used when it is interesting to be able to iterate over all components of the same type.

```c#
foreach (UxrGrabbableObject grabbableObject in UxrGrabbableObject.EnabledComponents)
{
    Debug.Log($"Found grabbable object {grabbableObject.name}");
}
```

How can I set up an event so that I get notified every time an UxrComponent is enabled?

```c#
UxrComponent.GlobalEnabled += UxrComponent_GlobalEnabled;

private void UxrComponent_GlobalEnabled(UxrComponent component)
{
    Debug.Log($"Component {component.name} of type {component.GetType().Name} was enabled");
}
```

How can I get a unique ID for an object that derives from UxrComponent?

```c#
UxrComponent component = ...
Debug.Log(component.UniqueId);
```

How can I get the UxrComponent with a given unique ID?

```c#
if (UxrComponent.TryGetComponentById(uniqueId, out UxrComponent component))
{
    Debug.Log($"Found {component.name}");
}
```

How can I get the full path in the scene of a given component? 
*Note*: This will work on any Unity component and uses the ComponentExt extension.

```c#
Component component = ...
Debug.Log(component.GetPathUnderScene());
```

How can I get a unique full scene path by including sibling index, type and component index information?
*Note*: This will work on any Unity component and uses the ComponentExt extension.

```c#
Component component = ...
Debug.Log(component.GetUniqueScenePath());
```
