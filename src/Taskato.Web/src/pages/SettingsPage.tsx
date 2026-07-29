import { useEffect, useState } from "react";
import {
  Bell,
  CalendarBlank,
  GearSix,
  Sparkle,
  SpeakerHigh,
  Timer,
} from "@phosphor-icons/react";
import { bridge, type SettingsDto } from "../bridge";
import { applyTheme } from "../ui";

function Toggle({ value, onChange, label }: { value: boolean; onChange(value: boolean): void; label: string }) {
  return <button className={`switch ${value ? "on" : ""}`} onClick={() => onChange(!value)} aria-label={label} aria-pressed={value}><span /></button>;
}

const palettes = [
  ["#6366F1", "#A855F7"],
  ["#EC4899", "#F43F5E"],
  ["#14B8A6", "#06B6D4"],
  ["#F59E0B", "#F97316"],
  ["#22C55E", "#10B981"],
  ["#3B82F6", "#2563EB"],
] as const;

type Section = "theme" | "pomodoro" | "system" | "summary" | "sound" | "feishu" | "data";
const sections: Array<{ id: Section; label: string; icon: typeof Sparkle }> = [
  { id: "theme", label: "主题颜色", icon: Sparkle },
  { id: "pomodoro", label: "番茄钟时长", icon: Timer },
  { id: "system", label: "系统", icon: GearSix },
  { id: "summary", label: "每日总结", icon: CalendarBlank },
  { id: "sound", label: "提示音", icon: SpeakerHigh },
  { id: "feishu", label: "飞书通知", icon: Bell },
  { id: "data", label: "数据与兼容", icon: GearSix },
];

