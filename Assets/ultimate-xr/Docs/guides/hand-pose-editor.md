---
title: "Hand Pose Editor"
---

# Using the Hand Pose Editor

The UltimateXR Hand Pose Editor is an important tool when adding interactivity to our avatars. It simplifies the creation of hand poses that can be used at runtime to make hand gestures or to grab objects with different shapes.

![](/guides/media/avatars/01Editor.png)
 
Hand poses are used primarily by these components:

- *UxrStandardAvatarController*: Present in the root GameObject of our avatar. It controls which hand poses are used depending on the controller buttons that are pressed.
There are different pose types that can be specified:
  - *Grab*: Normally assigned to the grip button. This special pose will also trigger the grabbing functionality to interact with UxrGrabbableObject components.
  - *Pointing*: It can be assigned to a button but if you don’t want the user to be able to point with the hands using buttons you can specify Everything as the button combination. It is a special pose because when the hand is inside a volume defined by a UxrFingerPointingVolume component, the hand will adopt the pointing pose. It is used in front of UIs to facilitate touch interaction.
  - *Other*: These are poses that can be assigned to button events but don’t have any special meaning besides adopting the pose whenever the button is pressed.
- *UxrGrabbableObject*: Added to objects so that they can be grabbed. Here when snapping is enabled it allows you to specify which hand pose will be used to grab that specific object.
- *UxrFirearmWeapon*: This component depends on a UxrGrabbableObject also present in the same GameObject, which is used to grab the weapon. The UxrGrabbableObject should have a blend pose assigned -more on blend poses later- that blends from trigger released to trigger completely pulled.

You can access the hand pose editor using the Hand Poses section in the UxrAvatar component of an avatar in the scene.

![](/guides/media/avatars/02UxrAvatar.png)
 
Or by using the Tools -> UltimateXR -> Hand Pose Editor menu.

![](/guides/media/avatars/03Menu.png)
 
Let’s first list some important concepts to understand how hand poses work:

- You edit hand poses of an avatar by loading the avatar in the Hand Pose Editor.
- Hand Poses are created by the Hand Pose Editor, added to an avatar and stored in an .asset file in your project.
- An avatar can have many different hand poses.
- A special pose is the default hand pose, which is the pose the hands will adopt when there is no user input and no events that change it.
- There are two types of hand poses:
  - *Fixed*: A hand pose with fixed bone orientations.
  - *Blend*: A hand pose defined by two different hand poses. A blend pose allows the hand to adopt any pose in between the two. Blend poses are useful to create generic grab poses where the start pose adopts a completely open grip and the second pose adopts a completely closed grip. This can then be used in UxrGrabbableObject components where a slider will let you control the pose that perfectly fits an object. UltimateXR avatars come with a set of common blend poses that can be used to grab most objects. Blend poses are also used in cases where you want to blend between two poses while an object is being grabbed, such as a firearm. The start pose would have the trigger released and the end pose the trigger pulled. Depending on the controller’s analog trigger button the pose would blend from start to end.
- If you create a prefab variant of an avatar, the prefab variant will inherit all hand poses of the parent prefabs and will be able to use them. However, these hand poses will be marked as read-only in the editor to prevent changing poses that don’t belong to the avatar, but to a parent. 
- You can create a new hand pose with the same name as an inherited pose. In this case the pose will be shown as an “override” pose.
- The editor will detect the coordinate system used by each avatar. It should be able to import any rig and even exchange hand pose assets between different avatars using different coordinate system, although the results may vary.

Being able to work with avatar prefab variants and inherit poses is a powerful feature. Among other things, it allows you to separate functionality and create specializations. For example, our sample scene uses prefab variants for all avatars (cyborg, big hands, small hands). This allows us to inherit all basic hand poses available in the parent prefabs but still add new ones that do not belong to the core functionality, such as grabbing the shooting range weapons and using the laser in the lab. These poses are specific to the example scene, and do not belong in the UltimateXR core prefabs.

![](/guides/media/avatars/04PoseInheritance.png)

## Using the main menu

![](/guides/media/avatars/05MainMenu.png)
 
*Create*

- Create New Pose: Creates a new pose for the avatar.
- Copy Current Pose to New: Creates a new pose for the avatar using the current pose finger orientations.
- Add Pose from File: Creates a new pose for the avatar loading a pose asset file from disk. It can be a pose from a different avatar, independently of which coordinate system they use, but the result may vary depending on how different they are rigged. It can be used as a starting point to improve on.
- Add all Poses from Folder: Creates a set of poses for the avatar using all the pose asset files on a disk folder. They can be from different avatars, independently of which coordinate system they use, but the result may vary.
- Add all Preset Poses: Will add all poses in the preset window to the avatar.

