# 番茄钟加减按钮“果冻胶囊”复刻计划

本计划旨在将 HTML 预览中的“方案 1”视觉效果完美迁移至 WPF 项目中，提升操作的物理反馈感。

## 1. 核心视觉规范
- **尺寸**: 32x32 像素
- **圆角 (CornerRadius)**: 10
- **默认态**: 背景 15% 主题色透明度，前景色为主题色实色。
- **悬停态**: 背景变为主题色实色，前景色变为白色，增加 5 像素扩展的柔和阴影，缩放 1.15 倍并旋转 5 度。
- **点击态**: 缩放降至 0.9 倍。

## 2. 技术路线
1. **重构全局样式**: 在 `App.xaml` 中新增 `JellyIconBtn` 样式。
2. **利用 Storyboard**: 弃用简单的 `Trigger.Setters`，改用 `ControlTemplate.Triggers` 配合 `Storyboard` 实现 0.25s 的平滑过渡。
3. **坐标变换**: 使用 `RenderTransform` 的 `TransformGroup` 同时控制比例 (Scale) 和旋转 (Rotate)。

## 3. 待修改内容预览

### [MODIFY] [App.xaml](file:///d:/Work/My/LearningMaterials/DotNET/WPF/code/Taskato/src/Taskato/App.xaml)
- 添加 `JellyIconBtn` 资源定义。

### [MODIFY] [MainWindow.xaml](file:///d:/Work/My/LearningMaterials/DotNET/WPF/code/Taskato/src/Taskato/Views/MainWindow.xaml)
- 将加减按钮的 `Style` 引用更新为 `JellyIconBtn`。

---
## 4. 验证方案
### 视觉验证
- [ ] 检查鼠标移入时是否有明显的弹出回弹感 (Elastic Feel)。
- [ ] 检查阴影颜色是否与当前主题色保持一致。
- [ ] 检查按钮在 25:00 数字两侧的对齐是否美观。

### 性能验证
- [ ] 连续快速点击/滑过，检查是否存在 UI 线程阻塞。
