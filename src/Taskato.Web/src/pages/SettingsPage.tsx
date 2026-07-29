import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
} from "react";
import {
  ArrowCounterClockwise,
  Bell,
  CalendarBlank,
  CaretDown,
  CaretLeft,
  CaretRight,
  Check,
  CheckCircle,
  ClockCounterClockwise,
  Copy,
  DotsThree,
  DownloadSimple,
  GearSix,
  ListChecks,
  NotePencil,
  Pause,
  Play,
  Plus,
  Stop,
  Sparkle,
  Timer,
  Trash,
  X,
} from "@phosphor-icons/react";
import {
  bridge,
  type SettingsDto,
  type SummaryResult,
  type TaskActivityDto,
  type TaskDto,
  type TaskListResult,
  type TimerListResult,
} from "../bridge";
import {
  applyTheme,
  IconButton,
  toLocalDateInput,
  useBridgeRefresh,
} from "../ui";

function Toggle({
  value,
  onChange,
  label,
}: {
  value: boolean;
  onChange(value: boolean): void;
  label: string;
}) {
  return (
    <button
      className={`switch ${value ? "on" : ""}`}
      onClick={() => onChange(!value)}
      aria-label={label}
      aria-pressed={value}
    >
      <span />
    </button>
  );
}
export function SettingsPage({ reportError }: { reportError(error: unknown): void }) {
  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [savedSettings, setSavedSettings] = useState<SettingsDto | null>(null);
  const [saveStatus, setSaveStatus] = useState<
    "clean" | "dirty" | "saving" | "saved" | "error"
  >("clean");
  const [dataStatus, setDataStatus] = useState("");
  const [settingsSection, setSettingsSection] = useState<
    "timer" | "notifications" | "theme"
  >("timer");
  useEffect(() => {
    void bridge
      .request<SettingsDto>("settings.get")
      .then((value) => {
        setSettings(value);
        setSavedSettings(value);
        setSaveStatus("clean");
      })
      .catch(reportError);
  }, [reportError]);
  if (!settings)
    return (
      <div className="full-page">
        <p>正在加载设置…</p>
      </div>
    );
  const patch = <K extends keyof SettingsDto>(
    key: K,
    value: SettingsDto[K],
  ) => {
    setSettings({ ...settings, [key]: value });
    setSaveStatus("dirty");
  };
  const save = async () => {
    if (
      settings.workMinutes < 1 ||
      settings.workMinutes > 240 ||
      settings.restMinutes < 1 ||
      settings.restMinutes > 120
    ) {
      setSaveStatus("error");
      reportError(new Error("请检查番茄钟时长，数值超出允许范围。"));
      return;
    }
    try {
      setSaveStatus("saving");
      const result = await bridge.request<SettingsDto>(
        "settings.save",
        settings as unknown as Record<string, unknown>,
      );
      setSettings(result);
      setSavedSettings(result);
      applyTheme(result);
      setSaveStatus("saved");
    } catch (error) {
      setSaveStatus("error");
      reportError(error);
    }
  };
  const chooseSound = async () => {
    try {
      const result = await bridge.request<{ selected: boolean; path: string }>(
        "settings.chooseSound",
      );
      if (result.selected) {
        setSettings({
          ...settings,
          notificationSoundChoice: 5,
          customSoundPath: result.path,
        });
        setSaveStatus("dirty");
      }
    } catch (error) {
      reportError(error);
    }
  };
  const previewSound = () =>
    bridge
      .request("settings.previewSound", {
        notificationSoundChoice: settings.notificationSoundChoice,
        customSoundPath: settings.customSoundPath,
      })
      .catch(reportError);
  const createBackup = async () => {
    try {
      setDataStatus("正在创建一致性备份…");
      const result = await bridge.request<{
        created: boolean;
        fileName?: string;
      }>("app.backupData");
      setDataStatus(
        result.created ? `备份已创建：${result.fileName}` : "已取消备份",
      );
    } catch (error) {
      setDataStatus("备份失败");
      reportError(error);
    }
  };
  const stageRestore = async () => {
    if (
      !window.confirm(
        "恢复会在下次启动前替换当前数据，并自动保留回滚副本。继续选择备份吗？",
      )
    )
      return;
    try {
      setDataStatus("正在校验备份…");
      const result = await bridge.request<{
        staged: boolean;
        restartRequired?: boolean;
      }>("app.restoreData");
      setDataStatus(
        result.staged
          ? "备份已校验并暂存，请退出并重新启动 Taskato 完成恢复。"
          : "已取消恢复",
      );
    } catch (error) {
      setDataStatus("恢复校验失败，当前数据未改变");
      reportError(error);
    }
  };
  const navigateSettings = (section: "timer" | "notifications" | "theme") => {
    setSettingsSection(section);
    requestAnimationFrame(() =>
      document
        .getElementById(`settings-${section}`)
        ?.scrollIntoView({ behavior: "smooth", block: "start" }),
    );
  };
  return (
    <div className="full-page settings-page page-enter">
      <div className="page-heading-row">
        <div>
          <span className="section-kicker">完整应用设置</span>
          <h2>设置</h2>
          <p>设置继续保存在原有 settings.json，并由 C# 校验。</p>
        </div>
        <div className="heading-actions">
          <span className={`save-status ${saveStatus}`} role="status">
            {saveStatus === "saving"
              ? "正在保存…"
              : saveStatus === "dirty"
                ? "有未保存更改"
                : saveStatus === "error"
                  ? "保存失败"
                  : saveStatus === "saved"
                    ? "已保存"
                    : "设置已同步"}
          </span>
          <button
            className="secondary-button"
            disabled={!savedSettings || saveStatus === "saving"}
            onClick={() => {
              if (!savedSettings) return;
              setSettings(savedSettings);
              applyTheme(savedSettings);
              setSaveStatus("clean");
            }}
          >
            恢复已保存值
          </button>
          <button
            className="primary-button"
            disabled={saveStatus === "saving" || saveStatus === "clean"}
            onClick={() => void save()}
          >
            保存设置
          </button>
        </div>
      </div>
      <div className="settings-layout">
        <aside className="settings-nav">
          <button
            className={settingsSection === "timer" ? "active" : ""}
            onClick={() => navigateSettings("timer")}
          >
            <Timer size={17} />
            番茄钟
          </button>
          <button
            className={settingsSection === "notifications" ? "active" : ""}
            onClick={() => navigateSettings("notifications")}
          >
            <Bell size={17} />
            提醒与总结
          </button>
          <button
            className={settingsSection === "theme" ? "active" : ""}
            onClick={() => navigateSettings("theme")}
          >
            <Sparkle size={17} />
            主题与回退
          </button>
        </aside>
        <main className="settings-content">
          <section id="settings-timer">
            <div>
              <h3>番茄钟</h3>
              <p>修改空闲计时器的默认时长。</p>
            </div>
            <div className="setting-row">
              <div>
                <strong>开机自动启动</strong>
                <span>登录 Windows 后启动 Taskato</span>
              </div>
              <Toggle
                label="开机自动启动"
                value={settings.autoStartEnabled}
                onChange={(value) => patch("autoStartEnabled", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>专注分钟</strong>
                <span>1–240</span>
              </div>
              <input
                className="number-input"
                type="number"
                min="1"
                max="240"
                value={settings.workMinutes}
                onChange={(event) =>
                  patch("workMinutes", Number(event.target.value))
                }
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>休息分钟</strong>
                <span>1–120</span>
              </div>
              <input
                className="number-input"
                type="number"
                min="1"
                max="120"
                value={settings.restMinutes}
                onChange={(event) =>
                  patch("restMinutes", Number(event.target.value))
                }
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>多组番茄钟</strong>
                <span>允许添加附加计时器</span>
              </div>
              <Toggle
                label="多组番茄钟"
                value={settings.enableMultiplePomodoros}
                onChange={(value) => patch("enableMultiplePomodoros", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>自动开始下一轮</strong>
                <span>继续工作时自动倒计时</span>
              </div>
              <Toggle
                label="自动开始下一轮"
                value={settings.autoStartNextPomodoro}
                onChange={(value) => patch("autoStartNextPomodoro", value)}
              />
            </div>
          </section>
          <section id="settings-notifications">
            <div>
              <h3>提醒与总结</h3>
              <p>桌面 Toast、飞书和跨天总结仍由 C# 执行。</p>
            </div>
            <div className="setting-row">
              <div>
                <strong>Toast 显示计时</strong>
              </div>
              <Toggle
                label="Toast 显示计时"
                value={settings.enableToastTimer}
                onChange={(value) => patch("enableToastTimer", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>Toast 操作延迟（秒）</strong>
                <span>防止通知按钮被连续误触</span>
              </div>
              <input
                className="number-input"
                type="number"
                min="0"
                max="60"
                step="0.5"
                value={settings.toastActionDelaySeconds}
                onChange={(event) =>
                  patch("toastActionDelaySeconds", Number(event.target.value))
                }
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>通知声音</strong>
                <span>0 为静音，5 为自定义文件</span>
              </div>
              <div className="setting-actions">
                <select
                  className="select-button"
                  value={settings.notificationSoundChoice}
                  onChange={(event) =>
                    patch("notificationSoundChoice", Number(event.target.value))
                  }
                >
                  <option value={0}>静音</option>
                  <option value={1}>系统通知</option>
                  <option value={2}>系统提示</option>
                  <option value={3}>系统背景</option>
                  <option value={4}>系统风铃</option>
                  <option value={5}>自定义</option>
                </select>
                <button className="secondary-button" onClick={previewSound}>
                  试听
                </button>
              </div>
            </div>
            <div className="setting-row nested">
              <div>
                <strong>自定义声音</strong>
                <span className="setting-path">
                  {settings.customSoundPath || "尚未选择文件"}
                </span>
              </div>
              <button
                className="secondary-button"
                onClick={() => void chooseSound()}
              >
                选择文件
              </button>
            </div>
            <div className="setting-row">
              <div>
                <strong>自动显示昨日总结</strong>
              </div>
              <Toggle
                label="自动显示昨日总结"
                value={settings.autoShowDailySummary}
                onChange={(value) => patch("autoShowDailySummary", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>飞书通知</strong>
              </div>
              <Toggle
                label="飞书通知"
                value={settings.feishuEnabled}
                onChange={(value) => patch("feishuEnabled", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>Webhook URL</strong>
                <span>环境变量仍可覆盖</span>
              </div>
              <input
                className="text-input"
                value={settings.feishuWebhookUrl}
                onChange={(event) =>
                  patch("feishuWebhookUrl", event.target.value)
                }
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>工作结束通知</strong>
              </div>
              <Toggle
                label="工作结束通知"
                value={settings.feishuNotifyOnWork}
                onChange={(value) => patch("feishuNotifyOnWork", value)}
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>休息结束通知</strong>
              </div>
              <Toggle
                label="休息结束通知"
                value={settings.feishuNotifyOnRest}
                onChange={(value) => patch("feishuNotifyOnRest", value)}
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>重复提醒</strong>
              </div>
              <Toggle
                label="重复提醒"
                value={settings.feishuRepeatEnabled}
                onChange={(value) => patch("feishuRepeatEnabled", value)}
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>休息过半提醒</strong>
              </div>
              <Toggle
                label="休息过半提醒"
                value={settings.feishuRestHalfwayEnabled}
                onChange={(value) => patch("feishuRestHalfwayEnabled", value)}
              />
            </div>
          </section>
          <section id="settings-theme">
            <div>
              <h3>主题与回退</h3>
              <p>主题保存后立即映射为网页 CSS 变量。</p>
            </div>
            <div className="setting-row">
              <div>
                <strong>主色</strong>
              </div>
              <input
                className="text-input color-input"
                value={settings.themePrimaryColor}
                onChange={(event) => {
                  patch("themePrimaryColor", event.target.value);
                  document.documentElement.style.setProperty(
                    "--primary",
                    event.target.value,
                  );
                }}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>辅色</strong>
              </div>
              <input
                className="text-input color-input"
                value={settings.themeSecondaryColor}
                onChange={(event) => {
                  patch("themeSecondaryColor", event.target.value);
                  document.documentElement.style.setProperty(
                    "--secondary",
                    event.target.value,
                  );
                }}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>下次启动使用旧 WPF 界面</strong>
                <span>WebView2 故障时的持久回退开关</span>
              </div>
              <Toggle
                label="使用旧界面"
                value={settings.useLegacyWorkspace}
                onChange={(value) => {
                  if (
                    value &&
                    !window.confirm(
                      "旧界面只用于 WebView2 故障回退。确定下次启动切换吗？",
                    )
                  )
                    return;
                  patch("useLegacyWorkspace", value);
                }}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>本地数据目录</strong>
                <span>数据库、设置、恢复回滚和诊断日志均保存在本机</span>
              </div>
              <div className="setting-actions">
                <button
                  className="secondary-button"
                  onClick={() =>
                    void bridge.request("app.openDataFolder").catch(reportError)
                  }
                >
                  打开数据位置
                </button>
                <button
                  className="secondary-button"
                  onClick={() =>
                    void bridge.request("app.openLogsFolder").catch(reportError)
                  }
                >
                  打开诊断日志
                </button>
              </div>
            </div>
            <div className="setting-row">
              <div>
                <strong>备份与恢复</strong>
                <span>备份包含 SQLite 数据和设置；恢复前会校验并保留回滚副本</span>
              </div>
              <div className="setting-actions">
                <button
                  className="secondary-button"
                  onClick={() => void createBackup()}
                >
                  创建备份
                </button>
                <button
                  className="secondary-button"
                  onClick={() => void stageRestore()}
                >
                  从备份恢复
                </button>
              </div>
            </div>
            {dataStatus && (
              <p className="data-operation-status" role="status">
                {dataStatus}
              </p>
            )}
          </section>
        </main>
      </div>
    </div>
  );
}
