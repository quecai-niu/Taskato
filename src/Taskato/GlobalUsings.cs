// 全局 using 声明 — 解决 WPF + WinForms 同时启用时的命名空间冲突
// 因为项目同时引用了 WPF 和 WinForms（为了系统托盘 NotifyIcon），
// 很多类名在两个框架中都存在（如 Application, Color, Brushes 等），
// 需要在这里明确指定默认使用 WPF 的版本。

global using Application = System.Windows.Application;
global using Color = System.Windows.Media.Color;
global using Brushes = System.Windows.Media.Brushes;
global using MessageBox = System.Windows.MessageBox;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Cursors = System.Windows.Input.Cursors;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;

