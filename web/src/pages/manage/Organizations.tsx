import { Building2, ChevronRight, Plus } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api } from "../../api";
import { Empty } from "../../components/States";
import { useAccount } from "../../context";
import { initials, plural } from "../../format";
import { linkProps, navigate } from "../../router";

export function Organizations() {
  const { me, refresh } = useAccount();
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Most people run one organizer: arriving here, go straight to it.
  useEffect(() => {
    if (me.organizations.length === 1) navigate(`/manage/${me.organizations[0]!.id}`, true);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  async function create(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await api.createOrganization(name);
      await refresh();
      navigate(`/manage/${created.id}/venues/new`);
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <div className="narrow-page">
      <div className="page-head"><div><h1>Sell tickets</h1><p className="muted">Put events on sale, follow the takings and check tickets at the door.</p></div></div>
      {me.organizations.length === 0 ? (
        <section className="card section">
          <Empty title="Start selling" icon={<Building2 size={26} />}>
            <span className="muted measure">Create an organizer for your venue, festival or company. You become its owner and can add managers and gate staff.</span>
          </Empty>
        </section>
      ) : (
        <ul className="org-list card">
          {me.organizations.map((o) => (
            <li key={o.id}>
              <a {...linkProps(`/manage/${o.id}`)}>
                <span className="org-avatar">{initials(o.name)}</span>
                <span className="grow"><strong className="block">{o.name}</strong><span className="small muted">{o.role} · {plural(o.events, "event")} · {plural(o.members, "member")}</span></span>
                <ChevronRight size={18} className="muted" />
              </a>
            </li>
          ))}
        </ul>
      )}
      <form className="card section" onSubmit={create}>
        <h2>New organizer</h2>
        <label className="field">Name<input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="Karadeniz Sahne" required minLength={2} maxLength={80} /></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div><button className="button primary" disabled={busy || name.trim().length < 2}><Plus size={16} />Create organizer</button></div>
      </form>
    </div>
  );
}
