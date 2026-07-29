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
  MagnifyingGlass,
  NotePencil,
  Pause,
  Play,
  Plus,
  Stop,
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

type TaskNode = TaskDto & { children: TaskNode[] };

function buildTree(tasks: TaskDto[]): TaskNode[] {
  const nodes = new Map(
    tasks.map((task) => [task.id, { ...task, children: [] } as TaskNode]),
  );
  const roots: TaskNode[] = [];
  nodes.forEach((node) => {
    const parent = node.parentId == null ? null : nodes.get(node.parentId);
    if (parent) parent.children.push(node);
    else roots.push(node);
  });
  return roots;
}
function TaskTreeRow({
  task,
  selectedId,
  expanded,
  onToggle,
  onSelect,
  onComplete,
}: {
  task: TaskNode;
  selectedId: number | null;
  expanded: Set<number>;
  onToggle(id: number): void;
  onSelect(id: number): void;
  onComplete(task: TaskDto): void;
}) {
  const hasChildren = task.children.length > 0;
  const isExpanded = expanded.has(task.id);
  return (
    <>
      <div
        className={`tree-row ${selectedId === task.id ? "selected" : ""}`}
        style={{ "--depth": task.depth } as React.CSSProperties}
      >
        <button
          className="tree-expand"
          aria-label={
            hasChildren
              ? isExpanded
                ? "收起子任务"
                : "展开子任务"
              : "无子任务"
          }
          onClick={() => hasChildren && onToggle(task.id)}
        >
          {hasChildren ? (
            <CaretRight className={isExpanded ? "rotated" : ""} size={14} />
          ) : (
            <span />
          )}
        </button>
        <button
          className={`task-check ${task.isCompleted ? "checked" : ""}`}
          aria-label={task.isCompleted ? "取消完成" : "标记完成"}
          onClick={() => onComplete(task)}
        >
          {task.isCompleted && <Check size={12} weight="bold" />}
        </button>
        <button className="tree-title" onClick={() => onSelect(task.id)}>
          {task.title}
        </button>
        {task.priority >= 2 && (
          <span className="priority-mark" title="高优先级" />
        )}
        {hasChildren && (
          <span className="tree-progress">
            {task.completedLeafCount}/{task.totalLeafCount}
          </span>
        )}
      </div>
      {hasChildren &&
        isExpanded &&
        task.children.map((child) => (
          <TaskTreeRow
            key={child.id}
            task={child}
            selectedId={selectedId}
            expanded={expanded}
            onToggle={onToggle}
            onSelect={onSelect}
            onComplete={onComplete}
          />
        ))}
    </>
  );
}

function CompletionDialog({
  task,
  onClose,
  onComplete,
}: {
  task: TaskDto;
  onClose(): void;
  onComplete(duration: number | null): void;
}) {
  const [duration, setDuration] = useState("");
  return (
    <div className="modal-backdrop">
      <section className="modal" role="dialog" aria-modal="true">
        <header>
          <div>
            <span className="section-kicker">完成叶子任务</span>
            <h2>{task.title}</h2>
          </div>
          <IconButton label="关闭" onClick={onClose}>
            <X size={19} />
          </IconButton>
        </header>
        <p>可以记录本次实际专注时长；全部子任务完成后，父级成果会自动生成。</p>
        <div className="modal-fields">
          <label>
            <span>专注时长（分钟，可选）</span>
            <input
              autoFocus
              type="number"
              min="1"
              value={duration}
              onChange={(event) => setDuration(event.target.value)}
            />
          </label>
        </div>
        <footer>
          <button className="secondary-button" onClick={onClose}>
            取消
          </button>
          <button
            className="primary-button"
            onClick={() => onComplete(duration ? Number(duration) : null)}
          >
            <Check size={17} />
            确认完成
          </button>
        </footer>
      </section>
    </div>
  );
}

