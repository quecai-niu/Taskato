Add-Type -AssemblyName System.Windows.Forms
$files = @(
    "C:\Windows\Media\Windows Notify.wav",
    "C:\Windows\Media\Windows Ding.wav",
    "C:\Windows\Media\chimes.wav",
    "C:\Windows\Media\Windows Background.wav"
)
foreach ($f in $files) {
    Write-Host "正在播放: $f"
    $player = New-Object System.Media.SoundPlayer($f)
    $player.PlaySync()
    Start-Sleep -Milliseconds 800
}
Write-Host "全部播放完毕"
