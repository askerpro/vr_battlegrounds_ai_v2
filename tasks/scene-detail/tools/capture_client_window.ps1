# Скриншот окна E2E-клиента (VrBattlegrounds.exe с -e2eRole client-1) без перехвата фокуса: PrintWindow
# с PW_RENDERFULLCONTENT читает содержимое DirectX-окна даже под другими окнами. Для визуального сравнения
# вариантов render_diag; это не замер.
param(
    [Parameter(Mandatory = $true)][string] $Out,
    [int] $WaitSeconds = 0
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32Capture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
}
'@
if ($WaitSeconds -gt 0) { Start-Sleep -Seconds $WaitSeconds }
$client = Get-CimInstance Win32_Process -Filter "Name='VrBattlegrounds.exe'" |
    Where-Object { $_.CommandLine -match 'client-1' } | Select-Object -First 1
if ($null -eq $client) { throw 'клиент VrBattlegrounds (client-1) не найден' }
$proc = Get-Process -Id $client.ProcessId
if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw 'у клиента нет окна' }
$rect = New-Object Win32Capture+RECT
[void][Win32Capture]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Win32Capture]::PrintWindow($proc.MainWindowHandle, $hdc, 2)
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Output ("{0} {1}x{2} printWindow={3}" -f $Out, $w, $h, $ok)
