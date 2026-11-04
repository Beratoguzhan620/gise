import { ArrowDown, ArrowUp, Plus, Trash2, Wand2 } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../../api";
import { Failed, Loading } from "../../components/States";
import { useOrg } from "../../context";
import { canManage, plural } from "../../format";
import { linkProps, navigate } from "../../router";
import type { SectionInput, SectionKind, VenueInput } from "../../types";

const letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

/** Rows labelled A, B, C… (then AA, AB… past Z), each with the same number of seats. */
export function makeRows(count: number, seats: number, startAt = 0) {
  return Array.from({ length: count }, (_, i) => {
    const n = startAt + i;
    const label = n < 26 ? letters[n]! : `${letters[Math.floor(n / 26) - 1]}${letters[n % 26]}`;
    return { label, seats };
  });
}

const starter: VenueInput = {
  name: "",
  city: "",
  address: "",
  sections: [
    { key: "A", name: "Stalls", kind: "Seated", rows: makeRows(8, 16), capacity: 0 },
    { key: "floor", name: "Standing", kind: "Standing", rows: [], capacity: 100 },
  ],
};

const places = (s: SectionInput) => (s.kind === "Seated" ? s.rows.reduce((sum, r) => sum + (Number(r.seats) || 0), 0) : Number(s.capacity) || 0);

function SectionEditor({ section, index, last, onChange, onMove, onRemove }: {
  section: SectionInput; index: number; last: boolean;
  onChange: (s: SectionInput) => void; onMove: (by: number) => void; onRemove: () => void;
}) {
  const [fill, setFill] = useState({ rows: section.rows.length || 8, seats: section.rows[0]?.seats ?? 16 });
  const set = (patch: Partial<SectionInput>) => onChange({ ...section, ...patch });
  const id = `section-${index}`;

  return (
    <fieldset className="section-editor card" aria-labelledby={`${id}-title`}>
      <div className="row wrap between">
        <strong id={`${id}-title`}>{section.name || "New section"} <span className="muted small">· {plural(places(section), "place")}</span></strong>
        <div className="row gap-4">
          <button type="button" className="icon-button" onClick={() => onMove(-1)} disabled={index === 0} aria-label="Move up"><ArrowUp size={16} /></button>
          <button type="button" className="icon-button" onClick={() => onMove(1)} disabled={last} aria-label="Move down"><ArrowDown size={16} /></button>
          <button type="button" className="icon-button" onClick={onRemove} aria-label={`Remove ${section.name || "section"}`}><Trash2 size={16} /></button>
        </div>
      </div>
      <div className="grid-3">
        <label className="field">Name<input className="input" value={section.name} onChange={(e) => set({ name: e.target.value })} required maxLength={40} /></label>
        <label className="field">Short key<input className="input mono-input" value={section.key} onChange={(e) => set({ key: e.target.value.replace(/[^A-Za-z0-9_-]/g, "") })} required maxLength={12} />
          <span className="hint">Stays the same on tickets and prices.</span></label>
        <label className="field">Kind
          <select className="select" value={section.kind} onChange={(e) => set({ kind: e.target.value as SectionKind })}>
            <option value="Seated">Numbered seats</option>
            <option value="Standing">Standing area</option>
          </select>
        </label>
      </div>
      {section.kind === "Standing" ? (
        <label className="field narrow-field">Capacity<input className="input" type="number" min={1} max={20000} value={section.capacity} onChange={(e) => set({ capacity: Number(e.target.value) })} /></label>
      ) : (
        <>
          <div className="fill-row">
            <Wand2 size={16} className="muted" />
            <label className="inline-field"><input className="input small-input" type="number" min={1} max={60} value={fill.rows} onChange={(e) => setFill({ ...fill, rows: Number(e.target.value) })} aria-label="Number of rows" /> rows</label>
            <span>×</span>
            <label className="inline-field"><input className="input small-input" type="number" min={1} max={80} value={fill.seats} onChange={(e) => setFill({ ...fill, seats: Number(e.target.value) })} aria-label="Seats per row" /> seats</label>
            <button type="button" className="button small" onClick={() => set({ rows: makeRows(fill.rows, fill.seats) })}>Fill rows</button>
          </div>
          <div className="rows-editor">
            {section.rows.map((row, i) => (
              <div key={i} className="row-chip">
                <input className="input row-label-input" value={row.label} aria-label={`Row ${i + 1} label`} maxLength={4}
                  onChange={(e) => set({ rows: section.rows.map((r, j) => (j === i ? { ...r, label: e.target.value } : r)) })} />
                <input className="input small-input" type="number" min={1} max={80} value={row.seats} aria-label={`Seats in row ${row.label}`}
                  onChange={(e) => set({ rows: section.rows.map((r, j) => (j === i ? { ...r, seats: Number(e.target.value) } : r)) })} />
                <button type="button" className="icon-button" onClick={() => set({ rows: section.rows.filter((_, j) => j !== i) })} aria-label={`Remove row ${row.label}`}><Trash2 size={14} /></button>
              </div>
            ))}
            <button type="button" className="button small" onClick={() => set({ rows: [...section.rows, ...makeRows(1, section.rows.at(-1)?.seats ?? 16, section.rows.length)] })}><Plus size={14} />Row</button>
          </div>
        </>
      )}
    </fieldset>
  );
}

