import jsQR from "jsqr";
import { AlertTriangle, Camera, CameraOff, CheckCircle2, CloudOff, RefreshCw, ScanLine, Wifi, XCircle } from "lucide-react";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "../../api";
import { OutcomeBadge } from "../../components/Badges";
import { Failed, Loading } from "../../components/States";
import { useOrg } from "../../context";
import { outcomeLabel, plural, time } from "../../format";
import { useOnline } from "../../hooks";
import { admittedHere, GateStore, isConflict, judgeOffline, parseCode, pending, type GateState, type QueuedScan } from "../../gate";
import { linkProps } from "../../router";

/** A random id for a scan; crypto.randomUUID is missing outside HTTPS, getRandomValues is not. */
function uuid(): string {
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6]! & 0x0f) | 0x40;
  b[8] = (b[8]! & 0x3f) | 0x80;
  const h = Array.from(b, (x) => x.toString(16).padStart(2, "0")).join("");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

function setting(key: string, fallback: () => string): string {
  try {
    const value = localStorage.getItem(key);
    if (value) return value;
    const made = fallback();
    localStorage.setItem(key, made);
    return made;
  } catch {
    return fallback();
  }
}

function saveSetting(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Not kept across reloads; fine for one shift.
  }
}

/** Reads QR codes from the camera with jsQR, a few times a second. */
function useCameraScanner(onCode: (code: string) => void) {
  const video = useRef<HTMLVideoElement>(null);
  const [on, setOn] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const handler = useRef(onCode);
  handler.current = onCode;

  useEffect(() => {
    if (!on) return;
    let stream: MediaStream | null = null;
    let frame = 0;
    let stopped = false;
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d", { willReadFrequently: true });

    const tick = () => {
      const v = video.current;
      if (stopped || !v || !context) return;
      if (v.readyState === v.HAVE_ENOUGH_DATA) {
        canvas.width = v.videoWidth;
        canvas.height = v.videoHeight;
        context.drawImage(v, 0, 0);
        const found = jsQR(context.getImageData(0, 0, canvas.width, canvas.height).data, canvas.width, canvas.height, { inversionAttempts: "dontInvert" });
        if (found?.data) handler.current(found.data);
      }
      frame = window.setTimeout(() => requestAnimationFrame(tick), 150);
    };

    if (!navigator.mediaDevices?.getUserMedia) {
      setError("This browser cannot use the camera here. Cameras need HTTPS, or type the code below.");
      setOn(false);
      return;
    }

    navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" }, audio: false }).then((s) => {
      stream = s;
      if (video.current) {
        video.current.srcObject = s;
        void video.current.play();
      }
      tick();
    }, () => {
      setError("The camera is not available. Allow it in the browser, or type the code below.");
      setOn(false);
    });

    return () => {
      stopped = true;
      clearTimeout(frame);
      stream?.getTracks().forEach((t) => t.stop());
    };
  }, [on]);

  return { video, on, setOn: (value: boolean) => { setError(null); setOn(value); }, error };
}

function Result({ scan }: { scan: QueuedScan }) {
  const outcome = scan.server?.outcome ?? scan.local;
  const tone = outcome === "Admitted" ? "ok" : outcome === "AlreadyUsed" ? "warn" : "bad";
  const first = scan.server?.firstGate ? `${scan.server.firstGate}, ${time(scan.server.firstCheckedInAt!)}` : null;
  return (
    <div className={`scan-result ${tone}`} role="status" aria-live="assertive">
      {tone === "ok" ? <CheckCircle2 size={44} /> : tone === "warn" ? <AlertTriangle size={44} /> : <XCircle size={44} />}
      <div>
        <strong className="scan-outcome">{outcomeLabel[outcome]}</strong>
        {scan.label && <span className="block">{scan.label}</span>}
        {outcome === "AlreadyUsed" && first && <span className="block small">First used at {first}</span>}
        {!scan.server && <span className="block small"><CloudOff size={13} /> Checked on this device; it syncs when the connection is back.</span>}
      </div>
    </div>
  );
}