export function TaskPage({
  reportError,
  requestedTaskId,
}: {
  reportError(error: unknown): void;
  requestedTaskId: number | null;
}) {
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [expanded, setExpanded] = useState(new Set<number>());
  const [filter, setFilter] = useState("");
  const [rootTitle, setRootTitle] = useState("");
  const [childTitle, setChildTitle] = useState("");
  const [titleDraft, setTitleDraft] = useState("");
  const [notesDraft, setNotesDraft] = useState("");
  const [notesStatus, setNotesStatus] = useState<
    "saved" | "unsaved" | "saving" | "error"
  >("saved");
  const [taskMenuOpen, setTaskMenuOpen] = useState(false);
  const [undoAction, setUndoAction] = useState<{
    message: string;
    run(): Promise<void>;
  } | null>(null);
  const notesRequestVersion = useRef(0);
  const [completionTask, setCompletionTask] = useState<TaskDto | null>(null);
  const [parentCandidates, setParentCandidates] = useState<TaskDto[]>([]);
  const [timerState, setTimerState] = useState<TimerListResult>({
    activeId: "",
    multipleEnabled: false,
    timers: [],
  });

  const load = useCallback(async () => {
    try {
      const result = await bridge.request<TaskListResult>("tasks.list");
      setTasks(result.tasks);
      setSelectedId((current) =>
        result.tasks.some((task) => task.id === current)
          ? current
          : (result.selectedTaskId ?? result.tasks[0]?.id ?? null),
      );
      setExpanded((current) =>
        current.size
          ? current
          : new Set(
              result.tasks
                .filter((task) => task.hasChildren)
                .map((task) => task.id),
            ),
      );
    } catch (error) {
      reportError(error);
    }
  }, [reportError]);
  const loadTimers = useCallback(async () => {
    try {
      setTimerState(await bridge.request<TimerListResult>("pomodoro.list"));
    } catch (error) {
      reportError(error);
    }
  }, [reportError]);
  useEffect(() => {
    void load();
    void loadTimers();
  }, [load, loadTimers]);
  useBridgeRefresh("tasks.changed", () => void load());
  useBridgeRefresh("pomodoro.changed", () => void loadTimers());
  useEffect(() => {
    if (requestedTaskId == null) return;
    setSelectedId(requestedTaskId);
    requestAnimationFrame(() =>
      document
        .querySelector<HTMLElement>(".task-title-input")
        ?.scrollIntoView({ block: "center" }),
    );
  }, [requestedTaskId]);
  useEffect(() => {
    if (selectedId == null) {
      setParentCandidates([]);
      return;
    }
    void bridge
      .request<TaskDto[]>("tasks.parentCandidates", { taskId: selectedId })
      .then(setParentCandidates)
      .catch(reportError);
  }, [reportError, selectedId, tasks]);

  const filtered = useMemo(
    () =>
      filter
        ? tasks.filter((task) =>
            task.title.toLowerCase().includes(filter.toLowerCase()),
          )
        : tasks,
    [filter, tasks],
  );
  const roots = useMemo(() => buildTree(filtered), [filtered]);
  const selected = tasks.find((task) => task.id === selectedId) ?? null;
  const children = tasks.filter((task) => task.parentId === selectedId);
  useEffect(
    () => {
      setTitleDraft(selected?.title ?? "");
      setNotesDraft(selected?.notes ?? "");
      setNotesStatus("saved");
    },
    [selected?.id, selected?.notes, selected?.title],
  );
  const activeTimer =
    timerState.timers.find((timer) => timer.id === timerState.activeId) ??
    timerState.timers[0];

  const add = async (event: FormEvent, parentId: number | null) => {
    event.preventDefault();
    const title = parentId == null ? rootTitle.trim() : childTitle.trim();
    if (!title) return;
    try {
      await bridge.request("tasks.add", { title, parentId });
      if (parentId == null) setRootTitle("");
      else setChildTitle("");
      await load();
    } catch (error) {
      reportError(error);
    }
  };
  const offerUndo = (message: string, run: () => Promise<void>) => {
    setUndoAction({ message, run });
    window.setTimeout(() => {
      setUndoAction((current) => (current?.message === message ? null : current));
    }, 6000);
  };
  const toggleComplete = (task: TaskDto) => {
    if (!task.isCompleted && task.hasChildren) {
      reportError(new Error("父任务会在全部叶子任务完成后自动达成。"));
      return;
    }
    if (task.isCompleted)
      return void bridge
        .request("tasks.toggle", { taskId: task.id, completed: false })
        .then(async () => {
          await load();
          offerUndo(`已取消完成“${task.title}”`, async () => {
            await bridge.request("tasks.toggle", {
              taskId: task.id,
              completed: true,
              durationMinutes: task.completionDurationMinutes,
            });
            await load();
          });
        })
        .catch(reportError);
    if (bridge.isNative)
      return void bridge
        .request<{ completed: boolean; cancelled: boolean }>(
          "tasks.completeDialog",
          { taskId: task.id },
        )
        .then(async (result) => {
          if (result.completed) {
            await load();
            offerUndo(`已完成“${task.title}”`, async () => {
              await bridge.request("tasks.toggle", {
                taskId: task.id,
                completed: false,
              });
              await load();
            });
          }
        })
        .catch(reportError);
    setCompletionTask(task);
  };
  const complete = async (durationMinutes: number | null) => {
    if (!completionTask) return;
    try {
      await bridge.request("tasks.toggle", {
        taskId: completionTask.id,
        completed: true,
        durationMinutes,
      });
      setCompletionTask(null);
      await load();
      const completedTask = completionTask;
      offerUndo(`已完成“${completedTask.title}”`, async () => {
        await bridge.request("tasks.toggle", {
          taskId: completedTask.id,
          completed: false,
        });
        await load();
      });
    } catch (error) {
      reportError(error);
    }
  };
  const updateTask = async (
    changes: { title?: string; priority?: number; notes?: string },
    undoChanges?: { title?: string; priority?: number; notes?: string },
    successMessage?: string,
  ) => {
    if (!selected) return;
    try {
      await bridge.request("tasks.update", { taskId: selected.id, ...changes });
      await load();
      if (undoChanges && successMessage) {
        const taskId = selected.id;
        offerUndo(successMessage, async () => {
          await bridge.request("tasks.update", { taskId, ...undoChanges });
          await load();
        });
      }
    } catch (error) {
      reportError(error);
    }
  };
  const deleteTask = async (mode: "deleteBranch" | "promoteChildren") => {
    if (
      !selected ||
      !window.confirm(`确定处理“${selected.title}”吗？完成历史会保留。`)
    )
      return;
    try {
      await bridge.request("tasks.delete", { taskId: selected.id, mode });
      await load();
    } catch (error) {
      reportError(error);
    }
  };
  const moveTask = async (parentId: number | null) => {
    if (!selected) return;
    const previousParentId = selected.parentId;
    const taskId = selected.id;
    try {
      await bridge.request("tasks.move", { taskId, parentId });
      await load();
      offerUndo(`已移动“${selected.title}”`, async () => {
        await bridge.request("tasks.move", {
          taskId,
          parentId: previousParentId,
        });
        await load();
      });
    } catch (error) {
      reportError(error);
    }
  };
  const timerCommand = async (command: string) => {
    try {
      setTimerState(
        await bridge.request<TimerListResult>("pomodoro.command", {
          command,
          timerId: activeTimer?.id,
        }),
      );
    } catch (error) {
      reportError(error);
    }
  };

  useEffect(() => {
    if (!selected || notesDraft === selected.notes) {
      setNotesStatus("saved");
      return;
    }
    setNotesStatus("unsaved");
    const version = ++notesRequestVersion.current;
    const timer = window.setTimeout(() => {
      setNotesStatus("saving");
      void bridge
        .request("tasks.update", {
          taskId: selected.id,
          notes: notesDraft,
        })
        .then(async () => {
          if (notesRequestVersion.current !== version) return;
          setNotesStatus("saved");
          await load();
        })
        .catch((error) => {
          if (notesRequestVersion.current !== version) return;
          setNotesStatus("error");
          reportError(error);
        });
    }, 700);
    return () => window.clearTimeout(timer);
  }, [load, notesDraft, reportError, selected?.id, selected?.notes]);

  return (
    <div className="task-workspace page-enter">
      <section className="tree-pane">
        <div className="pane-heading">
          <div>
            <span className="section-kicker">工作空间</span>
            <h2>项目与任务</h2>
          </div>
        </div>
        <label className="pane-search">
          <MagnifyingGlass size={16} />
          <input
            type="search"
            aria-label="筛选任务"
            placeholder="筛选任务"
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
          />
        </label>
        <div className="tree-filter-row">
          <button className="filter-chip active">全部</button>
          <span>{roots.length} 个项目</span>
        </div>
        <div className="task-tree">
          {roots.map((task) => (
            <TaskTreeRow
              key={task.id}
              task={task}
              selectedId={selectedId}
              expanded={expanded}
              onToggle={(id) =>
                setExpanded((value) => {
                  const next = new Set(value);
                  next.has(id) ? next.delete(id) : next.add(id);
                  return next;
                })
              }
              onSelect={setSelectedId}
              onComplete={toggleComplete}
            />
          ))}
        </div>
        <form
          className="inline-add"
          onSubmit={(event) => void add(event, null)}
        >
          <button type="submit" aria-label="新建顶级任务">
            <Plus size={16} />
          </button>
          <input
            value={rootTitle}
            onChange={(event) => setRootTitle(event.target.value)}
            placeholder="新建顶级任务"
          />
        </form>
      </section>
      <main className="task-canvas">
        {selected ? (
          <>
            <div className="canvas-header">
              <div className="breadcrumbs">
                <span>任务</span>
                <CaretRight size={13} />
                <span>{selected.title}</span>
              </div>
              <IconButton
                label="更多操作"
                onClick={() => setTaskMenuOpen((value) => !value)}
              >
                <DotsThree size={20} />
              </IconButton>
              {taskMenuOpen && (
                <div className="task-action-menu" role="menu">
                  <button
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      document
                        .querySelector<HTMLInputElement>(".task-title-input")
                        ?.focus();
                    }}
                  >
                    <NotePencil size={16} />
                    编辑标题
                  </button>
                  <button
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      document
                        .getElementById("task-notes")
                        ?.scrollIntoView({ behavior: "smooth", block: "center" });
                    }}
                  >
                    <NotePencil size={16} />
                    编辑任务笔记
                  </button>
                  <button
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      document
                        .getElementById("task-operations")
                        ?.scrollIntoView({ behavior: "smooth", block: "center" });
                    }}
                  >
                    <ListChecks size={16} />
                    移动或调整层级
                  </button>
                  <button
                    className="danger"
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      void deleteTask("deleteBranch");
                    }}
                  >
                    <Trash size={16} />
                    删除任务分支
                  </button>
                </div>
              )}
            </div>
            <div className="task-title-block">
              <button
                className={`task-check large ${selected.isCompleted ? "checked" : ""}`}
                onClick={() => toggleComplete(selected)}
              >
                {selected.isCompleted && <Check size={15} />}
              </button>
              <div>
                <input
                  className="task-title-input"
                  aria-label="任务标题"
                  maxLength={500}
                  value={titleDraft}
                  onChange={(event) => setTitleDraft(event.target.value)}
                  onBlur={() => {
                    const title = titleDraft.trim();
                    if (title && title !== selected.title)
                      void updateTask(
                        { title },
                        { title: selected.title },
                        `已重命名为“${title}”`,
                      );
                    else setTitleDraft(selected.title);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") event.currentTarget.blur();
                    if (event.key === "Escape") {
                      setTitleDraft(selected.title);
                      event.currentTarget.blur();
                    }
                  }}
                />
                <p>
                  {selected.hasChildren
                    ? "父级任务由叶子任务的完成状态自动维护。"
                    : "专注完成当前任务，并按需记录实际耗时。"}
                </p>
              </div>
            </div>
            <div className="meta-strip">
              <button
                title="点击切换优先级"
                onClick={() =>
                  void updateTask(
                    { priority: (selected.priority + 1) % 4 },
                    { priority: selected.priority },
                    "已调整任务优先级",
                  )
                }
              >
                优先级 {selected.priority}
              </button>
              <span className="meta-item">
                <CalendarBlank size={16} />
                {new Date(selected.createdAt).toLocaleDateString()}
              </span>
            </div>
            <section className="content-section">
              <div className="section-title-row">
                <div>
                  <h3>直属子任务</h3>
                  <span>
                    {children.filter((item) => item.isCompleted).length}/
                    {children.length} 已完成
                  </span>
                </div>
              </div>
              <div className="progress-track">
                <span
                  style={{
                    width: `${children.length ? (children.filter((item) => item.isCompleted).length / children.length) * 100 : 0}%`,
                  }}
                />
              </div>
              <div className="subtask-list">
                {children.map((item) => (
                  <div className="subtask-row" key={item.id}>
                    <button
                      className={`task-check ${item.isCompleted ? "checked" : ""}`}
                      onClick={() => toggleComplete(item)}
                    >
                      {item.isCompleted && <Check size={12} />}
                    </button>
                    <span className={item.isCompleted ? "done" : ""}>
                      {item.title}
                    </span>
                    <small>
                      {item.completionDurationMinutes
                        ? `${item.completionDurationMinutes} 分钟`
                        : ""}
                    </small>
                  </div>
                ))}
              </div>
              {selected.canAddChild && (
                <form
                  className="inline-add"
                  onSubmit={(event) => void add(event, selected.id)}
                >
                  <button type="submit">
                    <Plus size={16} />
                  </button>
                  <input
                    value={childTitle}
                    onChange={(event) => setChildTitle(event.target.value)}
                    placeholder="添加子任务，按 Enter 保存"
                  />
                </form>
              )}
            </section>
            <section id="task-notes" className="content-section notes-section">
              <div className="section-title-row">
                <h3>任务笔记</h3>
                <span
                  className={`save-status ${notesStatus}`}
                  role={notesStatus === "error" ? "alert" : "status"}
                >
                  {notesStatus === "saving"
                    ? "正在保存…"
                    : notesStatus === "unsaved"
                      ? "等待自动保存"
                      : notesStatus === "error"
                        ? "保存失败，继续输入可重试"
                        : "已保存"}
                </span>
              </div>
              <textarea
                aria-label="任务笔记"
                maxLength={4000}
                placeholder="记录上下文、验收要点或下一步；停止输入后自动保存。"
                value={notesDraft}
                onChange={(event) => setNotesDraft(event.target.value)}
              />
            </section>
            <section id="task-operations" className="content-section">
              <div className="section-title-row">
                <h3>任务操作</h3>
              </div>
              <div className="setting-row compact-setting-row">
                <div>
                  <strong>所属父任务</strong>
                  <span>移动后由 C# 校验循环引用和五层限制</span>
                </div>
                <select
                  className="select-button"
                  value={selected.parentId ?? ""}
                  onChange={(event) =>
                    void moveTask(
                      event.target.value ? Number(event.target.value) : null,
                    )
                  }
                >
                  <option value="">顶级任务</option>
                  {parentCandidates.map((candidate) => (
                    <option key={candidate.id} value={candidate.id}>
                      {"　".repeat(candidate.depth)}
                      {candidate.title}
                    </option>
                  ))}
                </select>
              </div>
              <div className="timer-controls">
                <button
                  className="secondary-button"
                  onClick={() => void deleteTask("deleteBranch")}
                >
                  删除任务分支
                </button>
                {selected.hasChildren && (
                  <button
                    className="secondary-button"
                    onClick={() => void deleteTask("promoteChildren")}
                  >
                    仅删父级并提升子任务
                  </button>
                )}
              </div>
            </section>
          </>
        ) : (
          <div className="empty-state">
            <h2>还没有任务</h2>
            <p>从左侧创建一个顶级任务开始。</p>
          </div>
        )}
      </main>
      <aside className="inspector-pane">
        <section className="focus-panel">
          <div className="focus-heading">
            <div>
              <Timer size={19} />
              <span>当前专注</span>
            </div>
            <span className="live-label">
              {activeTimer?.status ?? "未连接"}
            </span>
          </div>
          <select
            className="timer-select"
            value={timerState.activeId}
            onChange={(event) =>
              void bridge
                .request("pomodoro.command", {
                  command: "select",
                  timerId: event.target.value,
                })
                .then(loadTimers)
                .catch(reportError)
            }
          >
            {timerState.timers.map((timer) => (
              <option key={timer.id} value={timer.id}>
                {timer.name}
              </option>
            ))}
          </select>
          <strong className="timer-value">
            {activeTimer?.display ?? "25:00"}
          </strong>
          <p>{selected?.title ?? "选择任务后开始专注"}</p>
          <div className="timer-progress">
            <span style={{ width: `${(activeTimer?.progress ?? 1) * 100}%` }} />
          </div>
          <div className="timer-controls">
            <button
              className="primary-control"
              disabled={
                activeTimer?.isStarted
                  ? !activeTimer.canPause
                  : !activeTimer?.canStart
              }
              onClick={() =>
                void timerCommand(activeTimer?.isStarted ? "pause" : "start")
              }
            >
              {activeTimer?.isStarted ? (
                <Pause size={18} />
              ) : (
                <Play size={18} />
              )}
              {activeTimer?.isStarted ? "暂停/继续" : "开始"}
            </button>
            <IconButton
              label="停止计时"
              disabled={!activeTimer?.canStop}
              onClick={() => void timerCommand("stop")}
            >
              <Stop size={17} />
            </IconButton>
          </div>
          <div className="timer-secondary-controls">
            <button
              disabled={!activeTimer?.canRest}
              onClick={() => void timerCommand("rest")}
            >
              提前休息
            </button>
            {timerState.multipleEnabled && (
              <button onClick={() => void timerCommand("add")}>
                添加计时器
              </button>
            )}
            {timerState.multipleEnabled && activeTimer?.id !== "timer-0" && (
              <button onClick={() => void timerCommand("remove")}>
                移除当前计时器
              </button>
            )}
          </div>
        </section>
        <section className="details-panel">
          <div className="section-title-row">
            <h3>任务详情</h3>
            <IconButton
              label="编辑任务标题"
              onClick={() =>
                document
                  .querySelector<HTMLInputElement>(".task-title-input")
                  ?.focus()
              }
            >
              <NotePencil size={16} />
            </IconButton>
          </div>
          <dl>
            <div>
              <dt>状态</dt>
              <dd>
                <span
                  className={`status-dot ${selected && !selected.isCompleted ? "active" : ""}`}
                />
                {selected?.isCompleted ? "已完成" : "进行中"}
              </dd>
            </div>
            <div>
              <dt>层级</dt>
              <dd>{selected ? `${selected.depth + 1} / 5` : "—"}</dd>
            </div>
            <div>
              <dt>累计专注</dt>
              <dd>
                {selected?.completionDurationMinutes
                  ? `${selected.completionDurationMinutes} 分钟`
                  : "未记录"}
              </dd>
            </div>
          </dl>
        </section>
      </aside>
      {completionTask && (
        <CompletionDialog
          task={completionTask}
          onClose={() => setCompletionTask(null)}
          onComplete={(duration) => void complete(duration)}
        />
      )}
      {undoAction && (
        <div className="undo-toast" role="status">
          <span>{undoAction.message}</span>
          <button
            onClick={() => {
              const action = undoAction;
              setUndoAction(null);
              void action.run().catch(reportError);
            }}
          >
            <ArrowCounterClockwise size={16} />
            撤销
          </button>
          <IconButton label="关闭撤销提示" onClick={() => setUndoAction(null)}>
            <X size={15} />
          </IconButton>
        </div>
      )}
    </div>
  );
}
