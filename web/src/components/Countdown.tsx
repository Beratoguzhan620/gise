import { Timer } from "lucide-react";
import { useEffect } from "react";
import { clock } from "../format";
import { useNow } from "../hooks";

/** Time left on a hold or a payment, turning red in the last minute. Calls onEnd once when it runs out. */
export function Countdown({ until, label, onEnd }: { until: string; label: string; onEnd?: () => void }) {
  const now = useNow();
  const left = new Date(until).getTime() - now;
  useEffect(() => {
    if (left <= 0) onEnd?.();
  }, [left <= 0]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <span className={`countdown${left < 60_000 ? " urgent" : ""}`} role="timer" aria-live="off">
      <Timer size={15} />
      {label} <strong>{clock(left)}</strong>
    </span>
  );
}
