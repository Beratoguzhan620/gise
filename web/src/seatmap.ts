import type { Availability, Category, EventDetail } from "./types";

export interface Selection {
  /** Seat keys: "section|row|number". */
  seats: string[];
  /** Spots asked for in each standing area. */
  standing: Record<string, number>;
}

export const emptySelection: Selection = { seats: [], standing: {} };

export const seatKey = (section: string, row: string, number: number) => `${section}|${row}|${number}`;

export function parseSeat(key: string): { section: string; row: string; number: number } {
  const [section, row, number] = key.split("|");
  return { section: section!, row: row!, number: Number(number) };
}

export const count = (selection: Selection) => selection.seats.length + Object.values(selection.standing).reduce((sum, n) => sum + n, 0);

/** Whether a seat is free on the last seat map read: rows are strings with a "1" for each free seat. */
export function isFree(availability: Availability | undefined, section: string, row: string, number: number): boolean {
  const seats = availability?.sections.find((s) => s.key === section)?.rows.find((r) => r.label === row)?.seats;
  return seats?.[number - 1] === "1";
}

/** Adds or removes a seat, keeping within the buyer's limit. Returns the selection unchanged when the limit is reached. */
export function toggleSeat(selection: Selection, key: string, limit: number): Selection {
  if (selection.seats.includes(key)) return { ...selection, seats: selection.seats.filter((s) => s !== key) };
  if (count(selection) >= limit) return selection;
  return { ...selection, seats: [...selection.seats, key] };
}

/** Sets a standing quantity, clamped to what is free and to the buyer's limit. */
export function setStanding(selection: Selection, section: string, quantity: number, free: number, limit: number): Selection {
  const others = count(selection) - (selection.standing[section] ?? 0);
  const clamped = Math.max(0, Math.min(quantity, free, limit - others));
  const standing = { ...selection.standing, [section]: clamped };
  if (clamped === 0) delete standing[section];
  return { ...selection, standing };
}

/** Drops seats that were taken since they were chosen. */
export function withoutTaken(selection: Selection, availability: Availability): Selection {
  const seats = selection.seats.filter((key) => {
    const { section, row, number } = parseSeat(key);
    return isFree(availability, section, row, number);
  });
  return seats.length === selection.seats.length ? selection : { ...selection, seats };
}

export function categoryOf(event: EventDetail, section: string): Category | undefined {
  const id = event.sections.find((s) => s.key === section)?.categoryId;
  return event.categories.find((c) => c.id === id);
}

export function total(event: EventDetail, selection: Selection): number {
  const seats = selection.seats.reduce((sum, key) => sum + (categoryOf(event, parseSeat(key).section)?.price ?? 0), 0);
  const standing = Object.entries(selection.standing).reduce((sum, [section, n]) => sum + n * (categoryOf(event, section)?.price ?? 0), 0);
  return Math.round((seats + standing) * 100) / 100;
}

export function holdRequest(selection: Selection) {
  return {
    seats: selection.seats.map(parseSeat),
    standing: Object.entries(selection.standing).map(([section, quantity]) => ({ section, quantity })),
  };
}
