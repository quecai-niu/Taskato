Add-Type -AssemblyName System.Windows.Forms
# 使用用户选定的 Background 音效（低沉厚重，沉浸感强）
$soundPath = "C:\Windows\Media\Alarm01.wav"
if (Test-Path $soundPath) {
    $player = New-Object System.Media.SoundPlayer($soundPath)
    $player.Play()
} else {
    [System.Media.SystemSounds]::Exclamation.Play()
}
$form = New-Object System.Windows.Forms.Form
$form.TopMost = $true
[System.Windows.Forms.MessageBox]::Show($form, '您的 AI 助手已完成当前周期的任务执行，请查阅！', 'Taskato Notification', 0, 64)
