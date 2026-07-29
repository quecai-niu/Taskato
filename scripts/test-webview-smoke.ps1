param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$fullExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $fullExecutablePath)) {
    throw "WebView2 烟测找不到可执行文件：$fullExecutablePath"
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "Taskato-WebView-Smoke-$([Guid]::NewGuid().ToString('N'))"
$dataDirectory = Join-Path $smokeRoot "Data"
$webViewDataDirectory = Join-Path $smokeRoot "WebView2"
$resultPath = Join-Path $smokeRoot "result.json"
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

$previousValues = @{
    TASKATO_ALLOW_MULTIPLE_INSTANCES = $env:TASKATO_ALLOW_MULTIPLE_INSTANCES
    TASKATO_SKIP_FIRST_RUN_GUIDE = $env:TASKATO_SKIP_FIRST_RUN_GUIDE
    TASKATO_DATA_DIRECTORY = $env:TASKATO_DATA_DIRECTORY
    TASKATO_E2E_SMOKE = $env:TASKATO_E2E_SMOKE
    TASKATO_E2E_RESULT_PATH = $env:TASKATO_E2E_RESULT_PATH
    TASKATO_WEBVIEW_USER_DATA_DIRECTORY = $env:TASKATO_WEBVIEW_USER_DATA_DIRECTORY
}

try {
    $env:TASKATO_ALLOW_MULTIPLE_INSTANCES = "1"
    $env:TASKATO_SKIP_FIRST_RUN_GUIDE = "1"
    $env:TASKATO_DATA_DIRECTORY = $dataDirectory
    $env:TASKATO_E2E_SMOKE = "1"
    $env:TASKATO_E2E_RESULT_PATH = $resultPath
    $env:TASKATO_WEBVIEW_USER_DATA_DIRECTORY = $webViewDataDirectory
    $process = Start-Process -FilePath $fullExecutablePath -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }
    if (-not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "WebView2 烟测在 $TimeoutSeconds 秒内未结束。"
    }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "WebView2 烟测未生成结果文件，进程退出码：$($process.ExitCode)。"
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (-not $result.passed -or $process.ExitCode -ne 0) {
        throw "WebView2 烟测失败：$($result.error)；退出码：$($process.ExitCode)。"
    }
    Write-Host "PASS: Release WebView2、React 根节点与本地桥接自动烟测通过。"
}
finally {
    foreach ($name in $previousValues.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousValues[$name], "Process")
    }
    # WebView2 与 SQLite 在窗口关闭后可能短暂持有文件句柄；有限重试避免把已通过的烟测误判为清理失败。
    # 目录名含随机 GUID，且始终位于系统临时目录；只清理本次烟测产生的隔离数据。
    if (Test-Path -LiteralPath $smokeRoot) {
        $cleanupError = $null
        for ($attempt = 1; $attempt -le 40; $attempt++) {
            try {
                Remove-Item -LiteralPath $smokeRoot -Recurse -Force
                $cleanupError = $null
                break
            }
            catch {
                $cleanupError = $_
                Start-Sleep -Milliseconds 250
            }
        }
        if ($null -ne $cleanupError) {
            throw "WebView2 烟测已结束，但无法清理隔离目录：$smokeRoot。$($cleanupError.Exception.Message)"
        }
    }
}
