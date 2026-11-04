import { webcrypto } from "node:crypto";
import { beforeAll, describe, expect, it } from "vitest";
import { admittedHere, GateStore, isConflict, judgeOffline, parseCode, type QueuedScan } from "./gate";
import type { GatePackage } from "./types";

const subtle = webcrypto.subtle as unknown as SubtleCrypto;
const eventId = "01a1a6d2-0000-7000-8000-000000000001";
const ticketId = "01a1a6d2-0000-7000-8000-0000000000aa";
let keys: CryptoKeyPair;
let pack: GatePackage;

const b64url = (bytes: Uint8Array) => Buffer.from(bytes).toString("base64url");
const guidBytes = (id: string) => Uint8Array.from(Buffer.from(id.replace(/-/g, ""), "hex"));

async function code(ticket: string, event: string, key: CryptoKey = keys.privateKey, keyId = pack.keyId) {
  const payload = new Uint8Array(37);
  payload[0] = 1;
  payload.set(Buffer.from(keyId, "hex"), 1);
  payload.set(guidBytes(ticket), 5);
  payload.set(guidBytes(event), 21);
  const signature = new Uint8Array(await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key, payload));
  return `GS1.${b64url(payload)}.${b64url(signature)}`;
}

beforeAll(async () => {
  keys = (await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"])) as CryptoKeyPair;
  const spki = new Uint8Array(await subtle.exportKey("spki", keys.publicKey));
  const digest = new Uint8Array(await subtle.digest("SHA-256", spki));
  pack = {
    eventId, title: "Akustik Gece", startsAt: "2026-12-05T17:00:00Z", venueName: "Kültür Merkezi", generatedAt: "2026-12-05T15:00:00Z",
    keyId: Buffer.from(digest.subarray(0, 4)).toString("hex"), publicKey: Buffer.from(spki).toString("base64"),
    tickets: [
      { id: ticketId, label: "Parter · Row C · Seat 4", category: "Ön", holder: "Ayşe", status: "Valid", checkedInAt: null, gate: null },
      { id: "01a1a6d2-0000-7000-8000-0000000000bb", label: "Ayakta", category: "Ayakta", holder: "Can", status: "Void", checkedInAt: null, gate: null },
    ],
  };
});

describe("ticket codes at the gate", () => {
  it("reads the ticket and event out of a code", async () => {
    const parsed = parseCode(await code(ticketId, eventId));
    expect(parsed?.ticketId).toBe(ticketId);
    expect(parsed?.eventId).toBe(eventId);
    expect(parseCode("https://example.com")).toBeNull();
    expect(parseCode("GS1.abc.def")).toBeNull();
  });

  it("lets a signed ticket in once, offline", async () => {
    const valid = await code(ticketId, eventId);
    const first = await judgeOffline(valid, pack, new Set(), subtle);
    const again = await judgeOffline(valid, pack, new Set([ticketId]), subtle);
    expect([first.outcome, first.checkedBy]).toEqual(["Admitted", "signature"]);
    expect(again.outcome).toBe("AlreadyUsed");
  });

  it("admits tickets bought after the list was downloaded, on their signature", async () => {
    const fresh = await code("01a1a6d2-0000-7000-8000-0000000000cc", eventId);
    expect((await judgeOffline(fresh, pack, new Set(), subtle)).outcome).toBe("Admitted");
  });

  it("turns away forged, refunded and other events' tickets", async () => {
    const other = (await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"])) as CryptoKeyPair;
    const forged = await code(ticketId, eventId, other.privateKey);
    const refunded = await code("01a1a6d2-0000-7000-8000-0000000000bb", eventId);
    const elsewhere = await code(ticketId, "01a1a6d2-0000-7000-8000-000000000002");
    expect((await judgeOffline(forged, pack, new Set(), subtle)).outcome).toBe("Invalid");
    expect((await judgeOffline(refunded, pack, new Set(), subtle)).outcome).toBe("Void");
    expect((await judgeOffline(elsewhere, pack, new Set(), subtle)).outcome).toBe("WrongEvent");
  });

  it("falls back to the list where the browser cannot check signatures", async () => {
    const listed = await code(ticketId, eventId);
    const unlisted = await code("01a1a6d2-0000-7000-8000-0000000000dd", eventId);
    const trusted = await judgeOffline(listed, pack, new Set(), null);
    expect([trusted.outcome, trusted.checkedBy]).toEqual(["Admitted", "list"]);
    expect((await judgeOffline(unlisted, pack, new Set(), null)).outcome).toBe("Invalid");
  });
});

describe("the device's scan log", () => {
  const scan = (over: Partial<QueuedScan>): QueuedScan => ({
    scanId: crypto.randomUUID(), code: "x", gate: "Kuzey", device: "tablet", scannedAt: "2026-12-05T17:00:00Z", ticketId, label: null, local: "Admitted", server: null, ...over,
  });

  it("knows which tickets it let in and which the server overruled", () => {
    const overruled = scan({ server: { scanId: "s", outcome: "AlreadyUsed", ticket: null, scannedAt: "", firstCheckedInAt: null, firstGate: "Güney" } });
    expect(admittedHere([scan({})])).toEqual(new Set([ticketId]));
    expect(admittedHere([overruled]).size).toBe(0);
    expect(isConflict(overruled)).toBe(true);
  });

  it("survives a reload", () => {
    const memory = new Map<string, string>();
    const storage = { getItem: (k: string) => memory.get(k) ?? null, setItem: (k: string, v: string) => void memory.set(k, v) } as unknown as Storage;
    new GateStore(eventId, storage).save({ pack, scans: [scan({})] });
    expect(new GateStore(eventId, storage).load().scans).toHaveLength(1);
    expect(new GateStore("other", storage).load()).toEqual({ pack: null, scans: [] });
  });
});