export function VenueForm({ venueId }: { venueId?: string }) {
  const org = useOrg();
  const [form, setForm] = useState<VenueInput | null>(venueId ? null : starter);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const editable = canManage(org.role);

  useEffect(() => {
    if (!venueId) return;
    api.venues(org.id).then((all) => {
      const venue = all.find((v) => v.id === venueId);
      if (!venue) setLoadError("This venue does not exist.");
      else setForm({ name: venue.name, city: venue.city, address: venue.address, sections: venue.sections.map((s) => ({ key: s.key, name: s.name, kind: s.kind, rows: s.rows, capacity: s.capacity })) });
    }, (e: Error) => setLoadError(e.message));
  }, [org.id, venueId]);

  if (loadError) return <Failed message={loadError} />;
  if (!form) return <Loading />;

  const sections = form.sections;
  const setSections = (next: SectionInput[]) => setForm({ ...form, sections: next });
  const total = sections.reduce((sum, s) => sum + places(s), 0);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!form) return;
    setBusy(true);
    setError(null);
    try {
      if (venueId) await api.updateVenue(org.id, venueId, form);
      else await api.createVenue(org.id, form);
      navigate(`/manage/${org.id}/venues`);
    } catch (err) {
      setError((err as ApiError).message);
      setBusy(false);
    }
  }

  async function remove() {
    if (!venueId) return;
    setError(null);
    try {
      await api.deleteVenue(org.id, venueId);
      navigate(`/manage/${org.id}/venues`);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <form className="stack" onSubmit={save}>
      <a className="back" {...linkProps(`/manage/${org.id}/venues`)}>← Venues</a>
      <div className="page-head"><div><h2>{venueId ? form.name : "New venue"}</h2><p className="muted small">{plural(total, "place")} in {plural(sections.length, "section")}. Events already on sale keep the plan they were published with.</p></div></div>
      <fieldset className="card section" disabled={!editable}>
        <div className="grid-3">
          <label className="field">Name<input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required maxLength={80} placeholder="Atatürk Kültür Merkezi" /></label>
          <label className="field">City<input className="input" value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} required maxLength={60} placeholder="Samsun" /></label>
          <label className="field">Address<input className="input" value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} maxLength={200} /></label>
        </div>
      </fieldset>
      <fieldset className="stack" disabled={!editable}>
        <legend className="section-legend">Floor plan, nearest the stage first</legend>
        {sections.map((section, i) => (
          <SectionEditor key={i} section={section} index={i} last={i === sections.length - 1}
            onChange={(s) => setSections(sections.map((x, j) => (j === i ? s : x)))}
            onMove={(by) => { const next = [...sections]; [next[i], next[i + by]] = [next[i + by]!, next[i]!]; setSections(next); }}
            onRemove={() => setSections(sections.filter((_, j) => j !== i))} />
        ))}
        <div className="row wrap gap-10">
          <button type="button" className="button" onClick={() => setSections([...sections, { key: `S${sections.length + 1}`, name: "", kind: "Seated", rows: makeRows(5, 12), capacity: 0 }])}><Plus size={16} />Seated section</button>
          <button type="button" className="button" onClick={() => setSections([...sections, { key: `F${sections.length + 1}`, name: "", kind: "Standing", rows: [], capacity: 100 }])}><Plus size={16} />Standing area</button>
        </div>
      </fieldset>
      {error && <p className="form-error" role="alert">{error}</p>}
      {editable && (
        <div className="row wrap between sticky-actions">
          {venueId ? <button type="button" className="button danger" onClick={remove}><Trash2 size={15} />Delete venue</button> : <span />}
          <button className="button primary" disabled={busy}>{busy ? "Saving…" : venueId ? "Save venue" : "Create venue"}</button>
        </div>
      )}
    </form>
  );
}