*Delete*

- Delete Current Pose: Will delete the currently selected pose from the avatar and remove the .asset file from disk.
- Delete All Poses: Will delete all avatar poses and remove the .asset files from disk.

*Misc*

- Set Current Pose as Default: Will set the currently selected pose as the default pose for the avatar. The default pose is the pose the avatar hands will adopt when there is no user input and no events that change the pose.
- Select Current Pose Asset File: Selects and highlights the current pose asset file in the project window.
- Bake Pose in Avatar: Saves the current finger transforms in the avatar prefab.

*Prefab Hierarchy*

•	If the selected avatar is a prefab variant, it will show a top to bottom prefab hierarchy where the root parent prefab is at the top and the avatar being edited at the bottom.
 
## Using the pose menu

![](/guides/media/avatars/06PoseMenu.png)
 
- Show inherited poses: Lets you choose between showing only the poses stored in the avatar being edited or also include the poses inherited through parent prefabs. Inherited poses will be read-only and can only be edited using the appropriate parent prefab.
- Pose: Lets you select the current pose being edited. When you switch between poses, the avatar hands in the scene window will preview the transforms of each pose.
- Pose Type: Lets you choose if the current pose is a fixed pose or a blend pose.
  - Fixed poses have fixed transforms.
  - Blend poses are defined by an open grip and a closed grip and allow to adopt any pose in between. They are used to provide flexible grab poses that can adapt to different object sizes. They can also be used to blend while an object is being grabbed, for example to pull a trigger while holding a firearm.
- Edit Blend Pose: Lets you select in a blend pose which one of the two poses (open or closed) is currently being edited.
- Pose Name: Lets you change the pose name. If you set the same name as an inherited pose, the pose will be shown as override. Override poses will hide the inherited pose and use the new pose instead.

## Using the Hand Controls

![](/guides/media/avatars/07HandsEdition.png)
 
The hand controls provide an easy way to rotate individual finger bones and to copy data from one hand to the other. When the hands show up without the spinners it means that there is no pose currently selected, or the selected pose is read-only because it’s from a parent prefab.
There are two ways finger transforms can be edited:

- Using the provided spinner controls. The spinner sensitivity will let you adjust how much the transforms rotate when dragging the controls.
- Using Unity to rotate the finger transforms like you would normally do. The hand pose editor will monitor changes and keep everything in sync. The Select finger bone in Hierarchy Window when interacting with spinner option can be handy to select the given object in the hierarchy window each time a spinner is clicked.

Using Unity to rotate can be especially useful when editing the thumb rotations, which can be buggy sometimes when using the spinner controls.

Options:

- Copy Left to Right/Right to Left: Will copy the current finger rotations from one hand to the other. When editing a blend pose, it will copy only the currently active Open Grip or Closed Grip.
- Reset Open Grip: Resets all finger transforms to the defaults in the prefab. When editing a blend pose, it will only reset the currently active Open Grip or Closed Grip.
- Load External: Lets you load an external hand pose asset file to the given hand, which may be from a different avatar. If it is from a different avatar, the results may vary a bit. When editing a blend pose, it will only override the currently active Open Grip or Closed Grip.
- Copy Pose: In blend poses, it allows you to copy finger orientations from one grip to another.
- Place Snap on Grabbable Object: If you are editing a grab pose by placing the grabbable object in the avatar hand and using the hand pose editor to adjust the grip on the object, this option can help you create a snap transform. You will need to have the grabbable object currently selected in the hierarchy window.
The created snap transform can later be assigned in the UxrGrabbableObject component to select where the hand should snap when grabbing the object using the pose.

## Using the hand pose presets

![](/guides/media/avatars/08Presets.png)
 
The presets window contains a set of commonly used poses that can speed up the creation process. By clicking on any preset, the editor will automatically load it into the avatar pose being edited.

The Hand Pose Editor automatically detects the coordinate system used by the avatar transforms, which means that no matter which coordinate system is used, you will be able to load presets and exchange poses between any avatar. This is a very power feature, however, since avatars are rigged in different ways, the quality of the results may vary. We expect to improve how the presets work but in most cases it provides a very good starting point that can quickly be improved manually.
