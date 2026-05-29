# 飞书 Webhook 通知 — 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 番茄钟工作/休息结束时通过飞书 Webhook 发送通知消息

**Architecture:** 新建 `FeishuService` 封装飞书 HTTP POST 逻辑，`SettingsConfig` 新增 4 个配置字段，`SettingsWindow` 新增飞书设置 UI 区块，`MainWindow.xaml.cs` 在现有事件回调中调用飞书通知

**Tech Stack:** .NET 8 WPF, `HttpClient`, `System.Text.Json`

---

### Task 1: SettingsConfig 新增飞书字段

**Files:**
- Modify: `src/Taskato/Services/SettingsService.cs:11-38`

- [ ] **Step 1: 在 SettingsConfig 类末尾添加 4 个飞书配置属性**

在 `CustomSoundPath` 属性之后、类的闭合大括号之前添加：

```csharp
/// <summary>是否启用飞书通知</summary>
public bool FeishuEnabled { get; set; } = false;

/// <summary>飞书 Webhook URL（环境变量 FEISHU_WEBHOOK_URL 优先）</summary>
public string FeishuWebhookUrl { get; set; } = string.Empty;

/// <summary>工作完成时发送飞书通知</summary>
public bool FeishuNotifyOnWork { get; set; } = true;

/// <summary>休息完成时发送飞书通知</summary>
public bool FeishuNotifyOnRest { get; set; } = true;
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build src/Taskato/Taskato.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Taskato/Services/SettingsService.cs
git commit -m "feat: SettingsConfig 新增飞书通知配置字段"
```

---

### Task 2: 新建 FeishuService

**Files:**
- Create: `src/Taskato/Services/FeishuService.cs`

