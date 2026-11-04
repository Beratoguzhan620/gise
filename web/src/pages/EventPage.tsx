import { AlertCircle, CalendarDays, Hourglass, MapPin, ShieldCheck, Ticket, Undo2, Users, X } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "../api";
import { SaleBadge } from "../components/Badges";
import { Countdown } from "../components/Countdown";
import { Legend, SeatMap } from "../components/SeatMap";
import { Failed, Loading } from "../components/States";
import { useApp } from "../context";
import { longDate, plural, price, seatName, shortPrice, time, wait, when } from "../format";
import { useLoad } from "../hooks";
import { navigate, signInHere } from "../router";
import { count, emptySelection, holdRequest, parseSeat, setStanding, toggleSeat, total, withoutTaken, type Selection } from "../seatmap";
import type { Availability, EventDetail, Hold, QueueStatus } from "../types";

const holdKey = (eventId: string) => `gise.hold.${eventId}`;

function rememberHold(eventId: string, holdId: string | null) {
  try {
    if (holdId) sessionStorage.setItem(holdKey(eventId), holdId);
    else sessionStorage.removeItem(holdKey(eventId));
  } catch {
    // Without storage a reload forgets the basket; the seats come back when the hold runs out.
  }
}

function recallHold(eventId: string): string | null {
  try {
    return sessionStorage.getItem(holdKey(eventId));
  } catch {
    return null;
  }
}

function WaitingRoom({ status, event }: { status: QueueStatus; event: EventDetail }) {
  const before = new Date(event.saleStartsAt).getTime() > Date.now();
  return (
    <div className="queue" role="status" aria-live="polite">
      <Hourglass size={28} className="accent" />
      <h2>You're in line</h2>
      <p className="queue-number">Number <strong>{status.position?.toLocaleString("en-GB")}</strong></p>
      <p className="muted measure">
        {before
          ? `Sales open ${when(event.saleStartsAt)}. Everyone who joins before then gets a random place in line, so there is nothing to gain by refreshing.`
          : `${plural(status.ahead ?? 0, "person", "people")} ahead of you. Your turn comes in ${wait(status.estimatedWaitSeconds ?? 0)}.`}
      </p>
      <p className="small muted">Keep this page open; it moves on by itself. The line lets in {status.admitPerMinute.toLocaleString("en-GB")} people a minute.</p>
    </div>
  );
}

function Basket({ event, selection, hold, busy, error, onHold, onCheckout, onRelease, onClear, onExpired }: {
  event: EventDetail; selection: Selection; hold: Hold | null; busy: boolean; error: string | null;
  onHold: () => void; onCheckout: () => void; onRelease: () => void; onClear: () => void; onExpired: () => void;
}) {
  if (hold) {
    return (
      <aside className="basket card" aria-label="Your basket">
        <div className="basket-head">
          <h2>Your places</h2>
          <Countdown until={hold.expiresAt} label="Held for" onEnd={onExpired} />
        </div>
        <ul className="lines">
          {hold.lines.map((l) => (
            <li key={l.placeId}><span>{seatName(l.sectionName, l.row, l.number)}<span className="small muted block">{l.category}</span></span><strong>{price(l.price)}</strong></li>
          ))}
        </ul>
        <div className="basket-total"><span>Total</span><strong>{price(hold.total)}</strong></div>
        {error && <p className="form-error" role="alert">{error}</p>}
        <button className="button primary wide" onClick={onCheckout} disabled={busy}>{busy ? "Opening the payment page…" : `Pay ${price(hold.total)}`}</button>
        <button className="button wide" onClick={onRelease} disabled={busy}><Undo2 size={15} />Choose again</button>
        <p className="small muted row gap-6"><ShieldCheck size={14} />Nobody else can take these while you pay.</p>
      </aside>
    );
  }

  const n = count(selection);
  return (
    <aside className="basket card" aria-label="Your basket">
      <div className="basket-head">
        <h2>{n === 0 ? "Choose places" : plural(n, "place")}</h2>
        {n > 0 && <button className="link small" onClick={onClear}>Clear</button>}
      </div>
      {n === 0 ? (
        <p className="muted small">Tap seats on the map, or pick how many standing places you want. Up to {event.maxTicketsPerBuyer} per person.</p>
      ) : (
        <ul className="lines">
          {selection.seats.map((key) => {
            const seat = parseSeat(key);
            const section = event.sections.find((s) => s.key === seat.section);
            const category = event.categories.find((c) => c.id === section?.categoryId);
            return <li key={key}><span>{seatName(section?.name ?? seat.section, seat.row, seat.number)}</span><strong>{shortPrice(category?.price ?? 0)}</strong></li>;
          })}
          {Object.entries(selection.standing).map(([key, quantity]) => {
            const section = event.sections.find((s) => s.key === key);
            const category = event.categories.find((c) => c.id === section?.categoryId);
            return <li key={key}><span>{quantity} × {section?.name}</span><strong>{shortPrice((category?.price ?? 0) * quantity)}</strong></li>;
          })}
        </ul>
      )}
      {n > 0 && <div className="basket-total"><span>Total</span><strong>{price(total(event, selection))}</strong></div>}
      {error && <p className="form-error" role="alert">{error}</p>}
      <button className="button primary wide" onClick={onHold} disabled={busy || n === 0}>{busy ? "Setting them aside…" : n === 0 ? "Hold places" : `Hold ${plural(n, "place")}`}</button>
    </aside>
  );
}

