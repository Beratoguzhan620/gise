import { Minus, Plus } from "lucide-react";
import { categoryColors, plural, shortPrice } from "../format";
import { isFree, seatKey, type Selection } from "../seatmap";
import type { Availability, EventDetail, PublicSection } from "../types";

interface Props {
  event: EventDetail;
  availability: Availability | undefined;
  selection: Selection;
  limit: number;
  disabled: boolean;
  onSeat: (key: string) => void;
  onStanding: (section: string, quantity: number, free: number) => void;
}

function colorOf(event: EventDetail, categoryId: string): string {
  return categoryColors[Math.max(0, event.categories.findIndex((c) => c.id === categoryId)) % categoryColors.length]!;
}

export function Legend({ event }: { event: EventDetail }) {
  return (
    <ul className="legend" aria-label="Prices">
      {event.categories.map((c) => (
        <li key={c.id}><span className="dot" style={{ background: colorOf(event, c.id) }} />{c.name}<strong>{shortPrice(c.price)}</strong></li>
      ))}
      <li><span className="dot selected" />Your choice</li>
      <li><span className="dot taken" />Taken</li>
    </ul>
  );
}

function SeatedSection({ section, color, price, props }: { section: PublicSection; color: string; price: number; props: Props }) {
  const { availability, selection, disabled, onSeat } = props;
  const free = availability?.sections.find((s) => s.key === section.key)?.available ?? 0;
  const full = selection.seats.length + Object.values(selection.standing).reduce((a, b) => a + b, 0) >= props.limit;

  return (
    <section className="map-section" aria-label={section.name}>
      <div className="map-section-head">
        <span className="dot" style={{ background: color }} />
        <strong>{section.name}</strong>
        <span className="muted small">{shortPrice(price)} · {plural(free, "seat")} free</span>
      </div>
      <div className="rows">
        {section.rows.map((row) => (
          <div className="seat-row" key={row.label}>
            <span className="row-label" aria-hidden="true">{row.label}</span>
            <div className="seats">
              {Array.from({ length: row.seats }, (_, i) => {
                const number = i + 1;
                const key = seatKey(section.key, row.label, number);
                const chosen = selection.seats.includes(key);
                const open = isFree(availability, section.key, row.label, number);
                const blocked = disabled || (!chosen && (!open || full));
                return (
                  <button
                    key={number}
                    type="button"
                    className={`seat${chosen ? " chosen" : open ? "" : " taken"}`}
                    style={{ ["--seat" as string]: color }}
                    disabled={blocked}
                    aria-pressed={chosen}
                    aria-label={`${section.name}, row ${row.label}, seat ${number}${open || chosen ? `, ${shortPrice(price)}` : ", taken"}`}
                    title={`${row.label}${number}`}
                    onClick={() => onSeat(key)}
                  />
                );
              })}
            </div>
            <span className="row-label" aria-hidden="true">{row.label}</span>
          </div>
        ))}
      </div>
    </section>
  );
}

function StandingSection({ section, color, price, props }: { section: PublicSection; color: string; price: number; props: Props }) {
  const free = props.availability?.sections.find((s) => s.key === section.key)?.available ?? 0;
  const quantity = props.selection.standing[section.key] ?? 0;
  return (
    <section className="map-section standing" aria-label={section.name}>
      <div className="map-section-head">
        <span className="dot" style={{ background: color }} />
        <strong>{section.name}</strong>
        <span className="muted small">{shortPrice(price)} · {free > 0 ? `${plural(free, "place")} left` : "sold out"}</span>
      </div>
      <div className="stepper" role="group" aria-label={`Places in ${section.name}`}>
        <button type="button" className="icon-button" onClick={() => props.onStanding(section.key, quantity - 1, free)} disabled={props.disabled || quantity === 0} aria-label={`One fewer in ${section.name}`}><Minus size={16} /></button>
        <output aria-live="polite">{quantity}</output>
        <button type="button" className="icon-button" onClick={() => props.onStanding(section.key, quantity + 1, free)} disabled={props.disabled || free === 0} aria-label={`One more in ${section.name}`}><Plus size={16} /></button>
      </div>
    </section>
  );
}

/** The floor plan as buyers see it: the stage at the top, then each section in the order the venue lists them. */
export function SeatMap(props: Props) {
  const { event } = props;
  return (
    <div className="seat-map">
      <div className="stage" aria-hidden="true">Stage</div>
      {event.sections.map((section) => {
        const color = colorOf(event, section.categoryId);
        const price = event.categories.find((c) => c.id === section.categoryId)?.price ?? 0;
        return section.kind === "Seated"
          ? <SeatedSection key={section.key} section={section} color={color} price={price} props={props} />
          : <StandingSection key={section.key} section={section} color={color} price={price} props={props} />;
      })}
    </div>
  );
}
