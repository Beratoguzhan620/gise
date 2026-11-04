import { CalendarDays, MapPin, Search, Ticket, Users } from "lucide-react";
import { useEffect, useState } from "react";
import { api } from "../api";
import { SaleBadge } from "../components/Badges";
import { Empty, Failed } from "../components/States";
import { plural, shortPrice, when } from "../format";
import { useDebounced, useLoad } from "../hooks";
import { linkProps } from "../router";
import type { EventSummary } from "../types";

function EventCard({ event }: { event: EventSummary }) {
  const day = new Date(event.startsAt);
  const scarce = event.saleState === "OnSale" && event.available > 0 && event.available <= Math.max(10, event.capacity * 0.1);
  return (
    <a className="event-card card" {...linkProps(`/events/${event.id}`)}>
      <div className="event-date" aria-hidden="true">
        <span>{day.toLocaleDateString("en-GB", { month: "short" })}</span>
        <strong>{day.getDate()}</strong>
      </div>
      <div className="event-body">
        <div className="row wrap gap-6">
          <SaleBadge state={event.saleState} />
          {event.waitingRoom && <span className="badge plain"><Users size={12} />Waiting room</span>}
          {scarce && <span className="badge info">{plural(event.available, "place")} left</span>}
        </div>
        <h2 className="event-title">{event.title}</h2>
        <p className="muted small row gap-6"><CalendarDays size={14} />{when(event.startsAt)}</p>
        <p className="muted small row gap-6"><MapPin size={14} />{event.venueName}, {event.city}</p>
      </div>
      <div className="event-foot">
        <span className="small muted">{event.organizer}</span>
        <span className="from">from <strong>{shortPrice(event.priceFrom)}</strong></span>
      </div>
    </a>
  );
}

function SkeletonCards() {
  return (
    <div className="event-grid" aria-hidden="true">
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className="event-card card">
          <div className="event-date skeleton-block" />
          <div className="event-body"><div className="skeleton" style={{ width: "40%" }} /><div className="skeleton" style={{ width: "80%", height: 18 }} /><div className="skeleton" style={{ width: "60%" }} /></div>
        </div>
      ))}
    </div>
  );
}

export function Catalog({ q: initialQ, city: initialCity }: { q?: string; city?: string }) {
  const [q, setQ] = useState(initialQ ?? "");
  const [city, setCity] = useState(initialCity ?? "");
  const term = useDebounced(q);
  const place = useDebounced(city);
  const [events, reload] = useLoad(() => api.events({ q: term, city: place }), [term, place]);

  // Keep the search in the address, so it survives a reload and can be shared.
  useEffect(() => {
    const params = new URLSearchParams();
    if (term.trim()) params.set("q", term.trim());
    if (place.trim()) params.set("city", place.trim());
    const search = params.toString();
    history.replaceState(null, "", search ? `/?${search}` : "/");
  }, [term, place]);

  const filtered = !!(term.trim() || place.trim());
  return (
    <>
      <section className="hero">
        <h1>What's on</h1>
        <p className="muted">Concerts, plays and festivals. Pick your seats, pay, and your tickets are on your phone.</p>
        <div className="hero-search card">
          <label className="search grow">
            <Search size={16} />
            <span className="sr-only">Search events</span>
            <input className="input" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Artist, play or venue" />
          </label>
          <label className="search city">
            <MapPin size={16} />
            <span className="sr-only">City</span>
            <input className="input" value={city} onChange={(e) => setCity(e.target.value)} placeholder="City" />
          </label>
        </div>
      </section>

      {events.state === "failed" && !events.data && <Failed message={events.error} onRetry={reload} />}
      {events.state === "loading" && !events.data && <SkeletonCards />}
      {events.data && events.data.items.length === 0 && (
        <Empty title={filtered ? "Nothing matches" : "No events on sale yet"} icon={<Ticket size={26} />}>
          <span className="muted">{filtered ? "Try another name or city." : "Organizers can put their first event on sale from Sell tickets."}</span>
        </Empty>
      )}
      {events.data && events.data.items.length > 0 && (
        <>
          <p className="small muted results" aria-live="polite">{plural(events.data.total, "event")}</p>
          <div className="event-grid">
            {events.data.items.map((e) => <EventCard key={e.id} event={e} />)}
          </div>
        </>
      )}
    </>
  );
}
