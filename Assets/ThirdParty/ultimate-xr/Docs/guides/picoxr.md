---
title: "PicoXR"
---

# PicoXR

## Installing and setting up PicoXR support

Switch to Android using File -> Buid Settings.

![](/guides/media/supported-platforms/PicoXR01Android.png)
 
Open the UltimateXR SDK Manager using the menu Tools -> UltimateXR -> SDK Manager. Click Get SDK under the Pico XR section. This will open the link to install the PicoXR support.

![](/guides/media/supported-platforms/PicoXR02SDKManager.png)
 
Download and unzip the package to a folder. If you are using versioning software you may want to unzip the package to a folder under version control, while keeping it outside of the project’s Assets folder. This will make sure the package is available to all users.

Go to the Unity Package Manager using the menu Window -> Package Manager. Click on the + and select Add package from disk…

![](/guides/media/supported-platforms/PicoXR03AddPackage.png)
 
Navigate to the unzipped folder and select the package.json to install the Pico XR support.

Follow [these instructions](https://developer-global.pico-interactive.com/document/unity/create-a-developer-account-organization-and-app) to create a developer account and create an app ID for your application.

Go to Edit -> Project Settings and enable PicoXR support for both the desktop and Android platforms.
 
![](/guides/media/supported-platforms/PicoXR04AddSupport.png)

Enable Multiview under the Android PicoXR settings

![](/guides/media/supported-platforms/PicoXR05Multiview.png)
 
Your application should now be compatible with the Pico headset and controllers.
To be able to use Play Mode to preview the scene in Unity, and not just create Pico-compatible builds, you will need to install the Pico Developer Center (PDC).
To install the PDC follow [these instructions](https://developer-global.pico-interactive.com/document/unity/pdc-basic-info).
