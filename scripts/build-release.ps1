param(
    [string]$Version = "2.20.0",
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot "src\Taskato.Web"
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\release"))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
$publishRoot = Join-Path $artifactsRoot "portable"

# 每次发布必须从空目录开始，避免旧 DLL、浏览器缓存或失败轮次文件混入新的 ZIP。
$releasePrefix = $releaseRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $artifactsRoot.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝清理发布根目录之外的路径：$artifactsRoot"
}
if (Test-Path -LiteralPath $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

Push-Location $webRoot
try {
    npm ci
    npm run check
    npm test
    npm run build
}
finally {
    Pop-Location
}

dotnet build (Join-Path $repositoryRoot "Taskato.sln") -c Release
dotnet run --project (Join-Path $repositoryRoot "src\TestProject\TestProject.csproj") -c Release
dotnet publish (Join-Path $repositoryRoot "src\Taskato\Taskato.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -o $publishRoot

$executable = Join-Path $publishRoot "Taskato.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "发布产物缺少 Taskato.exe。"
}
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot "Web\index.html"))) {
    throw "发布产物缺少 WebView2 前端资源。"
}

& (Join-Path $repositoryRoot "scripts\test-webview-smoke.ps1") -ExecutablePath $executable

$zipPath = Join-Path $artifactsRoot "Taskato-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $zipPath -Force
$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zipPath.sha256" -Value "$checksum  $(Split-Path -Leaf $zipPath)" -Encoding ascii

$manifest = [ordered]@{
    version = $Version
    createdAt = [DateTimeOffset]::Now.ToString("O")
    runtime = ".NET 8 self-contained (win-x64)"
    webView2 = "Evergreen Runtime; installer downloads it when missing"
    portableArchive = (Split-Path -Leaf $zipPath)
    sha256 = $checksum
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot "release-manifest.json") -Encoding utf8

if ($BuildInstaller) {
    $compiler = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
    if (-not $compiler) {
        throw "未找到 Inno Setup 编译器 iscc.exe；便携包已经生成，可安装后重新执行 -BuildInstaller。"
    }
    & $compiler "/DMyAppVersion=$Version" "/DPublishDir=$publishRoot" `
        (Join-Path $repositoryRoot "installer\Taskato.iss")
}

Write-Host "Taskato $Version 发布验证完成：$artifactsRoot"
