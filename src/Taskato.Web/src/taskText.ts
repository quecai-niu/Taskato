/** 内容完整保存，标题出口统一只显示第一行。 */
export function taskTitle(content: string): string {
  return content.split(/\r\n|\r|\n/, 1)[0]?.trim() || "未命名任务";
}

export function taskDetails(content: string): string {
  return content.split(/\r\n|\r|\n/).slice(1).join("\n").trim();
}

export function taskPath(path: string): string {
  return path.split(/ \/ | > /).map(taskTitle).join(" / ");
}

export function parentTaskPath(path: string): string {
  return path.split(/ \/ | > /).slice(0, -1).map(taskTitle).join(" / ");
}
