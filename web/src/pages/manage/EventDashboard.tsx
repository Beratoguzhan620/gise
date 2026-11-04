import { Ban, ExternalLink, Pencil, Rocket, RotateCcw, ScanLine, Search, Trash2 } from "lucide-react";
import { useState } from "react";
import { api } from "../../api";
import { Meter, OrderBadge, SaleBadge } from "../../components/Badges";
import { Dialog } from "../../components/Dialog";
import { Empty, Failed, Loading, SkeletonRows } from "../../components/States";
import { useOrg } from "../../context";
import { canManage, longDate, orderLabel, plural, price, time, when } from "../../format";
import { useDebounced, useLoad } from "../../hooks";
import { linkProps, navigate } from "../../router";
import type { OrderStatus, OrganizerEvent } from "../../types";
import { SalesChart } from "./SalesChart";

function Stat({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="stat card">
      <span className="small muted">{label}</span>
      <strong>{value}</strong>
      {note && <span className="small muted">{note}</span>}
    </div>
  );
}

function Sales({ event }: { event: OrganizerEvent }) {
  const org = useOrg();
  const [sales, reload] = useLoad(() => api.sales(org.id, event.id), [org.id, event.id]);
  if (sales.state === "failed" && !sales.data) return <Failed message={sales.error} onRetry={reload} />;
  if (!sales.data) return <Loading />;
  const s = sales.data;

  return (
    <>
      <div className="stats">
        <Stat label="Tickets sold" value={s.ticketsSold.toLocaleString("en-GB")} note={`of ${event.capacity.toLocaleString("en-GB")}`} />
        <Stat label="Net takings" value={price(s.net)} note={s.refunded > 0 ? `${price(s.refunded)} refunded` : undefined} />
        <Stat label="Paid orders" value={s.paidOrders.toLocaleString("en-GB")} note={s.pendingOrders > 0 ? `${s.pendingOrders} waiting for payment` : undefined} />
        <Stat label="In baskets now" value={event.inventory.held.toLocaleString("en-GB")} note="held while people pay" />
      </div>
      <div className="two-col">
        <section className="card section">
          <h3>By category</h3>
          <table className="table compact">
            <thead><tr><th>Category</th><th>Sold</th><th className="hide-sm">Takings</th></tr></thead>
            <tbody>
              {s.categories.map((c) => (
                <tr key={c.id}>
                  <td>{c.name}<span className="small muted block">{price(c.price)}</span></td>
                  <td className="sold-cell"><span className="small">{c.sold} of {c.capacity}</span><Meter value={c.sold} max={c.capacity} label={`${c.name}: sold`} /></td>
                  <td className="hide-sm">{price(c.revenue)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="card section"><h3>Sales</h3><SalesChart days={s.days} /></section>
      </div>
    </>
  );
}

function Orders({ event }: { event: OrganizerEvent }) {
  const org = useOrg();
  const [q, setQ] = useState("");
  const [status, setStatus] = useState<OrderStatus | "">("");
  const [page, setPage] = useState(1);
  const term = useDebounced(q);
  const [orders, reload] = useLoad(() => api.eventOrders(org.id, event.id, { q: term, status, page }), [org.id, event.id, term, status, page]);
  const [refunding, setRefunding] = useState<{ id: string; number: string; amount: number } | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refund() {
    if (!refunding) return;
    setError(null);
    try {
      await api.refundOrder(org.id, event.id, refunding.id);
      setRefunding(null);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  const pages = orders.data ? Math.max(1, Math.ceil(orders.data.total / orders.data.pageSize)) : 1;
  return (
    <section className="card table-card">
      <div className="toolbar">
        <h3 className="grow">Orders</h3>
        <label className="search"><Search size={16} /><span className="sr-only">Find an order</span>
          <input className="input" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} placeholder="Order number, name or email" /></label>
        <label><span className="sr-only">Status</span>
          <select className="select" value={status} onChange={(e) => { setStatus(e.target.value as OrderStatus | ""); setPage(1); }}>
            <option value="">Every status</option>
            {(["Paid", "PendingPayment", "RefundPending", "Refunded", "Expired"] as OrderStatus[]).map((s) => <option key={s} value={s}>{orderLabel[s]}</option>)}
          </select></label>
      </div>
      {orders.state === "failed" && !orders.data ? <Failed message={orders.error} onRetry={reload} /> : orders.data?.items.length === 0 ? (
        <Empty title={term || status ? "No orders match" : "No orders yet"} />
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead><tr><th>Order</th><th>Buyer</th><th className="hide-sm">Tickets</th><th>Amount</th><th className="hide-sm">Status</th><th /></tr></thead>
            {!orders.data ? <SkeletonRows columns={6} /> : (
              <tbody>
                {orders.data.items.map((o) => (
                  <tr key={o.id}>
                    <td><span className="mono">{o.number}</span><span className="small muted block">{when(o.createdAt)}</span></td>
                    <td><span className="block ellipsis">{o.buyerName}</span><span className="small muted block ellipsis">{o.buyerEmail}</span></td>
                    <td className="hide-sm">{o.tickets}</td>
                    <td className="nowrap">{price(o.amount)}</td>
                    <td className="hide-sm"><OrderBadge status={o.status} /></td>
                    <td className="actions">{o.status === "Paid" && <button className="button small" onClick={() => setRefunding(o)}><RotateCcw size={14} />Refund</button>}</td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
        </div>
      )}
      {orders.data && pages > 1 && (
        <div className="pager">
          <span>{plural(orders.data.total, "order")}</span>
          <div className="row gap-6">
            <button className="button small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
            <span>{page} / {pages}</span>
            <button className="button small" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
          </div>
        </div>
      )}
      {refunding && (
        <Dialog title={`Refund order ${refunding.number}?`} onClose={() => setRefunding(null)}>
          <p>{price(refunding.amount)} goes back to the buyer. Their tickets stop working at once and the places go back on sale.</p>
          {error && <p className="form-error" role="alert">{error}</p>}
          <div className="dialog-actions">
            <button className="button" onClick={() => setRefunding(null)}>Cancel</button>
            <button className="button danger" onClick={refund}>Refund</button>
          </div>
        </Dialog>
      )}
    </section>
  );
}

function Gate({ event }: { event: OrganizerEvent }) {
  const org = useOrg();
  const [summary] = useLoad(() => api.checkIns(org.id, event.id), [org.id, event.id]);
  const s = summary.data;
  return (
    <section className="card section">
      <div className="row wrap between">
        <h3>At the gate</h3>
        <a className="button small" {...linkProps(`/gate/${org.id}/${event.id}`)}><ScanLine size={14} />Open the scanner</a>
      </div>
      {!s ? <Loading /> : (
        <>
          <p><strong>{s.admitted.toLocaleString("en-GB")}</strong> of {plural(s.tickets, "ticket")} in{s.rejected > 0 ? ` · ${s.rejected} turned away` : ""}</p>
          <Meter value={s.admitted} max={s.tickets} label="Checked in" />
          {s.gates.length > 0 && <p className="small muted">{s.gates.map((g) => `${g.gate}: ${g.admitted}`).join(" · ")}</p>}
          {s.repeats.length > 0 && (
            <div className="stack-8">
              <strong className="small">Tickets used twice</strong>
              {s.repeats.slice(0, 5).map((r) => (
                <p key={r.ticketId + r.againAt} className="small">{r.label} ({r.holder}): in at {r.firstGate} {time(r.firstAt)}, again at {r.againGate} {time(r.againAt)}</p>
              ))}
            </div>
          )}
        </>
      )}
    </section>
  );
}

export function EventDashboard({ eventId }: { eventId: string }) {
  const org = useOrg();
  const [loaded, reload, set] = useLoad(() => api.organizerEvent(org.id, eventId), [org.id, eventId]);
  const [confirm, setConfirm] = useState<"publish" | "cancel" | "delete" | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const event = loaded.data;
  const manager = canManage(org.role);

  if (loaded.state === "failed" && !event) return <Failed message={loaded.error} onRetry={reload} />;
  if (!event) return <Loading />;

  async function act() {
    setBusy(true);
    setError(null);
    try {
      if (confirm === "publish") set(await api.publish(org.id, eventId));
      if (confirm === "cancel") { await api.cancel(org.id, eventId); reload(); }
      if (confirm === "delete") { await api.deleteEvent(org.id, eventId); navigate(`/manage/${org.id}`); return; }
      setConfirm(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const dialog = {
    publish: { title: "Publish this event?", text: `It becomes public with ${plural(event.capacity, "place")} at ${event.venueName}. Prices and the floor plan cannot change afterwards.`, action: "Publish", tone: "primary" },
    cancel: { title: "Cancel this event?", text: "Sales stop at once, every ticket stops working and every paid order is refunded. This cannot be undone.", action: "Cancel event", tone: "danger" },
    delete: { title: "Delete this draft?", text: "The draft is removed. Nothing was sold.", action: "Delete", tone: "danger" },
  };

  return (
    <div className="stack">
      <a className="back" {...linkProps(`/manage/${org.id}`)}>← Events</a>
      <div className="page-head">
        <div>
          <div className="row wrap gap-6"><SaleBadge state={event.saleState} />{event.waitingRoom && <span className="badge plain">Waiting room · {event.admitPerMinute}/min</span>}</div>
          <h2 className="event-manage-title">{event.title}</h2>
          <p className="muted small">{longDate(event.startsAt)}, {time(event.startsAt)} · {event.venueName}, {event.city} · sales open {when(event.saleStartsAt)}</p>
        </div>
        <div className="row wrap gap-6">
          {event.status !== "Draft" && <a className="button" {...linkProps(`/events/${event.id}`)}><ExternalLink size={15} />Public page</a>}
          {manager && event.status === "Draft" && <>
            <button className="button" onClick={() => setConfirm("delete")}><Trash2 size={15} />Delete</button>
            <a className="button" {...linkProps(`/manage/${org.id}/events/${event.id}/edit`)}><Pencil size={15} />Edit</a>
            <button className="button primary" onClick={() => setConfirm("publish")}><Rocket size={15} />Publish</button>
          </>}
          {manager && event.status === "Published" && <button className="button danger" onClick={() => setConfirm("cancel")}><Ban size={15} />Cancel event</button>}
        </div>
      </div>

      {event.status === "Draft" ? (
        <section className="card section">
          <h3>Draft</h3>
          <p className="muted">{plural(event.capacity, "place")} at {event.venueName}. Prices: {event.categories.map((c) => `${c.name} ${price(c.price)}`).join(", ")}.</p>
          <p className="muted small">{event.refundCutoffHours === null ? "Only the organizer refunds." : `Buyers may refund until ${event.refundCutoffHours} hours before the start.`} Up to {event.maxTicketsPerBuyer} tickets per buyer.</p>
        </section>
      ) : manager ? (
        <>
          <Sales event={event} />
          <Gate event={event} />
          <Orders event={event} />
        </>
      ) : <Gate event={event} />}

      {confirm && (
        <Dialog title={dialog[confirm].title} onClose={() => setConfirm(null)}>
          <p>{dialog[confirm].text}</p>
          {error && <p className="form-error" role="alert">{error}</p>}
          <div className="dialog-actions">
            <button className="button" onClick={() => setConfirm(null)}>Back</button>
            <button className={`button ${dialog[confirm].tone}`} onClick={act} disabled={busy}>{busy ? "Working…" : dialog[confirm].action}</button>
          </div>
        </Dialog>
      )}
    </div>
  );
}
