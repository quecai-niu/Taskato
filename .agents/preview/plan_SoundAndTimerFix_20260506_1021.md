# 子番茄钟隔离 + 提示音多选 + 播放稳定性修复

## 待办事项

- [ ] **[Fix]** 修复子计时器弹窗回调 — 用子 VM 自身的 StartRest，不影响主番茄钟
- [ ] **[Fix]** 修复 SoundPlayer 被 GC 回收导致的随机播放失败
- [ ] **[Feature]** 在 SettingsConfig 中增加 SoundChoice 枚举字段
- [ ] **[Feature]** 在 SettingsWindow 中增加声音选择 UI（5种音效）
- [ ] **[Feature]** ToastWindow 根据配置播放对应音效

## 方案细节

### 1. 子计时器弹窗隔离 (MainWindow.xaml.cs)
- 当前问题：子计时器完成时弹窗的 onRest 回调调用的是主 vm.StartRest()，导致主番茄钟被重置
- 修复：AdditionalTimers 的 WorkCompleted 事件回调中，传入子 VM 自身的 StartRest() 方法

### 2. SoundPlayer GC 问题 (ToastWindow.xaml.cs)
- 当前问题：Play() 是异步方法，对象在播放完成前被 GC 回收
- 修复：改用 PlaySync() 在独立线程调用，或用 static 持有对象

### 3. 声音选择配置
- SettingsConfig 新增 SoundChoice 枚举 (0-4)
- 5种选项：无声、Notify、Ding、Background、Chimes
- SettingsWindow 新增单选组

## 验证计划
- 启动子计时器，完成后点击"休息一下"，确认主番茄钟不受影响
- 连续触发3次弹窗，确认声音100%播放
- 在设置中切换声音，重新触发弹窗确认生效
