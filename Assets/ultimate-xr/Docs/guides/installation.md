---
title: "Getting started"
---

# Getting started

## Requirements

Unity 2021.2 or higher. LTS version is highly recommended.

## Uninstalling the previous version first

- Before installing a new version of UltimateXR, it is extremely important to delete the old Assets/UltimateXR folder first if it exists. This will make sure that files that were deleted in newer versions aren't preserved. Not doing so may create unwanted compilation errors.
Deleting UltimateXR before installing a newer version will not break any reference link. Unity’s .meta files include IDs to ensure all references are kept correctly.
- If the previous installation came from the Unity Package Manager, and not the Unity Asset Store or a .unitypackage file, this step is not required.

## Installation and setup

We provide the following ways to install **UltimateXR**:

### Method 1: Through the official Unity Asset Store

- Delete the previous version from /Assets/UltimateXR first. **Important!** Not removing the folder before updating may result in incorrect files remaining after installation.
- Go to the [Asset Store](https://assetstore.unity.com/packages/slug/236782) and click Open in Unity to proceed with the installation.
- Remember to rate and review our asset if you liked it!

### Method 2: Downloading and installing the .unitypackage file

UltimateXR can be installed by downloading the latest [.unitypackage]({{< param latest_version_download_url >}}) (version {{< param latest_version >}}).

- Delete the previous version from /Assets/UltimateXR first. **Important!** Not removing the folder before updating may result in incorrect files remaining after installation.
- Download the latest UltimateXR [.unitypackage]({{< param latest_version_download_url >}}) (version {{< param latest_version >}}). It contains a big sample scene so the file size is larger than usual. The core package and sample scene will be split in two different packages in the future to limit disk size and lower installation time.
- Right click on the Project Window and select Import Package -> Custom Package…. 

![ImportPackage](/guides/media/getting-started/01ImportPackage.png)
  
- Select the downloaded file and click Import. Importing assets may take a while due to the included sample scene.

### Method 3: Using the Package Manager for git users

- Open the Package Manager using the Window menu -> Package Manager

![OpenPackageManager](https://user-images.githubusercontent.com/5702147/197527726-7700f742-1055-4a66-8508-cc61ee529c08.jpg)

- Inside the Package Manager, click on the "+" button on the top left and select "Add package from git URL..."

![AddFromGitURL](https://user-images.githubusercontent.com/5702147/197527755-cfc2a87c-980b-4ea3-96ee-34c385f79d86.JPG)

- Input the following URL: https://github.com/VRMADA/ultimatexr-unity.git and click Add.
- When the installation has completed, make sure to import the examples if it's your first time using UltimateXR.

![Examples](https://user-images.githubusercontent.com/5702147/197527794-2f304b4d-0b36-4058-96d1-b7e4d389c0a7.jpg)

### Additional installation methods

We are in the process of providing installation through the Unity Package Manager without requiring git. Stay tuned!

## Setting up the environment

UltimateXR works with any render pipeline (BRP or Built-in Render Pipeline, URP or Universal Render Pipeline, HDRP or High-Definition Render Pipeline) although for the moment it comes with BRP and URP compatible materials.

The example scene is built using URP materials. BRP and HDRP versions will be provided in upcoming versions.
URP is recommended when creating Virtual Reality applications in Unity for the following reasons:

- BRP will be deprecated at some point.
- URP’s goal is to work on all supported platforms, including mobile for untethered XR devices.
- URP is the only viable option for untethered devices, where performance is limited. URP’s performance on these devices is very high.
- URP is very flexible and can also provide high quality graphics in more performant desktop setups.
 
To create a project using the Universal Render Pipeline, create a new project and select the URP template:

![](/guides/media/getting-started/02NewProject.png)

Now go to Edit -> Project settings. Select XR Plugin Management and install the XR Plugin Management package. This will enable the creation of virtual reality applications.

![](/guides/media/getting-started/03XRPluginManagement1.png)
 
After installing the XR Plugin Management you may activate support for some of the devices that have out-of-the-box integration with Unity. Remember to activate them in desktop, which is used both by the editor in play mode and by desktop builds, and also in the destination platform for your builds, which may be different.

![](/guides/media/getting-started/04XRPluginManagement2.png)
 
Your project is now ready to go! You may want to add support for different XR devices next. Luckily UltimateXR takes care of seamlessly integrating with different XR APIs. Go to the Integrations section to check how to add support for your target devices.
