param([string]$Name)
# Bring one window forward; FM27 runs fullscreen and steals focus, so it is minimized unless it is the target.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Fw { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e); }
"@
if ($Name -ne 'fm') { $fm = Get-Process fm -ErrorAction SilentlyContinue | Select -First 1; if ($fm) { [Fw]::ShowWindow($fm.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 400 } }
$p = Get-Process $Name | Where-Object MainWindowHandle -ne 0 | Sort StartTime | Select -First 1
[Fw]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [Fw]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
[Fw]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
[Fw]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400
"foreground ok: $([Fw]::GetForegroundWindow() -eq $p.MainWindowHandle)"
