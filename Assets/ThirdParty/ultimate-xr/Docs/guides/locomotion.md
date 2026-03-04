---
title: "Locomotion"
---

# Locomotion

## Introduction

Locomotion in Virtual Reality is the technique that enables travelling from one place to another. Current headsets allow the user to explore the virtual world simply by walking around in the real world. In most cases, however, the available room space is not enough. In addition, there are other limitations that cannot be solved by walking around in a room, such as climbing up ladders, jumping off cliffs, or exploring huge scenarios.

Locomotion methods try to solve all these limitations.

One of the biggest challenges since VR became mainstream has been consolidating standard locomotion methods to explore the virtual world. Some of the reasons why it is so challenging are:

- *Motion sickness*, which happens when your eyes tell your brain you're moving around but your body feels still.
- *Intuitiveness*. Users must learn and get used to new input mechanics to move around.
- *Personal preferences*. Users have very different preferences, and it is very important to support them all.

UltimateXR provides built-in components to support the most common locomotion methods. Built-in locomotion components, such as *UxrTeleportLocomotion* or *UxrSmoothLocomotion*, can be placed on any object in the avatar hierarchy. The UltimateXR avatar prefabs, for example, come with teleport components by default. Switching to another locomotion method can be done by disabling these default components and adding other component(s) to any object in the avatar hierarchy.

The framework also provides great flexibility to create new mechanics. There is no limitation on the amount of active locomotion components in an avatar. Some might add a single feature to the already existing components, such as climbing, but it is important that they all can work together.

It is also possible to have a single custom locomotion component that takes care of everything.

## Teleporting

Teleporting is one of the most common ways to move around in Virtual Reality. It solves a wide range of limitations, such as being able to move up or down different levels and move quickly around vast fields with minimal motion sickness.

The avatar prefabs in UltimateXR come with teleporting components by default, which can be found hanging from the HandsIntegration object.

![](/guides/media/locomotion/01Hierarchy.png)
 
Teleporting uses one component for each hand. These default components can be disabled and changed by any other locomotion component(s). They can also be left enabled and complemented with new locomotion component(s) in the case they add more features.

At runtime, teleporting looks like this:

![](/guides/media/locomotion/02Teleporting.jpg)
 
## The UxrTeleportLocomotion component

![](/guides/media/locomotion/03TeleportComponent.jpg)

### General parameters
- *Controller Hand*: Which hand controls the input.
- *Use Controller Forward*: Will the teleport use the controller's forward vector instead of its own transform forward?
- *Parent To Destination*: Whether to parent the avatar to the destination object after teleport. Use it when building applications with moving vehicles or platforms the avatar can move on, so that the avatar keeps the relative position/orientation after teleporting.
- *Shake Filter*: The amount of filtering to apply to the hand movement to smooth it out.

### Translation
- *Translation Type*: Which translation method to use.
- *Translation Fade Color*: The fade color when Fade translation type is used.
- *Translation Fade Seconds*: The fade transition in seconds when Fade translation type is used.
- *Smooth Translation Seconds*: The translation duration in seconds when Smooth translation type is used.
- *Allow Joystick Back Step*: Whether to allow back steps by pressing the joystick down.
- *Back Step Distance*: The distance of each back step.

### Rotation
- *Rotation Type*: Which rotation method to use.
- *Rotation Step Degrees*: The amount of degrees of each turn for Immediate and Fade rotations.
- *Rotation Fade Color*: The fade color when Fade rotation is used.
- *Rotation Fade Seconds*: The fade transition in seconds when Fade rotation is used.
- *Smooth Rotation Seconds*: The rotation duration in seconds when Smooth rotation is used.
- *Reorient After Teleport*: How to orient the view right after teleporting.

### Target
- *Target*: Which teleport target to use. Can be either a prefab or an already instantiated object.
- *Target Placement Above Floor*: Offset applied to the teleport target to help placing it a little above the floor.
- *Show Target Also When Invalid*: Whether to show the target object also when the destination is not valid.
- *Target Color When Valid*: Target color to use when the destination is valid.
- *Target Color When Invalid*: Target color to use when the destination is not valid.

### Constraints
- *Trigger Colliders Interaction*: Whether colliders with the trigger property set will interact with the teleport raycasts.
- *Max Allowed Distance Travel*: Maximum allowed distance that can be travelled using each teleport.
- *Max Allowed Height Difference*: Maximum allowed height difference to be able to teleport.
- *Max Allowed Slope Degrees*: Maximum allowed slope degrees at destination.
- *Destination Validation Radius*: Radius of a cylinder that will be used to validate the destination surroundings to allow teleporting.
- *Valid Target Layers*: Valid layers for teleporting destination objects.
- *Blocking Target Layers*: Objects that will block teleporting raycasts.