export function SettingsPage({ reportError }: { reportError(error: unknown): void }) {
  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [savedSettings, setSavedSettings] = useState<SettingsDto | null>(null);
  const [active, setActive] = useState<Section>("theme");
  const [saveStatus, setSaveStatus] = useState<"clean" | "dirty" | "saving" | "saved" | "error">("clean");
  const [dataStatus, setDataStatus] = useState("");
  const [feishuHelp, setFeishuHelp] = useState(false);
  const [feishuTestStatus, setFeishuTestStatus] = useState("");

  useEffect(() => {
    void bridge.request<SettingsDto>("settings.get").then((value) => {
      setSettings(value);
      setSavedSettings(value);
    }).catch(reportError);
  }, [reportError]);

  if (!settings) return <div className="full-page"><p>正在加载设置…</p></div>;

  const patch = <K extends keyof SettingsDto>(key: K, value: SettingsDto[K]) => {
    setSettings({ ...settings, [key]: value });
    setSaveStatus("dirty");
  };
  const selectPalette = (index: number) => {
    const [primary, secondary] = palettes[index];
    const next = { ...settings, selectedColorIndex: index, themePrimaryColor: primary, themeSecondaryColor: secondary };
    setSettings(next);
    applyTheme(next);
    setSaveStatus("dirty");
  };
  const save = async () => {
    if (settings.workMinutes < 1 || settings.workMinutes > 240 || settings.restMinutes < 1 || settings.restMinutes > 30) {
      setSaveStatus("error");
      reportError(new Error("工作时长应为 1–240 分钟，休息时长应为 1–30 分钟。"));
      return;
    }
    try {
      setSaveStatus("saving");
      const result = await bridge.request<SettingsDto>("settings.save", settings as unknown as Record<string, unknown>);
      setSettings(result);
      setSavedSettings(result);
      applyTheme(result);
      setSaveStatus("saved");
    } catch (error) {
      setSaveStatus("error");
      reportError(error);
    }
  };
  const navigate = (section: Section) => {
    setActive(section);
    requestAnimationFrame(() => document.getElementById(`settings-${section}`)?.scrollIntoView({ behavior: "smooth", block: "start" }));
  };
  const chooseSound = async () => {
    try {
      const result = await bridge.request<{ selected: boolean; path: string }>("settings.chooseSound");
      if (result.selected) {
        setSettings({ ...settings, notificationSoundChoice: 5, customSoundPath: result.path });
        setSaveStatus("dirty");
      }
    } catch (error) { reportError(error); }
  };
  const testFeishu = async () => {
    setFeishuTestStatus("正在发送…");
    try {
      const result = await bridge.request<{ sent: boolean }>("settings.testFeishu", { webhookUrl: settings.feishuWebhookUrl });
      setFeishuTestStatus(result.sent ? "发送成功" : "发送失败，请检查 Webhook");
    } catch (error) {
      setFeishuTestStatus("发送失败");
      reportError(error);
    }
  };
  const createBackup = async () => {
    try {
      setDataStatus("正在创建一致性备份…");
      const result = await bridge.request<{ created: boolean; fileName?: string }>("app.backupData");
      setDataStatus(result.created ? `备份已创建：${result.fileName}` : "已取消备份");
    } catch (error) { setDataStatus("备份失败"); reportError(error); }
  };
  const stageRestore = async () => {
    if (!window.confirm("恢复会在下次启动前替换当前数据，并自动保留回滚副本。继续吗？")) return;
    try {
      setDataStatus("正在校验备份…");
      const result = await bridge.request<{ staged: boolean }>("app.restoreData");
      setDataStatus(result.staged ? "备份已校验并暂存，请重启 Taskato 完成恢复。" : "已取消恢复");
    } catch (error) { setDataStatus("恢复校验失败，当前数据未改变"); reportError(error); }
  };

  return (
    <div className="full-page settings-page page-enter">
      <div className="page-heading-row">
        <div><span className="section-kicker">完整应用设置</span><h2>设置</h2><p>完整保留旧版设置能力，设置仍由 C# 校验并保存。</p></div>
        <div className="heading-actions">
          <span className={`save-status ${saveStatus}`} role="status">{saveStatus === "saving" ? "正在保存…" : saveStatus === "dirty" ? "有未保存更改" : saveStatus === "error" ? "保存失败" : saveStatus === "saved" ? "已保存" : "设置已同步"}</span>
          <button className="secondary-button" onClick={() => { if (savedSettings) { setSettings(savedSettings); applyTheme(savedSettings); setSaveStatus("clean"); } }}>恢复已保存值</button>
          <button className="primary-button" disabled={saveStatus === "saving" || saveStatus === "clean"} onClick={() => void save()}>保存设置</button>
        </div>
      </div>
      <div className="settings-layout">
        <aside className="settings-nav">
          {sections.map(({ id, label, icon: Icon }) => <button key={id} className={active === id ? "active" : ""} onClick={() => navigate(id)}><Icon size={17} />{label}</button>)}
        </aside>
        <main className="settings-content">
          <section id="settings-theme"><div><h3>主题颜色</h3><p>选择与旧版一致的六组渐变主题。</p></div><div className="theme-palette" role="radiogroup" aria-label="主题颜色">{palettes.map(([primary, secondary], index) => <button key={primary} role="radio" aria-checked={settings.selectedColorIndex === index} className={settings.selectedColorIndex === index ? "selected" : ""} style={{ background: `linear-gradient(135deg,${primary},${secondary})` }} onClick={() => selectPalette(index)}>{settings.selectedColorIndex === index ? "✓" : ""}</button>)}</div></section>

          <section id="settings-pomodoro"><div><h3>番茄钟时长</h3><p>与旧版相同：工作 1–240 分钟，休息 1–30 分钟。</p></div>
            <div className="setting-row"><div><strong>工作时长</strong><span>每次调整 1 分钟</span></div><div className="stepper"><button onClick={() => patch("workMinutes", Math.max(1, settings.workMinutes - 1))}>−</button><span>{settings.workMinutes} min</span><button onClick={() => patch("workMinutes", Math.min(240, settings.workMinutes + 1))}>＋</button></div></div>
            <div className="setting-row"><div><strong>休息时长</strong><span>每次调整 1 分钟</span></div><div className="stepper"><button onClick={() => patch("restMinutes", Math.max(1, settings.restMinutes - 1))}>−</button><span>{settings.restMinutes} min</span><button onClick={() => patch("restMinutes", Math.min(30, settings.restMinutes + 1))}>＋</button></div></div>
          </section>

          <section id="settings-system"><div><h3>系统</h3><p>启动、Toast 与多番茄钟行为。</p></div>
            <div className="setting-row"><div><strong>开机自动启动</strong><span>登录 Windows 后启动 Taskato</span></div><Toggle label="开机自动启动" value={settings.autoStartEnabled} onChange={(v) => patch("autoStartEnabled", v)} /></div>
            <div className="setting-row"><div><strong>自动开始下一轮</strong></div><Toggle label="自动开始下一轮" value={settings.autoStartNextPomodoro} onChange={(v) => patch("autoStartNextPomodoro", v)} /></div>
            <div className="setting-row"><div><strong>Toast 显示计时</strong></div><Toggle label="Toast 显示计时" value={settings.enableToastTimer} onChange={(v) => patch("enableToastTimer", v)} /></div>
            <div className="setting-row"><div><strong>Toast 操作延迟（秒）</strong><span>0–5 秒，每次调整 0.5 秒</span></div><div className="stepper"><button onClick={() => patch("toastActionDelaySeconds", Math.max(0, settings.toastActionDelaySeconds - .5))}>−</button><span>{settings.toastActionDelaySeconds.toFixed(1)}</span><button onClick={() => patch("toastActionDelaySeconds", Math.min(5, settings.toastActionDelaySeconds + .5))}>＋</button></div></div>
            <div className="setting-row"><div><strong>多组番茄钟</strong><span>允许添加附加计时器</span></div><Toggle label="多组番茄钟" value={settings.enableMultiplePomodoros} onChange={(v) => patch("enableMultiplePomodoros", v)} /></div>
          </section>

          <section id="settings-summary"><div><h3>每日总结</h3><p>跨天后自动展示昨日完成成果。</p></div><div className="setting-row"><div><strong>自动显示昨日总结</strong></div><Toggle label="自动显示昨日总结" value={settings.autoShowDailySummary} onChange={(v) => patch("autoShowDailySummary", v)} /></div></section>

          <section id="settings-sound"><div><h3>提示音</h3><p>与旧版相同的六种选择，可试听当前选项。</p></div>
            <div className="sound-options">{["静音", "系统通知", "系统提示", "系统背景", "系统风铃", "自定义"].map((label, index) => <label key={label}><input type="radio" name="sound" checked={settings.notificationSoundChoice === index} onChange={() => patch("notificationSoundChoice", index)} />{label}</label>)}</div>
            {settings.notificationSoundChoice === 5 && <div className="setting-row nested"><div><strong>自定义声音</strong><span className="setting-path">{settings.customSoundPath || "尚未选择文件"}</span></div><button className="secondary-button" onClick={() => void chooseSound()}>选择文件</button></div>}
            <button className="secondary-button" onClick={() => void bridge.request("settings.previewSound", { notificationSoundChoice: settings.notificationSoundChoice, customSoundPath: settings.customSoundPath }).catch(reportError)}>试听</button>
          </section>

          <section id="settings-feishu"><div><h3>飞书通知</h3><p>Webhook、测试消息及四类提醒均完整保留。</p></div>
            <div className="setting-row"><div><strong>启用飞书通知</strong></div><Toggle label="启用飞书通知" value={settings.feishuEnabled} onChange={(v) => patch("feishuEnabled", v)} /></div>
            <div className="setting-row"><div><strong>Webhook URL</strong><span>飞书群机器人的 Webhook 地址</span></div><input className="text-input" value={settings.feishuWebhookUrl} onChange={(e) => patch("feishuWebhookUrl", e.target.value)} /></div>
            <div className="setting-actions"><button className="secondary-button" onClick={() => setFeishuHelp(!feishuHelp)}>如何获取 Webhook？</button><button className="secondary-button" onClick={() => void bridge.request<{ shouldConfigure: boolean }>("settings.openFeishuGuide").then((r) => r.shouldConfigure && setFeishuHelp(true)).catch(reportError)}>查看首次配置引导</button><button className="secondary-button" onClick={() => void testFeishu()}>发送测试消息</button></div>
            {feishuTestStatus && <p className="data-operation-status" role="status">{feishuTestStatus}</p>}
            {feishuHelp && <div className="settings-help"><strong>飞书电脑版客户端</strong><p>群设置 → 群机器人 → 添加自定义机器人 → 复制 Webhook</p><code>https://open.feishu.cn/open-apis/bot/v2/hook/...</code></div>}
            <div className="setting-row nested"><div><strong>工作结束通知</strong></div><Toggle label="工作结束通知" value={settings.feishuNotifyOnWork} onChange={(v) => patch("feishuNotifyOnWork", v)} /></div>
            <div className="setting-row nested"><div><strong>重复提醒 3 次（间隔 3 秒）</strong></div><Toggle label="重复提醒" value={settings.feishuRepeatEnabled} onChange={(v) => patch("feishuRepeatEnabled", v)} /></div>
            <div className="setting-row nested"><div><strong>休息过半提醒</strong></div><Toggle label="休息过半提醒" value={settings.feishuRestHalfwayEnabled} onChange={(v) => patch("feishuRestHalfwayEnabled", v)} /></div>
            <div className="setting-row nested"><div><strong>休息结束通知</strong></div><Toggle label="休息结束通知" value={settings.feishuNotifyOnRest} onChange={(v) => patch("feishuNotifyOnRest", v)} /></div>
          </section>

          <section id="settings-data"><div><h3>数据与兼容</h3><p>保留已批准的数据保护和旧界面回退能力，不替代上述旧版设置。</p></div>
            <div className="setting-row"><div><strong>下次启动使用旧 WPF 界面</strong><span>WebView2 故障时的回退开关</span></div><Toggle label="使用旧界面" value={settings.useLegacyWorkspace} onChange={(v) => patch("useLegacyWorkspace", v)} /></div>
            <div className="setting-row"><div><strong>本地数据目录</strong></div><div className="setting-actions"><button className="secondary-button" onClick={() => void bridge.request("app.openDataFolder").catch(reportError)}>打开数据位置</button><button className="secondary-button" onClick={() => void bridge.request("app.openLogsFolder").catch(reportError)}>打开诊断日志</button></div></div>
            <div className="setting-row"><div><strong>备份与恢复</strong><span>恢复前校验并自动保留回滚副本</span></div><div className="setting-actions"><button className="secondary-button" onClick={() => void createBackup()}>创建备份</button><button className="secondary-button" onClick={() => void stageRestore()}>从备份恢复</button></div></div>
            {dataStatus && <p className="data-operation-status" role="status">{dataStatus}</p>}
          </section>
        </main>
      </div>
    </div>
  );
}
