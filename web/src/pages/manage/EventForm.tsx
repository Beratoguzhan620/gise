import { Plus, Trash2 } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../../api";
import { Empty, Failed, Loading } from "../../components/States";
import { useOrg } from "../../context";
import { fromLocalInput, plural, toLocalInput } from "../../format";
import { linkProps, navigate } from "../../router";
import type { EventInput, OrganizerEvent, Venue } from "../../types";

interface Draft {
  venueId: string;
  title: string;
  description: string;
  startsAt: string;
  saleStartsAt: string;
  maxTicketsPerBuyer: number;
  refundCutoffHours: string;
  waitingRoom: boolean;
  admitPerMinute: number;
  categories: { name: string; price: string }[];
  pricing: Record<string, string>;
}

function defaults(venue: Venue | undefined): Draft {
  const start = new Date();
  start.setDate(start.getDate() + 30);
  start.setHours(20, 0, 0, 0);
  const sale = new Date();
  sale.setSeconds(0, 0);
  const categories = venue && venue.sections.length > 1
    ? [{ name: "Front", price: "750" }, { name: "Standard", price: "450" }]
    : [{ name: "Standard", price: "450" }];
  return {
    venueId: venue?.id ?? "",
    title: "",
    description: "",
    startsAt: toLocalInput(start.toISOString()),
    saleStartsAt: toLocalInput(sale.toISOString()),
    maxTicketsPerBuyer: 6,
    refundCutoffHours: "48",
    waitingRoom: false,
    admitPerMinute: 200,
    categories,
    pricing: Object.fromEntries((venue?.sections ?? []).map((s, i) => [s.key, categories[Math.min(i, categories.length - 1)]!.name])),
  };
}

function fromEvent(e: OrganizerEvent): Draft {
  const name = (id: string) => e.categories.find((c) => c.id === id)?.name ?? "";
  return {
    venueId: e.venueId,
    title: e.title,
    description: e.description,
    startsAt: toLocalInput(e.startsAt),
    saleStartsAt: toLocalInput(e.saleStartsAt),
    maxTicketsPerBuyer: e.maxTicketsPerBuyer,
    refundCutoffHours: e.refundCutoffHours === null ? "" : String(e.refundCutoffHours),
    waitingRoom: e.waitingRoom,
    admitPerMinute: e.admitPerMinute || 200,
    categories: e.categories.map((c) => ({ name: c.name, price: String(c.price) })),
    pricing: Object.fromEntries(e.pricing.map((p) => [p.section, name(p.categoryId)])),
  };
}

function toInput(d: Draft, venue: Venue): EventInput {
  return {
    venueId: d.venueId,
    title: d.title,
    description: d.description,
    startsAt: fromLocalInput(d.startsAt),
    saleStartsAt: fromLocalInput(d.saleStartsAt),
    maxTicketsPerBuyer: d.maxTicketsPerBuyer,
    refundCutoffHours: d.refundCutoffHours === "" ? null : Number(d.refundCutoffHours),
    waitingRoom: d.waitingRoom,
    admitPerMinute: d.waitingRoom ? d.admitPerMinute : 0,
    categories: d.categories.map((c) => ({ name: c.name, price: Number(c.price.replace(",", ".")) })),
    pricing: venue.sections.map((s) => ({ section: s.key, category: d.pricing[s.key] ?? "" })),
  };
}

