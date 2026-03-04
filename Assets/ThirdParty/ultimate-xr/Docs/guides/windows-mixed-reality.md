---
title: "Windows Mixed Reality"
---

# Windows Mixed Reality

## Installing and setting up SteamVR support

Open Edit -> Project Settings and enable Open XR support. You may get a warning to activate the new input system. Select Yes.

![](/guides/media/supported-platforms/WMR01OpenXR.png)

After enabling Open XR support go to the OpenXR settings and add the Microsoft Motion Controller Profile.

![](/guides/media/supported-platforms/WMR02AddProfile.png)

You will now have support for the standard Microsoft Motion Controllers.

## Adding support for HP Reverb G2

To add support for the HP Reverb G2 headset you need to install the Mixed Reality Feature tool: https://aka.ms/MRFeatureTool.

Unzip the downloaded file to a folder and run the MixedRealityFeatureTool.exe.

Click Start.

![](/guides/media/supported-platforms/WMR03ClickStart.png)
 
Choose the project folder and click Discover Features.

![](/guides/media/supported-platforms/WMR04SetupProjectFolder.png)
 
Only select the Mixed Reality Open_XR Plugin and click Get Features. The content will be downloaded.

![](/guides/media/supported-platforms/WMR05OpenXRPlugin.png)
 
Click Import, then Approve and Exit.

Back in Unity you should now be able to install the HP Reverb G2 Controller Profile.

![](/guides/media/supported-platforms/WMR06NewProfile.png)
