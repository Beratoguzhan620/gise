import type { CheckInOutcome, EventStatus, OrderStatus, SaleState } from "../types";
import { orderLabel, outcomeLabel, saleLabel } from "../format";

const saleTone: Record<SaleState, string> = { Draft: "plain", Upcoming: "info", OnSale: "ok", SoldOut: "plain", Ended: "plain", Cancelled: "danger" };
const orderTone: Record<OrderStatus, string> = { PendingPayment: "info", Paid: "ok", Failed: "danger", Expired: "plain", RefundPending: "info", Refunded: "plain" };
const statusTone: Record<EventStatus, string> = { Draft: "plain", Published: "ok", Cancelled: "danger" };
const outcomeTone: Record<CheckInOutcome, string> = { Admitted: "ok", AlreadyUsed: "info", Void: "danger", WrongEvent: "danger", Invalid: "danger" };

export const SaleBadge = ({ state }: { state: SaleState }) => <span className={`badge ${saleTone[state]}`}>{saleLabel[state]}</span>;
export const OrderBadge = ({ status }: { status: OrderStatus }) => <span className={`badge ${orderTone[status]}`}>{orderLabel[status]}</span>;
export const StatusBadge = ({ status }: { status: EventStatus }) => <span className={`badge ${statusTone[status]}`}>{status}</span>;
export const OutcomeBadge = ({ outcome }: { outcome: CheckInOutcome }) => <span className={`badge ${outcomeTone[outcome]}`}>{outcomeLabel[outcome]}</span>;

/** A thin bar for "sold of capacity". */
export function Meter({ value, max, label }: { value: number; max: number; label: string }) {
  const share = max > 0 ? Math.min(100, (value / max) * 100) : 0;
  return (
    <div className="meter" role="meter" aria-valuemin={0} aria-valuemax={max} aria-valuenow={value} aria-label={label}>
      <span style={{ width: `${share}%` }} />
    </div>
  );
}
