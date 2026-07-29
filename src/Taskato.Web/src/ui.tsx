import { useEffect, type ReactNode } from "react";
import {
  CalendarBlank,
  ClockCounterClockwise,
  GearSix,
  ListChecks,
} from "@phosphor-icons/react";
import { bridge, type SettingsDto } from "./bridge";

export type PageId = "tasks" | "summary" | "history" | "settings";

export const navItems = [
  { id: "tasks" as const, label: "任务", icon: ListChecks },
  { id: "summary" as const, label: "每日总结", icon: CalendarBlank },
  { id: "history" as const, label: "历史记录", icon: ClockCounterClockwise },
  { id: "settings" as const, label: "设置", icon: GearSix },
];

export function toLocalDateInput(date = new Date()): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function applyTheme(
  settings: Pick<SettingsDto, "themePrimaryColor" | "themeSecondaryColor">,
): void {
  document.documentElement.style.setProperty(
    "--primary",
    settings.themePrimaryColor,
  );
  document.documentElement.style.setProperty(
    "--secondary",
    settings.themeSecondaryColor,
  );
}

export function IconButton({
  label,
  children,
  onClick,
  className = "",
  disabled = false,
}: {
  label: string;
  children: ReactNode;
  onClick?: () => void;
  className?: string;
  disabled?: boolean;
}) {
  return (
    <button
      className={`icon-button ${className}`}
      aria-label={label}
      title={label}
      onClick={onClick}
      disabled={disabled}
    >
      {children}
    </button>
  );
}

export function useBridgeRefresh(
  eventName: string,
  refresh: () => void,
): void {
  useEffect(() => bridge.subscribe(eventName, refresh), [eventName, refresh]);
}
