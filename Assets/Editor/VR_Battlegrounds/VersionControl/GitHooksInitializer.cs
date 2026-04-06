#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Diagnostics;

[InitializeOnLoad]
public class GitHooksInitializer
{
    static GitHooksInitializer()
    {
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = "config core.hooksPath .githooks";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit();
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning("Failed to set git config core.hooksPath automatically. " + e.Message);
        }
    }
}
#endif
