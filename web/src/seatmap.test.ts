import { describe, expect, it } from "vitest";
import { count, emptySelection, holdRequest, isFree, seatKey, setStanding, toggleSeat, total, withoutTaken } from "./seatmap";
import type { Availability, EventDetail } from "./types";

const event = {
  categories: [{ id: "front", name: "Ön", price: 750 }, { id: "floor", name: "Ayakta", price: 300.5 }],
  sections: [
    { key: "A", name: "Parter", kind: "Seated", categoryId: "front", places: 4, rows: [{ label: "A", seats: 4 }], capacity: 0 },
    { key: "floor", name: "Ayakta", kind: "Standing", categoryId: "floor", places: 10, rows: [], capacity: 10 },
  ],
} as unknown as EventDetail;

const availability = {
  sections: [{ key: "A", kind: "Seated", available: 3, places: 4, rows: [{ label: "A", seats: "1101" }] }, { key: "floor", kind: "Standing", available: 10, places: 10, rows: [] }],
} as unknown as Availability;

describe("seat selection", () => {
  it("reads free seats from the availability strings", () => {
    expect(isFree(availability, "A", "A", 1)).toBe(true);
    expect(isFree(availability, "A", "A", 3)).toBe(false);
    expect(isFree(availability, "A", "Z", 1)).toBe(false);
  });

  it("toggles seats within the buyer's limit", () => {
    let selection = toggleSeat(emptySelection, seatKey("A", "A", 1), 2);
    selection = toggleSeat(selection, seatKey("A", "A", 2), 2);
    expect(toggleSeat(selection, seatKey("A", "A", 4), 2)).toBe(selection);
    expect(toggleSeat(selection, seatKey("A", "A", 1), 2).seats).toEqual(["A|A|2"]);
  });

  it("clamps standing quantities to what is free and to the limit", () => {
    const one = toggleSeat(emptySelection, seatKey("A", "A", 1), 4);
    expect(setStanding(one, "floor", 9, 10, 4).standing).toEqual({ floor: 3 });
    expect(setStanding(one, "floor", 5, 2, 4).standing).toEqual({ floor: 2 });
    expect(setStanding(setStanding(one, "floor", 2, 10, 4), "floor", 0, 10, 4).standing).toEqual({});
  });

  it("adds up the price and builds the request", () => {
    const selection = setStanding(toggleSeat(emptySelection, seatKey("A", "A", 2), 6), "floor", 2, 10, 6);
    expect(count(selection)).toBe(3);
    expect(total(event, selection)).toBe(1351);
    expect(holdRequest(selection)).toEqual({ seats: [{ section: "A", row: "A", number: 2 }], standing: [{ section: "floor", quantity: 2 }] });
  });

  it("drops seats someone else took meanwhile", () => {
    const selection = toggleSeat(toggleSeat(emptySelection, seatKey("A", "A", 1), 6), seatKey("A", "A", 3), 6);
    expect(withoutTaken(selection, availability).seats).toEqual(["A|A|1"]);
  });
});
