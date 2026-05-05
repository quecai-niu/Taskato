Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.TopMost = $true
[System.Windows.Forms.MessageBox]::Show($form, '您的 AI 助手已完成当前周期的任务执行，请查阅！', 'Taskato Notification', 0, 64)
