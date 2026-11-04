import { useState } from "react";
import { plural, price } from "../../format";
import type { SalesSummary } from "../../types";

/**
 * Tickets sold per day: one series, one hue, thin bars with rounded tops anchored to the baseline, a recessive
 * grid, and a tooltip on hover or focus. The same numbers are in a table for screen readers.
 */
export function SalesChart({ days }: { days: SalesSummary["days"] }) {
  const [active, setActive] = useState<number | null>(null);
  if (days.length === 0) return <p className="muted small">No sales yet. Paid orders show up here by day.</p>;

  const max = Math.max(...days.map((d) => d.tickets), 1);
  const top = niceTop(max);
  const height = 160;
  const label = (iso: string) => new Date(`${iso}T00:00:00`).toLocaleDateString("en-GB", { day: "numeric", month: "short" });
  const shown = active === null ? null : days[active]!;

  return (
    <figure className="chart">
      <figcaption className="small muted">Tickets sold per day</figcaption>
      <div className="chart-plot" style={{ height }}>
        {[0, 0.5, 1].map((f) => (
          <div key={f} className="gridline" style={{ bottom: `${f * 100}%` }}><span>{Math.round(top * f)}</span></div>
        ))}
        <div className="bars" onMouseLeave={() => setActive(null)}>
          {days.map((d, i) => (
            <button
              key={d.date}
              type="button"
              className={`bar-hit${active === i ? " active" : ""}`}
              onMouseEnter={() => setActive(i)}
              onFocus={() => setActive(i)}
              onBlur={() => setActive(null)}
              aria-label={`${label(d.date)}: ${plural(d.tickets, "ticket")}, ${price(d.revenue)}`}
            >
              <span className="bar-mark" style={{ height: `${(d.tickets / top) * 100}%` }} />
            </button>
          ))}
        </div>
        {shown && (
          <div className="chart-tip" role="status" style={{ left: `${((active! + 0.5) / days.length) * 100}%` }}>
            <strong>{label(shown.date)}</strong>
            <span>{plural(shown.tickets, "ticket")}</span>
            <span className="muted">{price(shown.revenue)}</span>
          </div>
        )}
      </div>
      <div className="chart-axis" aria-hidden="true">
        <span>{label(days[0]!.date)}</span>
        {days.length > 1 && <span>{label(days.at(-1)!.date)}</span>}
      </div>
      <table className="sr-only">
        <caption>Tickets sold per day</caption>
        <thead><tr><th>Day</th><th>Tickets</th><th>Takings</th></tr></thead>
        <tbody>{days.map((d) => <tr key={d.date}><td>{label(d.date)}</td><td>{d.tickets}</td><td>{price(d.revenue)}</td></tr>)}</tbody>
      </table>
    </figure>
  );
}

/** A round number at or above the highest bar, so the grid reads 0 / half / top in whole tickets. */
function niceTop(max: number): number {
  if (max <= 4) return 4;
  const magnitude = 10 ** Math.floor(Math.log10(max));
  const step = [1, 2, 2.5, 5, 10].find((s) => s * magnitude >= max)!;
  return step * magnitude;
}
