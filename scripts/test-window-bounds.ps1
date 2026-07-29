param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
$fullExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $fullExecutablePath)) {
    throw "窗口探针找不到可执行文件：$fullExecutablePath"
}

$nativeSource = @'
using System;
using System.Runtime.InteropServices;
public static class TaskatoWindowProbe
{
    public const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int Size;
        public RECT MonitorArea;
        public RECT WorkArea;
        public uint Flags;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out RECT value,
        int valueSize);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MONITORINFO monitorInfo);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr windowHandle);
}
'@

Add-Type -TypeDefinition $nativeSource

$probeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "Taskato-Window-Probe-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
$previousValues = @{
    TASKATO_ALLOW_MULTIPLE_INSTANCES = $env:TASKATO_ALLOW_MULTIPLE_INSTANCES
    TASKATO_SKIP_FIRST_RUN_GUIDE = $env:TASKATO_SKIP_FIRST_RUN_GUIDE
    TASKATO_DATA_DIRECTORY = $env:TASKATO_DATA_DIRECTORY
}
$process = $null

try {
    $env:TASKATO_ALLOW_MULTIPLE_INSTANCES = "1"
    $env:TASKATO_SKIP_FIRST_RUN_GUIDE = "1"
    $env:TASKATO_DATA_DIRECTORY = $probeRoot
    $process = Start-Process -FilePath $fullExecutablePath -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        throw "窗口探针未取得主窗口句柄。"
    }

    $normal = New-Object TaskatoWindowProbe+RECT
    # DWM 扩展边界固定返回物理像素，不受探针进程自身 DPI 感知级别影响。
    $dwmResult = [TaskatoWindowProbe]::DwmGetWindowAttribute(
        $process.MainWindowHandle,
        9,
        [ref]$normal,
        [Runtime.InteropServices.Marshal]::SizeOf($normal))
    if ($dwmResult -ne 0) {
        throw "窗口探针无法读取 DWM 窗口边界，错误码：$dwmResult。"
    }
    $monitorHandle = [TaskatoWindowProbe]::MonitorFromWindow(
        $process.MainWindowHandle,
        [TaskatoWindowProbe]::MonitorDefaultToNearest)
    $monitorInfo = New-Object TaskatoWindowProbe+MONITORINFO
    $monitorInfo.Size = [Runtime.InteropServices.Marshal]::SizeOf($monitorInfo)
    if (-not [TaskatoWindowProbe]::GetMonitorInfo($monitorHandle, [ref]$monitorInfo)) {
        throw "窗口探针无法读取显示器工作区。"
    }
    # PowerShell 宿主会把 MonitorInfo 虚拟化为逻辑坐标，而目标 WPF 窗口矩形是物理像素。
    # 使用目标窗口 DPI 把工作区换算到同一坐标系，避免 150% 缩放下误报偏移。
    $dpiScale = [TaskatoWindowProbe]::GetDpiForWindow($process.MainWindowHandle) / 96.0
    $workArea = $monitorInfo.WorkArea
    $workArea.Left = [int][Math]::Round($workArea.Left * $dpiScale)
    $workArea.Top = [int][Math]::Round($workArea.Top * $dpiScale)
    $workArea.Right = [int][Math]::Round($workArea.Right * $dpiScale)
    $workArea.Bottom = [int][Math]::Round($workArea.Bottom * $dpiScale)
    $normalCenterX = ($normal.Left + $normal.Right) / 2
    $normalCenterY = ($normal.Top + $normal.Bottom) / 2
    $workWidth = $workArea.Right - $workArea.Left
    $workHeight = $workArea.Bottom - $workArea.Top
    $workCenterX = $workArea.Left + $workWidth / 2
    $workCenterY = $workArea.Top + $workHeight / 2
    # DWM 扩展边界可能排除最多数像素的不可见调整边框，8 像素内视为视觉居中。
    if ([Math]::Abs($normalCenterX - $workCenterX) -gt 8 -or
        [Math]::Abs($normalCenterY - $workCenterY) -gt 8) {
        throw "启动窗口未居中：窗口中心=($normalCenterX,$normalCenterY)，工作区中心=($workCenterX,$workCenterY)。"
    }

    # 3 是 Win32 SW_MAXIMIZE；探针只操作使用隔离数据目录启动的测试进程。
    [TaskatoWindowProbe]::ShowWindow($process.MainWindowHandle, 3) | Out-Null
    Start-Sleep -Milliseconds 500
    $maximized = New-Object TaskatoWindowProbe+RECT
    $dwmResult = [TaskatoWindowProbe]::DwmGetWindowAttribute(
        $process.MainWindowHandle,
        9,
        [ref]$maximized,
        [Runtime.InteropServices.Marshal]::SizeOf($maximized))
    if ($dwmResult -ne 0) {
        throw "窗口探针无法读取最大化后的 DWM 边界，错误码：$dwmResult。"
    }
    $leftGap = $maximized.Left - $workArea.Left
    $topGap = $maximized.Top - $workArea.Top
    $rightGap = $workArea.Right - $maximized.Right
    $bottomGap = $workArea.Bottom - $maximized.Bottom
    # DWM 会从最大化窗口可见边界中排除约 10 像素的调整边框；关键约束是不能越过工作区覆盖任务栏。
    if ($leftGap -lt 0 -or $topGap -lt 0 -or $rightGap -lt 0 -or $bottomGap -lt 0 -or
        $leftGap -gt 16 -or $topGap -gt 16 -or $rightGap -gt 16 -or $bottomGap -gt 16) {
        throw "最大化边界未匹配工作区：窗口=[$($maximized.Left),$($maximized.Top),$($maximized.Right),$($maximized.Bottom)]，工作区=[$($workArea.Left),$($workArea.Top),$($workArea.Right),$($workArea.Bottom)]。"
    }

    Write-Host "PASS: 启动窗口中心与工作区中心一致；最大化可见边界未越过工作区，任务栏已避让。"
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    foreach ($name in $previousValues.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousValues[$name], "Process")
    }

    $resolvedProbeRoot = [System.IO.Path]::GetFullPath($probeRoot)
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedProbeRoot.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedProbeRoot)) {
        $cleanupError = $null
        for ($attempt = 1; $attempt -le 40; $attempt++) {
            try {
                Remove-Item -LiteralPath $resolvedProbeRoot -Recurse -Force
                $cleanupError = $null
                break
            }
            catch {
                $cleanupError = $_
                Start-Sleep -Milliseconds 250
            }
        }
        if ($null -ne $cleanupError) {
            throw "窗口探针结束后无法清理隔离目录：$resolvedProbeRoot。$($cleanupError.Exception.Message)"
        }
    }
}
