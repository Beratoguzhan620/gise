import { UserPlus } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../../api";
import { Failed, SkeletonRows } from "../../components/States";
import { useAccount, useOrg } from "../../context";
import { initials, roleHint } from "../../format";
import { useLoad } from "../../hooks";
import { navigate } from "../../router";
import type { Role } from "../../types";

const roles: Role[] = ["Staff", "Manager", "Owner"];

export function Team() {
  const org = useOrg();
  const { session, refresh } = useAccount();
  const [members, reload] = useLoad(() => api.members(org.id), [org.id]);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Staff");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const owner = org.role === "Owner";

  async function add(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.addMember(org.id, email, role);
      setEmail("");
      reload();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function change(userId: string, next: Role) {
    setError(null);
    try {
      await api.changeRole(org.id, userId, next);
      reload();
      if (userId === session.user.id) await refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function remove(userId: string) {
    setError(null);
    try {
      await api.removeMember(org.id, userId);
      if (userId === session.user.id) {
        await refresh();
        navigate("/manage");
      } else reload();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <>
      <div className="page-head"><div><h2>Team</h2><p className="muted small">Staff check tickets at the gate, managers run events and refunds, owners also manage the team.</p></div></div>
      {owner && (
        <form className="card section add-member" onSubmit={add}>
          <div className="row wrap gap-10">
            <label className="field grow">Email of their Gişe account<input className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required placeholder="kapi@example.com" /></label>
            <label className="field">Role
              <select className="select" value={role} onChange={(e) => setRole(e.target.value as Role)}>{roles.map((r) => <option key={r}>{r}</option>)}</select>
            </label>
            <button className="button primary align-end" disabled={busy}><UserPlus size={16} />Add</button>
          </div>
          <span className="hint">{roleHint[role]}.</span>
        </form>
      )}
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="card table-card">
        {members.state === "failed" && !members.data ? <Failed message={members.error} onRetry={reload} /> : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Member</th><th>Role</th><th /></tr></thead>
              {!members.data ? <SkeletonRows columns={3} rows={3} /> : (
                <tbody>
                  {members.data.map((m) => (
                    <tr key={m.userId}>
                      <td><div className="row gap-10"><span className="avatar">{initials(m.displayName)}</span><span className="min0"><strong className="block ellipsis">{m.displayName}{m.userId === session.user.id && <span className="muted"> (you)</span>}</strong><span className="small muted ellipsis block">{m.email}</span></span></div></td>
                      <td>
                        {owner ? (
                          <select className="select narrow" value={m.role} onChange={(e) => change(m.userId, e.target.value as Role)} aria-label={`Role of ${m.displayName}`}>
                            {roles.map((r) => <option key={r}>{r}</option>)}
                          </select>
                        ) : <span className="badge plain">{m.role}</span>}
                      </td>
                      <td className="actions">
                        {(owner || m.userId === session.user.id) && <button className="button small danger" onClick={() => remove(m.userId)}>{m.userId === session.user.id ? "Leave" : "Remove"}</button>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
          </div>
        )}
      </div>
    </>
  );
}
