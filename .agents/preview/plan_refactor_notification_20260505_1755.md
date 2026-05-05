# 桌面通知技能 (desktop-notification) 模块化重构计划

## 1. 现状分析
目前 `desktop-notification` 技能的文件分布较为零散：
- 技能定义：`.agents/skills/skills/desktop-notification/SKILL.md`
- 配置文件：`.agents/notification_config.txt`
- 执行脚本：`.agents/scripts/notify.ps1`

这种布局导致技能不具备“自包含性 (Self-contained)”，在迁移或维护时容易遗漏相关依赖。

## 2. 目标方案
将所有相关依赖移入技能文件夹内，形成标准的技能目录结构：
```text
desktop-notification/
├── SKILL.md            # 技能说明文档
├── resources/          # 静态资源与配置
│   └── notification_config.txt
└── scripts/            # 实现脚本
    └── notify.ps1
```

## 3. 执行步骤
1.  **创建目录结构**：[已完成]
2.  **迁移文件**：[已完成]
3.  **更新技能文档 (SKILL.md)**：[已完成]
4.  **清理旧文件**：[已完成]

## 4. 验证
- 检查 `SKILL.md` 中的路径是否正确：[通过]
- 发送测试通知确保迁移后功能正常：[通过]

---
**状态：已完成 (2026-05-05 18:01)**
