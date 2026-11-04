import type { CheckInOutcome, GatePackage, ScanResult } from "./types";

/** What a ticket code says, read without the server: see the gate package's description in the API reference. */
export interface ParsedCode {
  keyId: string;
  ticketId: string;
  eventId: string;
  payload: Uint8Array<ArrayBuffer>;
  signature: Uint8Array<ArrayBuffer>;
}

const PREFIX = "GS1.";

function fromBase64Url(value: string): Uint8Array<ArrayBuffer> {
  if (!/^[A-Za-z0-9_-]*$/.test(value)) throw new Error("not base64url");
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

function hex(bytes: Uint8Array): string {
  return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
}

/** A GUID from 16 big-endian bytes, as .NET writes them with bigEndian: true. */
function guid(bytes: Uint8Array): string {
  const h = hex(bytes);
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

export function parseCode(code: string): ParsedCode | null {
  const text = code.trim();
  if (!text.startsWith(PREFIX)) return null;
  const parts = text.slice(PREFIX.length).split(".");
  if (parts.length !== 2) return null;
  try {
    const payload = fromBase64Url(parts[0]!);
    const signature = fromBase64Url(parts[1]!);
    if (payload.length !== 37 || payload[0] !== 1 || signature.length !== 64) return null;
    return { keyId: hex(payload.subarray(1, 5)), ticketId: guid(payload.subarray(5, 21)), eventId: guid(payload.subarray(21, 37)), payload, signature };
  } catch {
    return null;
  }
}

/** Checks the signature with the package's public key. Browsers only offer this on HTTPS or localhost; elsewhere it answers null. */
export async function verifySignature(parsed: ParsedCode, publicKeyBase64: string, subtle: SubtleCrypto | null = globalThis.crypto?.subtle ?? null): Promise<boolean | null> {
  if (!subtle) return null;
  const key = await subtle.importKey("spki", fromBase64Url(publicKeyBase64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "")), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
  return subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, parsed.signature, parsed.payload);
}

export interface OfflineVerdict {
  outcome: CheckInOutcome;
  ticketId: string | null;
  /** How the code was trusted: by its signature, or (where the browser cannot check signatures) by being on the list. */
  checkedBy: "signature" | "list" | null;
}

/**
 * What the gate decides without a connection. A signed code for this event gets in once; a code the device saw
 * before, or one already used when the list was downloaded, is a repeat; a refunded ticket is turned away.
 */
export async function judgeOffline(
  code: string, pack: GatePackage, admittedHere: ReadonlySet<string>, subtle: SubtleCrypto | null = globalThis.crypto?.subtle ?? null,
): Promise<OfflineVerdict> {
  const parsed = parseCode(code);
  if (!parsed || parsed.keyId !== pack.keyId) return { outcome: "Invalid", ticketId: null, checkedBy: null };
  if (parsed.eventId !== pack.eventId) return { outcome: "WrongEvent", ticketId: parsed.ticketId, checkedBy: null };

  const listed = pack.tickets.find((t) => t.id === parsed.ticketId);
  const signed = await verifySignature(parsed, pack.publicKey, subtle);
  if (signed === false || (signed === null && !listed)) return { outcome: "Invalid", ticketId: null, checkedBy: null };

  const checkedBy = signed ? "signature" : "list";
  if (listed?.status === "Void") return { outcome: "Void", ticketId: parsed.ticketId, checkedBy };
  if (listed?.checkedInAt || admittedHere.has(parsed.ticketId)) return { outcome: "AlreadyUsed", ticketId: parsed.ticketId, checkedBy };
  return { outcome: "Admitted", ticketId: parsed.ticketId, checkedBy };
}

/** A scan as the device keeps it until the server has seen it. */
export interface QueuedScan {
  scanId: string;
  code: string;
  gate: string;
  device: string;
  scannedAt: string;
  ticketId: string | null;
  label: string | null;
  local: CheckInOutcome;
  server: ScanResult | null;
}

export interface GateState {
  pack: GatePackage | null;
  scans: QueuedScan[];
}

/** The device's copy of the gate package and its scans, kept in this browser so a reload or lost connection loses nothing. */
export class GateStore {
  constructor(private readonly eventId: string, private readonly storage: Storage | null = safeStorage()) {}

  private get key() {
    return `gise.gate.${this.eventId}`;
  }

  load(): GateState {
    try {
      const raw = this.storage?.getItem(this.key);
      return raw ? (JSON.parse(raw) as GateState) : { pack: null, scans: [] };
    } catch {
      return { pack: null, scans: [] };
    }
  }

  save(state: GateState) {
    try {
      this.storage?.setItem(this.key, JSON.stringify({ ...state, scans: state.scans.slice(-2000) }));
    } catch {
      // Storage full or blocked: the scans still live in memory until the page closes.
    }
  }
}

function safeStorage(): Storage | null {
  try {
    return typeof localStorage === "undefined" ? null : localStorage;
  } catch {
    return null;
  }
}

export const pending = (scans: QueuedScan[]) => scans.filter((s) => s.server === null);

/** Tickets this device let in, whether or not the server knows yet. */
export function admittedHere(scans: QueuedScan[]): Set<string> {
  return new Set(scans.filter((s) => s.ticketId && (s.server ? s.server.outcome === "Admitted" : s.local === "Admitted")).map((s) => s.ticketId!));
}

/** A scan that got someone in offline but that the server says was a repeat: someone else used the ticket first. */
export const isConflict = (scan: QueuedScan) => scan.local === "Admitted" && scan.server !== null && scan.server.outcome !== "Admitted";