export function Gate({ eventId }: { eventId: string }) {
  const org = useOrg();
  const online = useOnline();
  const store = useRef(new GateStore(eventId)).current;
  const [state, setState] = useState<GateState>(() => store.load());
  const [loadError, setLoadError] = useState<string | null>(null);
  const [gate, setGate] = useState(() => setting("gise.gate.name", () => "Main gate"));
  const [device] = useState(() => setting("gise.gate.device", () => `device-${uuid().slice(0, 6)}`));
  const [manual, setManual] = useState("");
  const [last, setLast] = useState<QueuedScan | null>(null);
  const [syncing, setSyncing] = useState(false);
  const recent = useRef(new Map<string, number>());

  const update = useCallback((change: (s: GateState) => GateState) => {
    setState((s) => {
      const next = change(s);
      store.save(next);
      return next;
    });
  }, [store]);

  // The package: fetched whenever there is a connection, kept for when there is not.
  const refreshPackage = useCallback(async () => {
    try {
      const pack = await api.gate(org.id, eventId);
      update((s) => ({ ...s, pack }));
      setLoadError(null);
    } catch (e) {
      if (!(e as ApiError).offline) setLoadError((e as Error).message);
    }
  }, [org.id, eventId, update]);
  useEffect(() => { void refreshPackage(); }, [refreshPackage]);

  const sync = useCallback(async () => {
    const waiting = pending(store.load().scans);
    if (waiting.length === 0) return;
    setSyncing(true);
    try {
      for (let i = 0; i < waiting.length; i += 200) {
        const batch = waiting.slice(i, i + 200);
        const results = await api.scan(org.id, eventId, batch.map(({ scanId, code, gate, device, scannedAt }) => ({ scanId, code, gate, device, scannedAt })));
        const byId = new Map(results.map((r) => [r.scanId, r]));
        update((s) => ({ ...s, scans: s.scans.map((x) => (byId.has(x.scanId) ? { ...x, server: byId.get(x.scanId)! } : x)) }));
      }
      await refreshPackage();
    } catch {
      // Still offline: the queue waits for the next try.
    } finally {
      setSyncing(false);
    }
  }, [org.id, eventId, store, update, refreshPackage]);

  useEffect(() => {
    if (!online) return;
    void sync();
    const timer = setInterval(sync, 15_000);
    return () => clearInterval(timer);
  }, [online, sync]);

  const handle = useCallback(async (raw: string) => {
    const code = raw.trim();
    if (!code) return;
    // The camera sees the same code many times a second; one scan per code every few seconds.
    const seen = recent.current.get(code);
    if (seen && Date.now() - seen < 4000) return;
    recent.current.set(code, Date.now());

    const current = store.load();
    const label = current.pack?.tickets.find((t) => t.id === parseCode(code)?.ticketId)?.label ?? null;
    const base = { scanId: uuid(), code, gate, device, scannedAt: new Date().toISOString(), label };
    let scan: QueuedScan;
    try {
      if (!navigator.onLine) throw new ApiError("offline", 0, null);
      const [result] = await api.scan(org.id, eventId, [base]);
      scan = { ...base, ticketId: result!.ticket?.id ?? null, label: result!.ticket?.label ?? label, local: result!.outcome, server: result! };
    } catch (e) {
      if (!(e as ApiError).offline || !current.pack) {
        setLoadError((e as Error).message);
        return;
      }
      const verdict = await judgeOffline(code, current.pack, admittedHere(current.scans));
      scan = { ...base, ticketId: verdict.ticketId, local: verdict.outcome, server: null };
    }

    update((s) => ({ ...s, scans: [...s.scans, scan] }));
    setLast(scan);
    navigator.vibrate?.(scan.local === "Admitted" ? 60 : [80, 60, 80]);
  }, [org.id, eventId, gate, device, store, update]);

  const camera = useCameraScanner((code) => void handle(code));

  function submit(e: FormEvent) {
    e.preventDefault();
    recent.current.delete(manual.trim());
    void handle(manual);
    setManual("");
  }

  if (!state.pack && loadError) return <Failed message={loadError} onRetry={refreshPackage} />;
  if (!state.pack) return <Loading label="Downloading the ticket list" />;

  const mine = state.scans.filter((s) => (s.server?.outcome ?? s.local) === "Admitted").length;
  const waiting = pending(state.scans).length;
  const conflicts = state.scans.filter(isConflict);

  return (
    <div className="gate">
      <a className="back" {...linkProps(`/manage/${org.id}/events/${eventId}`)}>← Event</a>
      <div className="page-head">
        <div>
          <h1>{state.pack.title}</h1>
          <p className="muted small">{state.pack.venueName} · {time(state.pack.startsAt)} · list of {plural(state.pack.tickets.length, "ticket")} from {time(state.pack.generatedAt)}</p>
        </div>
        <span className={`badge ${online ? "ok" : "info"}`}>{online ? <><Wifi size={13} />Online</> : <><CloudOff size={13} />Offline</>}</span>
      </div>

      <div className="gate-layout">
        <section className="card section">
          <label className="field">This gate<input className="input" value={gate} maxLength={30} onChange={(e) => { setGate(e.target.value); saveSetting("gise.gate.name", e.target.value); }} /></label>
          <div className={`camera${camera.on ? " on" : ""}`}>
            <video ref={camera.video} muted playsInline aria-label="Camera" />
            {!camera.on && <ScanLine size={40} className="muted" />}
          </div>
          <button className="button wide" onClick={() => camera.setOn(!camera.on)}>{camera.on ? <><CameraOff size={16} />Stop camera</> : <><Camera size={16} />Scan with the camera</>}</button>
          {camera.error && <p className="form-error" role="alert">{camera.error}</p>}
          <form className="row gap-6" onSubmit={submit}>
            <label className="grow"><span className="sr-only">Ticket code</span>
              <input className="input mono-input" value={manual} onChange={(e) => setManual(e.target.value)} placeholder="Or type / paste a code (GS1.…)" /></label>
            <button className="button primary">Check</button>
          </form>
        </section>

        <section className="stack">
          {last ? <Result scan={last} /> : <div className="scan-result idle"><ScanLine size={40} /><span>Point the camera at a ticket</span></div>}
          <div className="stats">
            <div className="stat card"><span className="small muted">Let in here</span><strong>{mine}</strong></div>
            <div className="stat card"><span className="small muted">Waiting to sync</span><strong>{waiting}</strong>
              <button className="link small" onClick={() => void sync()} disabled={!online || syncing || waiting === 0}><RefreshCw size={12} />{syncing ? "Syncing…" : "Sync now"}</button></div>
          </div>
          {conflicts.length > 0 && (
            <div className="notice warn" role="alert">
              <AlertTriangle size={18} />
              <span>{plural(conflicts.length, "ticket")} let in here while offline had been used elsewhere first: {conflicts.map((c) => `${c.label ?? "ticket"} (${c.server!.firstGate})`).join(", ")}.</span>
            </div>
          )}
          <section className="card">
            <h2 className="list-title">Last scans</h2>
            <ul className="scan-list">
              {state.scans.slice(-8).reverse().map((s) => (
                <li key={s.scanId}>
                  <OutcomeBadge outcome={s.server?.outcome ?? s.local} />
                  <span className="grow ellipsis">{s.label ?? "Unknown code"}</span>
                  <span className="small muted">{time(s.scannedAt)}{s.server ? "" : " · not synced"}</span>
                </li>
              ))}
              {state.scans.length === 0 && <li className="muted small">Nothing scanned yet.</li>}
            </ul>
          </section>
        </section>
      </div>
    </div>
  );
}