export function EventForm({ eventId }: { eventId?: string }) {
  const org = useOrg();
  const [venues, setVenues] = useState<Venue[] | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.venues(org.id), eventId ? api.organizerEvent(org.id, eventId) : Promise.resolve(null)]).then(([all, event]) => {
      setVenues(all);
      setDraft(event ? fromEvent(event) : defaults(all[0]));
    }, (e: Error) => setLoadError(e.message));
  }, [org.id, eventId]);

  if (loadError) return <Failed message={loadError} />;
  if (!venues || !draft) return <Loading />;
  if (venues.length === 0) {
    return (
      <div className="card section">
        <Empty title="Add a venue first">
          <span className="muted">An event takes its seats and standing areas from a venue's floor plan.</span>
          <a className="button primary" {...linkProps(`/manage/${org.id}/venues/new`)}><Plus size={16} />New venue</a>
        </Empty>
      </div>
    );
  }

  const venue = venues.find((v) => v.id === draft.venueId) ?? venues[0]!;
  const set = (patch: Partial<Draft>) => setDraft({ ...draft, ...patch });
  const names = draft.categories.map((c) => c.name.trim()).filter(Boolean);

  function chooseVenue(id: string) {
    const next = venues!.find((v) => v.id === id)!;
    const first = names[0] ?? "";
    set({ venueId: id, pricing: Object.fromEntries(next.sections.map((s) => [s.key, draft!.pricing[s.key] ?? first])) });
  }

  function renameCategory(i: number, name: string) {
    const old = draft!.categories[i]!.name;
    set({
      categories: draft!.categories.map((c, j) => (j === i ? { ...c, name } : c)),
      pricing: Object.fromEntries(Object.entries(draft!.pricing).map(([k, v]) => [k, v === old ? name : v])),
    });
  }

  async function save(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const body = toInput({ ...draft!, venueId: venue.id }, venue);
      const saved = eventId ? await api.updateEvent(org.id, eventId, body) : await api.createEvent(org.id, body);
      navigate(`/manage/${org.id}/events/${saved.id}`);
    } catch (err) {
      setError((err as ApiError).message);
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={save}>
      <a className="back" {...linkProps(eventId ? `/manage/${org.id}/events/${eventId}` : `/manage/${org.id}`)}>← {eventId ? "Event" : "Events"}</a>
      <div className="page-head"><div><h2>{eventId ? "Edit draft" : "New event"}</h2><p className="muted small">Saved as a draft. Nobody sees it until you publish.</p></div></div>

      <section className="card section">
        <h3>What and when</h3>
        <label className="field">Title<input className="input" value={draft.title} onChange={(e) => set({ title: e.target.value })} required minLength={3} maxLength={120} placeholder="Kemençe ve Şiir Gecesi" /></label>
        <label className="field">Description<textarea className="textarea" value={draft.description} onChange={(e) => set({ description: e.target.value })} maxLength={4000} placeholder="Who is on, how long it lasts, age limits…" /></label>
        <div className="grid-3">
          <label className="field">Venue
            <select className="select" value={venue.id} onChange={(e) => chooseVenue(e.target.value)}>
              {venues.map((v) => <option key={v.id} value={v.id}>{v.name}, {v.city} ({v.capacity.toLocaleString("en-GB")})</option>)}
            </select>
          </label>
          <label className="field">Starts<input className="input" type="datetime-local" value={draft.startsAt} onChange={(e) => set({ startsAt: e.target.value })} required /></label>
          <label className="field">Sales open<input className="input" type="datetime-local" value={draft.saleStartsAt} onChange={(e) => set({ saleStartsAt: e.target.value })} required /></label>
        </div>
      </section>

      <section className="card section">
        <h3>Prices</h3>
        <div className="category-list">
          {draft.categories.map((c, i) => (
            <div key={i} className="row gap-10">
              <label className="field grow">Category<input className="input" value={c.name} onChange={(e) => renameCategory(i, e.target.value)} required maxLength={40} /></label>
              <label className="field price-field">Price (₺)<input className="input" inputMode="decimal" value={c.price} onChange={(e) => set({ categories: draft.categories.map((x, j) => (j === i ? { ...x, price: e.target.value } : x)) })} required /></label>
              <button type="button" className="icon-button align-end" disabled={draft.categories.length === 1} onClick={() => set({ categories: draft.categories.filter((_, j) => j !== i) })} aria-label={`Remove ${c.name}`}><Trash2 size={16} /></button>
            </div>
          ))}
          <div><button type="button" className="button small" disabled={draft.categories.length >= 8} onClick={() => set({ categories: [...draft.categories, { name: `Category ${draft.categories.length + 1}`, price: "300" }] })}><Plus size={14} />Category</button></div>
        </div>
        <table className="table compact">
          <thead><tr><th>Section</th><th>Places</th><th>Category</th></tr></thead>
          <tbody>
            {venue.sections.map((s) => (
              <tr key={s.key}>
                <td>{s.name}</td>
                <td className="muted">{plural(s.places, s.kind === "Seated" ? "seat" : "place")}</td>
                <td>
                  <select className="select narrow" value={draft.pricing[s.key] ?? ""} onChange={(e) => set({ pricing: { ...draft.pricing, [s.key]: e.target.value } })} aria-label={`Category of ${s.name}`}>
                    <option value="" disabled>Choose…</option>
                    {names.map((n) => <option key={n}>{n}</option>)}
                  </select>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="card section">
        <h3>Rules</h3>
        <div className="grid-3">
          <label className="field">Tickets per buyer<input className="input" type="number" min={1} max={10} value={draft.maxTicketsPerBuyer} onChange={(e) => set({ maxTicketsPerBuyer: Number(e.target.value) })} /></label>
          <label className="field">Refunds
            <select className="select" value={draft.refundCutoffHours} onChange={(e) => set({ refundCutoffHours: e.target.value })}>
              <option value="24">Until 24 hours before</option>
              <option value="48">Until 48 hours before</option>
              <option value="72">Until 3 days before</option>
              <option value="168">Until a week before</option>
              <option value="">Only the organizer refunds</option>
            </select>
          </label>
        </div>
        <label className="check">
          <input type="checkbox" checked={draft.waitingRoom} onChange={(e) => set({ waitingRoom: e.target.checked })} />
          <span><strong>Waiting room</strong><span className="small muted block">For sales that sell out in minutes: buyers queue, early arrivals in random order, and are let in at a steady pace.</span></span>
        </label>
        {draft.waitingRoom && (
          <label className="field narrow-field">People let in per minute<input className="input" type="number" min={10} max={10000} value={draft.admitPerMinute} onChange={(e) => set({ admitPerMinute: Number(e.target.value) })} /></label>
        )}
      </section>

      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="row wrap between sticky-actions">
        <span className="small muted">{plural(venue.capacity, "place")} at {venue.name}</span>
        <button className="button primary" disabled={busy}>{busy ? "Saving…" : eventId ? "Save draft" : "Create draft"}</button>
      </div>
    </form>
  );
}
