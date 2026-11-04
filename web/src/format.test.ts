import { describe, expect, it } from "vitest";
import { clock, fromLocalInput, initials, plural, price, seatName, shortPrice, toLocalInput, wait } from "./format";

describe("format", () => {
  it("writes prices the Turkish way", () => {
    expect(price(1250)).toBe("1.250,00 ₺");
    expect(price(450.5)).toBe("450,50 ₺");
    expect(shortPrice(300)).toBe("300 ₺");
    expect(shortPrice(450.5)).toBe("450,50 ₺");
  });

  it("counts down without going negative", () => {
    expect(clock(462_000)).toBe("7:42");
    expect(clock(4_100)).toBe("0:05");
    expect(clock(-3_000)).toBe("0:00");
  });

  it("says how long the wait is in words", () => {
    expect(wait(20)).toBe("under a minute");
    expect(wait(60)).toBe("about 1 minute");
    expect(wait(400)).toBe("about 7 minutes");
    expect(wait(7_200)).toBe("about 2 hours");
  });

  it("names seats as people say them", () => {
    expect(seatName("Parter", "C", 12)).toBe("Parter C12");
    expect(seatName("Ayakta", null, 40)).toBe("Ayakta");
  });

  it("round-trips datetime-local values", () => {
    const iso = "2026-12-05T17:00:00.000Z";
    expect(fromLocalInput(toLocalInput(iso))).toBe(iso);
  });

  it("keeps Turkish capitals in initials and counts things", () => {
    expect(initials("ilker şahin")).toBe("İŞ");
    expect(plural(1, "ticket")).toBe("1 ticket");
    expect(plural(1200, "ticket")).toBe("1,200 tickets");
  });
});