export function EventPage({ eventId }: { eventId: string }) {
  const { account } = useApp();
  const [loaded, reload] = useLoad(() => api.event(eventId), [eventId]);
  const [availability, setAvailability] = useState<Availability>();
  const [queue, setQueue] = useState<QueueStatus | null>(null);
  const [selection, setSelection] = useState<Selection>(emptySelection);
  const [hold, setHold] = useState<Hold | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const event = loaded.data;
  const selling = event?.saleState === "OnSale";

  const readAvailability = useCallback(async () => {
    try {
      const fresh = await api.availability(eventId);
      setAvailability(fresh);
      setSelection((s) => withoutTaken(s, fresh));
    } catch {
      // The map keeps its last state; the next poll tries again.
    }
  }, [eventId]);

  // The seat map refreshes every few seconds while the sale is on.
  useEffect(() => {
    if (!event) return;
    void readAvailability();
    if (event.saleState !== "OnSale") return;
    const timer = setInterval(readAvailability, 5000);
    return () => clearInterval(timer);
  }, [event, readAvailability]);

  // A basket from before a reload is picked up again.
  useEffect(() => {
    const id = account ? recallHold(eventId) : null;
    if (!id) return;
    api.getHold(id).then((h) => setHold(h.status === "Active" ? h : null), () => rememberHold(eventId, null));
  }, [eventId, account]);

  // Busy sales: join the line on arrival and follow it until it is this buyer's turn.
  const inLine = !!event?.waitingRoom && !!account && (event.saleState === "OnSale" || event.saleState === "Upcoming");
  useEffect(() => {
    if (!inLine) return;
    let live = true;
    const join = () => api.joinQueue(eventId).then((s) => { if (live) setQueue(s); }, () => {});
    void join();
    const timer = setInterval(() => { if (!queue?.admitted) void join(); }, 5000);
    return () => { live = false; clearInterval(timer); };
  }, [inLine, eventId, queue?.admitted]);

  async function takePlaces() {
    if (!account) {
      navigate(signInHere());
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const created = await api.hold(eventId, holdRequest(selection));
      setHold(created);
      rememberHold(eventId, created.id);
      setSelection(emptySelection);
    } catch (e) {
      const err = e as ApiError;
      setError(err.message);
      if (err.code === "place_unavailable") void readAvailability();
      if (err.code === "queue_required") setQueue(await api.joinQueue(eventId).catch(() => null));
    } finally {
      setBusy(false);
    }
  }

  async function checkout() {
    if (!hold) return;
    setBusy(true);
    setError(null);
    try {
      const order = await api.checkout(hold.id);
      rememberHold(eventId, null);
      if (order.checkoutUrl) window.location.assign(order.checkoutUrl);
      else navigate(`/orders/${order.id}`);
    } catch (e) {
      const err = e as ApiError;
      setError(err.message);
      setBusy(false);
      if (err.code === "hold_expired") expired();
    }
  }

  async function release() {
    if (!hold) return;
    setBusy(true);
    await api.release(hold.id).catch(() => {});
    rememberHold(eventId, null);
    setHold(null);
    setBusy(false);
    void readAvailability();
  }

  function expired() {
    rememberHold(eventId, null);
    setHold(null);
    setError("Time ran out and the places went back on sale. Choose again.");
    void readAvailability();
  }

  if (loaded.state === "failed" && !event) return <Failed message={loaded.error} onRetry={reload} />;
  if (!event) return <Loading />;

  const waiting = inLine && queue !== null && !queue.admitted;
  const canChoose = selling && !hold && (!event.waitingRoom || (queue?.admitted ?? false));
  const notice =
    event.saleState === "Upcoming" && !event.waitingRoom ? `Sales open ${when(event.saleStartsAt)}.`
    : event.saleState === "SoldOut" ? "Every place is taken. Places that come back from unpaid baskets and refunds show up here."
    : event.saleState === "Cancelled" ? "This event was cancelled. Everyone who bought a ticket gets their money back."
    : event.saleState === "Ended" ? "This event has started; sales are closed."
    : event.waitingRoom && !account ? "This sale has a waiting room. Sign in to join the line."
    : null;

  return (
    <div className="event-page">
      <header className="event-head">
        <div className="row wrap gap-6">
          <SaleBadge state={event.saleState} />
          {event.waitingRoom && <span className="badge plain"><Users size={12} />Waiting room</span>}
        </div>
        <h1>{event.title}</h1>
        <p className="event-facts">
          <span className="row gap-6"><CalendarDays size={16} />{longDate(event.startsAt)}, {time(event.startsAt)}</span>
          <span className="row gap-6"><MapPin size={16} />{event.venueName}{event.address ? `, ${event.address}` : ""}, {event.city}</span>
          <span className="row gap-6"><Ticket size={16} />{event.organizer}</span>
        </p>
        {event.description && <p className="event-description">{event.description}</p>}
        <p className="small muted">
          {event.refundDeadline ? `Refunds until ${when(event.refundDeadline)}.` : "Refunds are handled by the organizer."} Up to {event.maxTicketsPerBuyer} tickets per person.
        </p>
      </header>

      {notice && <div className="notice" role="status"><AlertCircle size={18} />{notice}{event.waitingRoom && !account && <button className="button small primary" onClick={() => navigate(signInHere())}>Sign in</button>}</div>}

      <div className="buy-layout">
        <div className="map-card card">
          <div className="map-toolbar">
            <Legend event={event} />
            <span className="small muted">{plural(availability?.available ?? event.available, "place")} left of {event.capacity.toLocaleString("en-GB")}</span>
          </div>
          {waiting ? <WaitingRoom status={queue!} event={event} /> : (
            <>
              {queue?.admitted && queue.shopUntil && !hold && (
                <div className="notice ok" role="status"><Users size={18} />It's your turn. Choose your places; the line holds your spot until {time(queue.shopUntil)}.</div>
              )}
              <SeatMap
                event={event}
                availability={availability}
                selection={selection}
                limit={event.maxTicketsPerBuyer}
                disabled={!canChoose}
                onSeat={(key) => setSelection((s) => toggleSeat(s, key, event.maxTicketsPerBuyer))}
                onStanding={(section, quantity, free) => setSelection((s) => setStanding(s, section, quantity, free, event.maxTicketsPerBuyer))}
              />
            </>
          )}
        </div>
        {(selling || hold) && (
          <Basket
            event={event} selection={selection} hold={hold} busy={busy} error={error}
            onHold={takePlaces} onCheckout={checkout} onRelease={release} onClear={() => setSelection(emptySelection)} onExpired={expired}
          />
        )}
      </div>
      {hold && <span className="sr-only" role="status">Places held: {hold.lines.length}</span>}
      {error && !selling && !hold && <p className="form-error" role="alert"><X size={14} />{error}</p>}
    </div>
  );
}