### Arc
- *Arc Segments*: The number of subdivisions to use to render the arc.
- *Arc Width*: The width of the arc.
- *Arc Scroll Speed (Valid Target)*: The arc material scroll speed when the destination is valid.
- *Arc Scroll Speed (Invalid Target)*: The arc material scroll speed when the destination is invalid.
- *Arc Material (Valid Target)*: The arc material used when the destination is valid.
- *Arc Material (Invalid Target)*: The arc material used when the destination is invalid.
- *Arc Fade Length*: The fade length used when the arc exits the hand or collides with a target to hide intersections.
- *Blocking Raycast Steps Quality*: How many subdivisions along the arc to perform to raycasts against the scenario.

## Smooth Locomotion

Smooth Locomotion is an alternative movement system to Teleport Locomotion that uses continuous movement to travel across the virtual world. It can provide better immersion but is more prone to induce motion sickness. It is very popular among advanced videogames users.
The controls are similar to FPS (First Person Shooter) games, where the left joystick is used to move and the right is used to turn. 

### The UxrSmoothLocomotion component

The *UxrSmoothLocomotion* component in UltimateXR provides smooth locomotion capabilities to an avatar. It also serves as a demonstration of a possible subclass implementation of the *UxrLocomotion* component.

*Note*: The current smooth locomotion provided by *UxrSmoothLocomotion* is very basic. A more robust implementation will be released very soon.

Setting up *UxrSmoothLocomotion* on an avatar:

1. Have an avatar instantiated in the scene. You can use any of the UltimateXR avatar prefabs located in /UltimateXR/Prefabs/Avatars
2. Make sure that any *UxrTeleportLocomotion* components on your Avatar are inactive
3. Add the *UxrSmoothLocomotion* component anywhere in your Avatar's hierarchy

That's it! With the default settings, you can now smoothly move with the left controller joystick, and smoothly rotate with the right controller joystick.

What's Next? Tweak the following parameters to dial in the precise movement you desire.

![](/guides/media/locomotion/04SmoothLocomotion.jpg)
 
### General parameters
- *Parent To Destination*: Whether to parent the avatar to the destination object after teleport. Use it when building applications with moving vehicles or platforms the avatar can move on, so that the avatar keeps the relative position/orientation after teleporting.
- *Meters Per Second Normal*: Speed of normal joystick movement.
- *Meters Per Second Print*: Speed of movement while pressing the sprint button.
- *Walk Direction: Options for what the Up/Forward on the Joystick is based on*:
  - Controller Forward - Forward direction of your left controller.
  - Avatar Forward - Forward direction your Avatar.
  - Look Direction - Forward direction of your Camera.
- *Rotation Degrees Per Second Normal*: Speed of normal smooth rotation.
- *Rotation Degrees Per Second Sprint*: Speed of smooth rotation while sprinting.
- *Gravity*: Gravity Acceleration to apply in the Y direction. -9.81 approximates Earth gravity.

### Input parameters
Sprint Button Hand: Which Hand can enable sprint.
Sprint Button: Button to press to enable sprint.

### Constraints
- *Trigger Colliders Interaction*: Whether colliders with the trigger property set will interact with the teleport raycasts.
- *Collision Layer Mask*: Layers of colliders that will block your movement.
- *Capsule Radius*: In Meters, size of your body collider.
- *Max Step Height*: In Meters, you will climb geometry if it is within this height of your current ground.
- *Max Slope Degrees*: You will climb geometry if the slope is less than this amount.
- *Step Distance Check*: How far forwards to check for Max Steps & Max Slopes.

## Creating new locomotion components

To create your own locomotion components using scripting follow these steps:

1. Create a new class derived from *UxrLocomotion*
2. Override the IsSmoothLocomotion boolean property to indicate whether the component updates the avatar position every frame or at discrete times.
3. Override the UpdateLocomotion() method that will be called every frame.
4. Use methods in *UxrManager* such as MoveAvatarTo(), TranslateAvatar(), RotateAvatar(), TeleportLocalAvatar() so that the locomotion integrates correctly with the event system. This is important for other components in the framework such as LOD switching or in networking environments.
5. Disable the other locomotion components in the avatar if required and add the new component anywhere in the hierarchy.

A good start can be checking out the *UxrSmoothLocomotion* component to see how it works.
