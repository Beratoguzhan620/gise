import { CalendarDays, MapPin, Ticket } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { Empty, Failed, Loading } from "../components/States";
import { longDate, plural, time } from "../format";
import { useLoad } from "../hooks";
import { linkProps } from "../router";
import type { MyTicket } from "../types";
import { TicketCard } from "./OrderPage";

export function MyTickets() {
  const [past, setPast] = useState(false);
  const [tickets, reload] = useLoad(() => api.tickets(past), [past]);

  const byEvent = new Map<string, MyTicket[]>();
  for (const t of tickets.data ?? []) byEvent.set(t.eventId, [...(byEvent.get(t.eventId) ?? []), t]);

  return (
    <>
      <div className="page-head">
        <div>
          <h1>My tickets</h1>
          <p className="muted">Show the code at the gate. It works without a connection, so a screenshot will do.</p>
        </div>
        <div className="tabs" role="tablist">
          <button role="tab" aria-selected={!past} className={`tab${!past ? " active" : ""}`} onClick={() => setPast(false)}>Upcoming</button>
          <button role="tab" aria-selected={past} className={`tab${past ? " active" : ""}`} onClick={() => setPast(true)}>Past</button>
        </div>
      </div>

      {tickets.state === "failed" && !tickets.data && <Failed message={tickets.error} onRetry={reload} />}
      {tickets.state === "loading" && !tickets.data && <Loading />}
      {tickets.data?.length === 0 && (
        <Empty title={past ? "No past events" : "No upcoming tickets"} icon={<Ticket size={26} />}>
          {!past && <a className="button primary" {...linkProps("/")}>Find something to see</a>}
        </Empty>
      )}
      <div className="stack-lg">
        {[...byEvent.values()].map((group) => {
          const first = group[0]!;
          const orders = [...new Set(group.map((t) => t.orderId))];
          return (
            <section key={first.eventId} className="ticket-group">
              <div className="row wrap between">
                <div>
                  <h2><a className="strong-link" {...linkProps(`/events/${first.eventId}`)}>{first.eventTitle}</a></h2>
                  <p className="muted small row wrap gap-6"><CalendarDays size={14} />{longDate(first.startsAt)}, {time(first.startsAt)}<MapPin size={14} />{first.venueName}, {first.city}</p>
                </div>
                <div className="row wrap gap-6">
                  <span className="small muted">{plural(group.length, "ticket")}</span>
                  {orders.map((id) => <a key={id} className="button small" {...linkProps(`/orders/${id}`)}>Order {group.find((t) => t.orderId === id)!.orderNumber}</a>)}
                </div>
              </div>
              <div className="ticket-grid">{group.map((t) => <TicketCard key={t.id} ticket={t} />)}</div>
            </section>
          );
        })}
      </div>
    </>
  );
}
