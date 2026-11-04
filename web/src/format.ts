import type { CheckInOutcome, OrderStatus, RefundReason, Role, SaleState } from "./types";

const money = new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** "1.250,00 ₺": Turkish grouping, as on a receipt. */
export function price(amount: number): string {
  return `${money.format(amount)} ₺`;
}

/** Whole lira when there are no kuruş: "450 ₺", "450,50 ₺". */
export function shortPrice(amount: number): string {
  return Number.isInteger(amount) ? `${new Intl.NumberFormat("tr-TR").format(amount)} ₺` : price(amount);
}

/** "Sat 5 Dec, 20:00", with the year when it is not this year. */
export function when(iso: string, now: Date = new Date()): string {
  const date = new Date(iso);
  const day = date.toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short", year: date.getFullYear() === now.getFullYear() ? undefined : "numeric" });
  return `${day}, ${time(iso)}`;
}

export function longDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { weekday: "long", day: "numeric", month: "long", year: "numeric" });
}

export function time(iso: string): string {
  return new Date(iso).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
}

/** For countdowns: "7:42", "0:05", never negative. */
export function clock(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}

/** "about 3 minutes", "under a minute". */
export function wait(seconds: number): string {
  if (seconds < 60) return "under a minute";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `about ${minutes} minute${minutes === 1 ? "" : "s"}`;
  const hours = Math.round(minutes / 60);
  return `about ${hours} hour${hours === 1 ? "" : "s"}`;
}

export function plural(count: number, one: string, many = `${one}s`): string {
  return `${new Intl.NumberFormat("en-GB").format(count)} ${count === 1 ? one : many}`;
}

/** For datetime-local inputs, which work in the browser's time zone. */
export function toLocalInput(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function fromLocalInput(value: string): string {
  return new Date(value).toISOString();
}

export const saleLabel: Record<SaleState, string> = {
  Draft: "Draft",
  Upcoming: "Sales open soon",
  OnSale: "On sale",
  SoldOut: "Sold out",
  Ended: "Ended",
  Cancelled: "Cancelled",
};

export const orderLabel: Record<OrderStatus, string> = {
  PendingPayment: "Waiting for payment",
  Paid: "Paid",
  Failed: "Payment declined",
  Expired: "Not paid in time",
  RefundPending: "Refund on its way",
  Refunded: "Refunded",
};

export const refundLabel: Record<RefundReason, string> = {
  Buyer: "Refunded at the buyer's request",
  Organizer: "Refunded by the organizer",
  EventCancelled: "The event was cancelled",
  LatePayment: "Paid after the places were gone",
};

export const roleHint: Record<Role, string> = {
  Staff: "Checks tickets at the gate",
  Manager: "Also runs venues, events, sales and refunds",
  Owner: "Also manages the team",
};

export const canManage = (role: Role) => role === "Manager" || role === "Owner";

export const outcomeLabel: Record<CheckInOutcome, string> = {
  Admitted: "Admitted",
  AlreadyUsed: "Already used",
  Void: "Refunded ticket",
  WrongEvent: "Another event's ticket",
  Invalid: "Not a valid ticket",
};

/** A seat's name as people say it: "Parter C12", or just the area for standing spots. */
export function seatName(sectionName: string, row: string | null, number: number): string {
  return row === null ? sectionName : `${sectionName} ${row}${number}`;
}

export function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]!.toLocaleUpperCase("tr")).join("");
}

/** Colours that tell price categories apart on the seat map, in the order categories are listed. */
export const categoryColors = ["#8E2F3C", "#2F5D80", "#5B6B2E", "#A3523A", "#17707A", "#7A5410", "#1F2A37", "#6B6862"];
