# HistoryWindow 图标优化方案 (2026-04-29)

当前 `HistoryWindow.xaml` 使用的是 Emoji 图标（📋、🔍、📥），这在不同系统下的渲染效果不一，且缺乏现代 WPF 应用的专业质感。

## 1. 核心建议
- **使用 Path 矢量数据**：代替 Emoji，确保在任何缩放比例下都清晰锐利，且颜色可随主题动态变化。
- **标准化尺寸**：将标题栏图标固定为 16x16 视觉大小，按钮内图标与文字保持 8px 间距。
- **资源化管理**：将常用的矢量 Path 数据定义在 `App.xaml` 或独立的资源字典中，便于全局复用。

## 2. 推荐图标 Path 数据
我为你挑选了三个符合现代简约风格的图标：

| 功能 | 图标描述 | Path Data (示例) |
| :--- | :--- | :--- |
| **搜索** | 细线条放大镜 | `M15.5,14h-0.79l-0.28-0.27C15.41,12.59,16,11.11,16,9.5C16,5.91,13.09,3,9.5,3S3,5.91,3,9.5S5.91,16,9.5,16c1.61,0,3.09-0.59,4.23-1.57l0.27,0.28v0.79l5,4.99L20.49,19L15.5,14z M9.5,14C7.01,14,5,11.99,5,9.5S7.01,5,9.5,5S14,7.01,14,9.5S11.99,14,9.5,14z` |
| **历史/列表** | 极简剪贴板 | `M18,2h-3.18C14.4,0.84,13.3,0,12,0S9.6,0.84,9.18,2H6C4.9,2,4,2.9,4,4v16c0,1.1,0.9,2,2,2h12c1.1,0,2-0.9,2-2V4C20,2.9,19.1,2,18,2z M12,2c0.55,0,1,0.45,1,1s-0.45,1-1,1s-1-0.45-1-1S11.45,2,12,2z M18,20H6V4h2v3h8V4h2V20z` |
| **导出** | 托盘箭头向下 | `M19,9h-4V3H9v6H5l7,7L19,9z M5,18v2h14v-2H5z` |

## 3. 执行步骤

### 第一步：在 App.xaml 定义静态资源
为了保持代码整洁，我们先在 `App.xaml` 中添加这些 Path 数据。

### 第二步：修改 HistoryWindow.xaml
1. **标题栏图标**：将 `TextBlock` 替换为 `Path`。
2. **搜索按钮**：将 `Content="🔍 搜索"` 拆分为 `StackPanel`，包含 `Path` 和 `TextBlock`。
3. **导出按钮**：同上。

## 4. 预览效果示意 (代码结构)
```xml
<Button Style="{StaticResource PrimaryButton}" ...>
    <StackPanel Orientation="Horizontal">
        <Path Data="{StaticResource IconSearch}" Fill="White" Stretch="Uniform" Width="14" Height="14" Margin="0,0,8,0"/>
        <TextBlock Text="搜索" VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

**是否确认执行该优化方案？确认后我将为你修改 `App.xaml` 和 `HistoryWindow.xaml`。**
