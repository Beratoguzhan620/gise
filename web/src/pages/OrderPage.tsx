import { CalendarDays, CheckCircle2, Download, Loader2, MapPin, RotateCcw, XCircle } from "lucide-react";
import { useEffect, useState } from "react";
import { api } from "../api";
import { OrderBadge } from "../components/Badges";
import { Countdown } from "../components/Countdown";
import { Dialog } from "../components/Dialog";
import { Qr } from "../components/Qr";
import { Failed, Loading } from "../components/States";
import { longDate, price, refundLabel, seatName, time, when } from "../format";
import { useLoad } from "../hooks";
import { linkProps } from "../router";
import type { MyTicket, Order } from "../types";

function RefundDialog({ order, onClose, onDone }: { order: Order; onClose: () => void; onDone: (order: Order) => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function refund() {
    setBusy(true);
    setError(null);
    try {
      onDone(await api.refund(order.id));
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  return (
    <Dialog title="Refund this order?" onClose={onClose}>
      <p>{price(order.amount)} goes back to the card you paid with. Your {order.lines.length === 1 ? "ticket stops" : `${order.lines.length} tickets stop`} working at once and the places go back on sale.</p>
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="dialog-actions">
        <button className="button" onClick={onClose}>Keep my tickets</button>
        <button className="button danger" onClick={refund} disabled={busy}>{busy ? "Refunding…" : "Refund"}</button>
      </div>
    </Dialog>
  );
}

export function TicketCard({ ticket }: { ticket: MyTicket }) {
  return (
    <article className={`ticket card${ticket.status === "Void" ? " void" : ""}`} aria-label={`Ticket: ${ticket.label}`}>
      <div className="ticket-code">
        {ticket.code ? <Qr code={ticket.code} size={168} label={`QR code for ${ticket.label}`} /> : <div className="qr-void"><XCircle size={28} />No longer valid</div>}
      </div>
      <div className="ticket-info">
        <strong>{ticket.label}</strong>
        <span className="small muted">{ticket.category} · {price(ticket.price)}</span>
        <span className="small">{ticket.holder}</span>
        {ticket.checkedInAt && <span className="badge ok">In at {time(ticket.checkedInAt)}</span>}
        {ticket.status === "Void" && <span className="badge plain">Refunded</span>}
      </div>
    </article>
  );
}

export function OrderPage({ orderId }: { orderId: string }) {
  const [loaded, reload, set] = useLoad(() => api.order(orderId), [orderId]);
  const [tickets, setTickets] = useState<MyTicket[]>([]);
  const [refunding, setRefunding] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const order = loaded.data;

  // Back from the payment page the webhook may still be on its way: look again every two seconds.
  useEffect(() => {
    if (order?.status !== "PendingPayment" && order?.status !== "RefundPending") return;
    const timer = setInterval(reload, 2000);
    return () => clearInterval(timer);
  }, [order?.status, reload]);

  useEffect(() => {
    if (!order || (order.status !== "Paid" && order.status !== "Refunded" && order.status !== "RefundPending")) return;
    api.tickets(new Date(order.event.startsAt) <= new Date()).then((all) => setTickets(all.filter((t) => t.orderId === order.id)), () => {});
  }, [order?.status, order?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  async function download() {
    if (!order) return;
    setDownloading(true);
    try {
      const blob = await api.ticketsPdf(order.id);
      const url = URL.createObjectURL(blob);
      const link = Object.assign(document.createElement("a"), { href: url, download: `gise-${order.number}.pdf` });
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } finally {
      setDownloading(false);
    }
  }

  if (loaded.state === "failed" && !order) return <Failed message={loaded.error} onRetry={reload} />;
  if (!order) return <Loading />;

  const headline = {
    PendingPayment: { icon: <Loader2 size={26} className="spin accent" />, title: "Waiting for the payment", text: "As soon as the payment provider confirms, your tickets appear here and in your email." },
    Paid: { icon: <CheckCircle2 size={26} className="ok-text" />, title: "You're going!", text: "Your tickets are below and on their way to your email. Each code lets one person in, once." },
    Failed: { icon: <XCircle size={26} className="danger-text" />, title: "The payment was declined", text: "Nothing was charged and the places went back on sale." },
    Expired: { icon: <XCircle size={26} className="muted" />, title: "Not paid in time", text: "The places went back on sale. If money was taken after all, it comes back automatically." },
    RefundPending: { icon: <RotateCcw size={26} className="accent" />, title: "Refund on its way", text: "The tickets no longer work. The money goes back as soon as the payment provider confirms." },
    Refunded: { icon: <RotateCcw size={26} className="muted" />, title: "Refunded", text: "The money went back to the card you paid with." },
  }[order.status];

  return (
    <div className="order-page">
      <a className="back" {...linkProps("/tickets")}>← My tickets</a>
      <section className="card order-hero">
        {headline.icon}
        <div className="grow">
          <div className="row wrap gap-6"><h1>{headline.title}</h1><OrderBadge status={order.status} /></div>
          <p className="muted">{headline.text}</p>
          {order.refundReason && <p className="small muted">{refundLabel[order.refundReason]}.</p>}
        </div>
      </section>

      <section className="card section">
        <div className="row wrap between">
          <div>
            <a className="strong-link" {...linkProps(`/events/${order.event.id}`)}>{order.event.title}</a>
            <p className="muted small row gap-6"><CalendarDays size={14} />{longDate(order.event.startsAt)}, {time(order.event.startsAt)}</p>
            <p className="muted small row gap-6"><MapPin size={14} />{order.event.venueName}, {order.event.city}</p>
          </div>
          <div className="right">
            <span className="small muted block">Order</span>
            <strong className="mono">{order.number}</strong>
          </div>
        </div>
        <ul className="lines">
          {order.lines.map((l) => <li key={l.placeId}><span>{seatName(l.sectionName, l.row, l.number)}<span className="small muted block">{l.category}</span></span><strong>{price(l.price)}</strong></li>)}
        </ul>
        <div className="basket-total"><span>Total</span><strong>{price(order.amount)}</strong></div>
        {order.status === "PendingPayment" && order.checkoutUrl && (
          <div className="row wrap between">
            <Countdown until={order.paymentDeadline} label="Places held for" />
            <a className="button primary" href={order.checkoutUrl}>Go to the payment page</a>
          </div>
        )}
      </section>

      {tickets.length > 0 && (
        <section className="stack">
          <div className="row wrap between">
            <h2>Tickets</h2>
            {order.status === "Paid" && <button className="button" onClick={download} disabled={downloading}><Download size={16} />{downloading ? "Preparing…" : "Download PDF"}</button>}
          </div>
          <div className="ticket-grid">{tickets.map((t) => <TicketCard key={t.id} ticket={t} />)}</div>
        </section>
      )}

      {order.status === "Paid" && (
        <section className="card section refund-box">
          {order.canRefund ? (
            <>
              <p className="grow">Can't make it? You can refund until {when(order.refundDeadline!)}.</p>
              <button className="button danger" onClick={() => setRefunding(true)}><RotateCcw size={15} />Refund</button>
            </>
          ) : (
            <p className="muted">{order.refundDeadline ? `Refunds closed ${when(order.refundDeadline)}.` : "For refunds, contact the organizer with your order number."}</p>
          )}
        </section>
      )}

      {refunding && <RefundDialog order={order} onClose={() => setRefunding(false)} onDone={(o) => { set(o); setRefunding(false); }} />}
    </div>
  );
}
