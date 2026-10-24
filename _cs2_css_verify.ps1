$ErrorActionPreference = 'Continue'
$g = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo'
$css = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\counterstrikesharp.log'

Get-Process cs2 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 6
$before = (Get-Item $css).LastWriteTime
Write-Host "BEFORE lastwrite=$($before.ToString('HH:mm:ss'))"

Start-Process 'C:\Program Files (x86)\Steam\Steam.exe' -ArgumentList '-applaunch','730','-insecure','-console','+map','de_dust2'

$sig = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class DLG {
    public delegate bool Proc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Proc f, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    public struct RECT { public int L; public int T; public int R; public int B; }
    public static IntPtr Found = IntPtr.Zero;
    public static bool Cb(IntPtr h, IntPtr l) {
        if (!IsWindowVisible(h)) return true;
        var cn = new StringBuilder(64); GetClassNameW(h, cn, 64);
        if (cn.ToString() != "#32770") return true;
        RECT r; GetWindowRect(h, out r);
        int w = r.R-r.L, ht = r.B-r.T;
        if (w > 300 && w < 600 && ht > 200 && ht < 500) { Found = h; return false; }
        return true;
    }
    public static bool Confirm() {
        Found = IntPtr.Zero;
        EnumWindows(Cb, IntPtr.Zero);
        if (Found == IntPtr.Zero) return false;
        PostMessage(Found, 0x0111, (IntPtr)1, IntPtr.Zero);
        return true;
    }
}
'@
Add-Type -TypeDefinition $sig

$confirmed = 0
$loaded = $false
for ($i = 1; $i -le 30; $i++) {
  Start-Sleep -Seconds 5
  if ([DLG]::Confirm()) { $confirmed++; Write-Host "iter$i DLG-CONFIRMED" }
  $t = (Get-Item $css).LastWriteTime
  if ($t -gt $before) { Write-Host "iter$i CSS-LOADED $($t.ToString('HH:mm:ss'))"; $loaded = $true; break }
}
Write-Host "DLG_CONFIRM_COUNT=$confirmed"
Write-Host "CSS_LOADED=$loaded"

$p = Get-Process cs2 -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1
if (-not $p) { Write-Host "CS2_NOT_STARTED"; exit }
$pid1 = $p.Id
Write-Host "MONITOR_PID=$pid1"
$survived = 0
for ($i = 1; $i -le 14; $i++) {
  Start-Sleep -Seconds 5
  $alive = Get-Process -Id $pid1 -ErrorAction SilentlyContinue
  if (-not $alive) { Write-Host "iter${i}: CS2-EXITED-CRASH (after ~$($i*5)s)"; break }
  $mem = [math]::Round($alive.WorkingSet64/1MB)
  $cpu = [math]::Round($alive.TotalProcessorTime.TotalSeconds,0)
  Write-Host "iter${i}: ALIVE mem=${mem}MB cpu=${cpu}s"
  $survived = $i * 5
}
Write-Host "MONITORED_SECONDS=$survived"

$log = Get-ChildItem "$g\addons\counterstrikesharp\logs" -File -Filter 'log-all*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "LOG=$($log.FullName)"
Write-Host "=== KNIFE ==="
Select-String -Path $log.FullName -Pattern 'PlayerKnifeCustomizer' | Select-Object -Last 3 | ForEach-Object { $_.Line.Trim() }
Write-Host "=== MAP ==="
Select-String -Path $log.FullName -Pattern 'Map start' | Select-Object -Last 2 | ForEach-Object { $_.Line.Trim() }
Write-Host "=== ERRORS_TAIL ==="
Select-String -Path $log.FullName -Pattern '\[EROR\]' | Select-Object -Last 3 | ForEach-Object { $_.Line.Trim() }
Write-Host "=== FINAL ==="
$p = Get-Process cs2 -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1
if ($p) { "ALIVE pid=$($p.Id) cpu=$([math]::Round($p.TotalProcessorTime.TotalSeconds,0))s mem=$([math]::Round($p.WorkingSet64/1MB))MB" } else { "EXITED" }