- [ ] **Step 1: 创建 FeishuService 类**

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Taskato.Services
{
    /// <summary>
    /// 飞书 Webhook 通知服务 — 通过 HTTP POST 向飞书群机器人发送文本消息
    /// </summary>
    public class FeishuService
    {
        private readonly SettingsService _settings;
        private readonly HttpClient _http;

        public FeishuService(SettingsService settings)
        {
            _settings = settings;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>
        /// 发送飞书 post 格式消息
        /// </summary>
        /// <returns>是否发送成功</returns>
        public async Task<bool> SendAsync(string title, string content)
        {
            // 环境变量优先
            var webhookUrl = Environment.GetEnvironmentVariable("FEISHU_WEBHOOK_URL");
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                webhookUrl = _settings.Config.FeishuWebhookUrl;
            }

            if (string.IsNullOrWhiteSpace(webhookUrl))
                return false;

            try
            {
                var payload = new
                {
                    msg_type = "post",
                    content = new
                    {
                        post = new
                        {
                            zh_cn = new
                            {
                                title = title,
                                content = new[]
                                {
                                    new[]
                                    {
                                        new { tag = "text", text = content }
                                    }
                                }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(webhookUrl, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("code", out var code) && code.GetInt32() == 0)
                        return true;
                }
                return false;
            }
            catch
            {
                // 发送失败静默忽略，不打断用户
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build src/Taskato/Taskato.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Taskato/Services/FeishuService.cs
git commit -m "feat: 新建 FeishuService 飞书通知服务"
```

---

### Task 3: App.xaml.cs 和 MainViewModel 接入 FeishuService

**Files:**
- Modify: `src/Taskato/App.xaml.cs:51-68`
- Modify: `src/Taskato/ViewModels/MainViewModel.cs:22-29,127-148`

- [ ] **Step 1: App.xaml.cs 创建 FeishuService 并传入 MainViewModel**

在 `_settingsService = new SettingsService();` 之后添加：

```csharp
var _feishuService = new FeishuService(_settingsService);
```

修改 MainViewModel 构造调用：

```csharp
var mainVM = new MainViewModel(_dbService, _pomodoroService, _settingsService, _feishuService);
```

- [ ] **Step 2: MainViewModel 接收并暴露 FeishuService**

添加字段和属性（在现有 `_settingsService` 字段之后）：

```csharp
private readonly FeishuService _feishuService;
public FeishuService FeishuService => _feishuService;
```

修改构造函数签名，添加参数：

```csharp
public MainViewModel(DatabaseService dbService, PomodoroService pomodoroService, SettingsService settingsService, FeishuService feishuService)
```

在构造函数体中赋值：

```csharp
_feishuService = feishuService;
```

同时暴露飞书配置给 View 层：

```csharp
public bool FeishuEnabled => _settingsService.Config.FeishuEnabled;
public bool FeishuNotifyOnWork => _settingsService.Config.FeishuNotifyOnWork;
public bool FeishuNotifyOnRest => _settingsService.Config.FeishuNotifyOnRest;
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build src/Taskato/Taskato.csproj
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Taskato/App.xaml.cs src/Taskato/ViewModels/MainViewModel.cs
git commit -m "feat: App 和 MainViewModel 接入 FeishuService"
```

---

### Task 4: MainWindow.xaml.cs 在事件回调中发送飞书通知

**Files:**
- Modify: `src/Taskato/Views/MainWindow.xaml.cs:41-53` (WorkCompleted 回调)
- Modify: `src/Taskato/Views/MainWindow.xaml.cs:55-68` (RestCompleted 回调)
- Modify: `src/Taskato/Views/MainWindow.xaml.cs:76-88` (初始计时器的 WorkCompleted 回调)
- Modify: `src/Taskato/Views/MainWindow.xaml.cs:90-103` (初始计时器的 RestCompleted 回调)

共 4 处回调需要添加飞书通知调用。每处添加模式相同。

- [ ] **Step 1: 在 WorkCompleted 回调中添加飞书通知（新增计时器注册处）**

在 `capturedSubVm.WorkCompleted += () =>` 的 lambda 中，`toast.Show();` 之后添加：

```csharp
_ = Task.Run(async () =>
{
    if (vm.FeishuEnabled && vm.FeishuNotifyOnWork)
        await vm.FeishuService.SendAsync(
            $"{capturedSubVm.TimerName} 时间到！",
            "你已完成专注工作，要休息一下吗？");
});
```

- [ ] **Step 2: 在 RestCompleted 回调中添加飞书通知（新增计时器注册处）**

在 `capturedSubVm.RestCompleted += () =>` 的 lambda 中，`toast.Show();` 之后添加：

```csharp
_ = Task.Run(async () =>
{
    if (vm.FeishuEnabled && vm.FeishuNotifyOnRest)
        await vm.FeishuService.SendAsync(
            $"{capturedSubVm.TimerName} 休息结束！",
            "精力充沛了吗？开始新一轮专注吧！");
});
```

- [ ] **Step 3: 对初始计时器的两个回调做相同修改**

（代码与 Step 1、Step 2 完全一致，只是变量名 `capturedSubVm` 相同。）

- [ ] **Step 4: 添加 `using System.Threading.Tasks;`**

在文件顶部 using 区域添加：

```csharp
using System.Threading.Tasks;
```

- [ ] **Step 5: 编译验证**

```bash
dotnet build src/Taskato/Taskato.csproj
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Taskato/Views/MainWindow.xaml.cs
git commit -m "feat: MainWindow 番茄钟结束回调中接入飞书通知"
```

---

### Task 5: SettingsWindow 新增飞书设置 UI

**Files:**
- Modify: `src/Taskato/Views/SettingsWindow.xaml:265-266` (在提示音区域之后插入)
- Modify: `src/Taskato/Views/SettingsWindow.xaml.cs:74-77` (构造函数加载设置)

- [ ] **Step 1: SettingsWindow.xaml 添加飞书通知 UI 区块**

在提示音区域的 `</Border>`（第 265 行）之后、`</StackPanel>`（第 266 行的 Margin="20" 的 StackPanel 闭合）之前插入：

```xml
<!-- 飞书通知 -->
<TextBlock Text="🔗 飞书通知" FontSize="12" FontWeight="SemiBold"
           Foreground="{StaticResource TextSecondaryBrush}"
           Margin="0,8,0,12"/>
<Border Background="#0AFFFFFF" CornerRadius="10" Padding="16,12" Margin="0,0,0,8">
    <Grid>
        <TextBlock Text="启用飞书通知" FontSize="14"
                   Foreground="#CCCCCC" VerticalAlignment="Center"/>
        <CheckBox x:Name="FeishuEnabledCheckBox"
                  HorizontalAlignment="Right" VerticalAlignment="Center"
                  Checked="FeishuEnabledCheckBox_Changed"
                  Unchecked="FeishuEnabledCheckBox_Changed"/>
    </Grid>
</Border>
<Border Background="#0AFFFFFF" CornerRadius="10" Padding="16,12" Margin="0,0,0,8">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Text="Webhook URL" FontSize="14"
                   Foreground="#CCCCCC" VerticalAlignment="Center"/>
        <TextBox x:Name="FeishuWebhookUrlBox"
                 Grid.Row="1" Margin="0,8,0,0"
                 Foreground="#E0E0E0"
                 CaretBrush="#E0E0E0"
                 TextChanged="FeishuWebhookUrlBox_TextChanged">
            <TextBox.Style>
                <Style TargetType="TextBox">
                    <Setter Property="Background" Value="#15FFFFFF"/>
                    <Setter Property="BorderBrush" Value="#26FFFFFF"/>
                    <Setter Property="BorderThickness" Value="1"/>
                    <Setter Property="Padding" Value="8,6"/>
                </Style>
            </TextBox.Style>
        </TextBox>
    </Grid>
</Border>
<Border Background="#0AFFFFFF" CornerRadius="10" Padding="16,12" Margin="0,0,0,8">
    <Grid>
        <TextBlock Text="工作完成时通知" FontSize="14"
                   Foreground="#CCCCCC" VerticalAlignment="Center"/>
        <CheckBox x:Name="FeishuNotifyWorkCheckBox"
                  HorizontalAlignment="Right" VerticalAlignment="Center"
                  Checked="FeishuNotifyWorkCheckBox_Changed"
                  Unchecked="FeishuNotifyWorkCheckBox_Changed"/>
    </Grid>
</Border>
<Border Background="#0AFFFFFF" CornerRadius="10" Padding="16,12" Margin="0,0,0,24">
    <Grid>
        <TextBlock Text="休息完成时通知" FontSize="14"
                   Foreground="#CCCCCC" VerticalAlignment="Center"/>
        <CheckBox x:Name="FeishuNotifyRestCheckBox"
                  HorizontalAlignment="Right" VerticalAlignment="Center"
                  Checked="FeishuNotifyRestCheckBox_Changed"
                  Unchecked="FeishuNotifyRestCheckBox_Changed"/>
    </Grid>
</Border>
```

- [ ] **Step 2: SettingsWindow.xaml.cs 构造函数中加载飞书配置**

在现有 `MultiPomodoroCheckBox.IsChecked = ...` 之后添加：

```csharp
FeishuEnabledCheckBox.IsChecked = _settingsService.Config.FeishuEnabled;
FeishuWebhookUrlBox.Text = _settingsService.Config.FeishuWebhookUrl;
FeishuNotifyWorkCheckBox.IsChecked = _settingsService.Config.FeishuNotifyOnWork;
FeishuNotifyRestCheckBox.IsChecked = _settingsService.Config.FeishuNotifyOnRest;
```

- [ ] **Step 3: SettingsWindow.xaml.cs 添加事件处理方法**

在类的末尾（`CloseButton_Click` 方法之后、类闭合大括号之前）添加：

```csharp
private void FeishuEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
{
    if (_settingsService == null) return;
    _settingsService.Config.FeishuEnabled = FeishuEnabledCheckBox.IsChecked == true;
    _settingsService.Save();
}

private void FeishuWebhookUrlBox_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_settingsService == null) return;
    _settingsService.Config.FeishuWebhookUrl = FeishuWebhookUrlBox.Text;
    _settingsService.Save();
}

private void FeishuNotifyWorkCheckBox_Changed(object sender, RoutedEventArgs e)
{
    if (_settingsService == null) return;
    _settingsService.Config.FeishuNotifyOnWork = FeishuNotifyWorkCheckBox.IsChecked == true;
    _settingsService.Save();
}

private void FeishuNotifyRestCheckBox_Changed(object sender, RoutedEventArgs e)
{
    if (_settingsService == null) return;
    _settingsService.Config.FeishuNotifyOnRest = FeishuNotifyRestCheckBox.IsChecked == true;
    _settingsService.Save();
}
```

- [ ] **Step 4: 编译验证**

```bash
dotnet build src/Taskato/Taskato.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Taskato/Views/SettingsWindow.xaml src/Taskato/Views/SettingsWindow.xaml.cs
git commit -m "feat: SettingsWindow 新增飞书通知设置 UI"
```

---

### Task 6: 运行冒烟测试

- [ ] **Step 1: 启动应用**

```bash
dotnet run --project src/Taskato/Taskato.csproj
```

- [ ] **Step 2: 验证点**

1. 主窗口正常显示
2. 打开设置窗口，滚动到底部，确认"飞书通知"区块出现
3. 勾选"启用飞书通知"，粘贴一个飞书 Webhook URL
4. 关闭设置，启动一个 1 分钟的工作番茄钟
5. 等待工作结束，检查飞书群是否收到消息
6. 休息结束后也检查飞书群是否收到消息

Expected: 飞书群收到文本消息，标题包含计时器名称。

- [ ] **Step 3: Commit (如有微调)**

```bash
git add -A
git commit -m "chore: 冒烟测试通过，飞书通知功能完成"
```
