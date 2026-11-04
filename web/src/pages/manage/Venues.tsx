import { Building2, Plus } from "lucide-react";
import { api } from "../../api";
import { Empty, Failed, SkeletonRows } from "../../components/States";
import { useOrg } from "../../context";
import { canManage, plural } from "../../format";
import { useLoad } from "../../hooks";
import { linkProps, navigate } from "../../router";

export function Venues() {
  const org = useOrg();
  const [venues, reload] = useLoad(() => api.venues(org.id), [org.id]);
  const manager = canManage(org.role);

  return (
    <>
      <div className="page-head">
        <div><h2>Venues</h2><p className="muted small">Each venue has a floor plan; events copy it when they go on sale.</p></div>
        {manager && <a className="button primary" {...linkProps(`/manage/${org.id}/venues/new`)}><Plus size={16} />New venue</a>}
      </div>
      <div className="card table-card">
        {venues.state === "failed" && !venues.data ? <Failed message={venues.error} onRetry={reload} /> : venues.data?.length === 0 ? (
          <Empty title="No venues yet" icon={<Building2 size={26} />}>
            <span className="muted">Add the hall, club or stage where your events happen.</span>
            {manager && <a className="button primary" {...linkProps(`/manage/${org.id}/venues/new`)}><Plus size={16} />New venue</a>}
          </Empty>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Venue</th><th>Places</th><th className="hide-sm">Sections</th><th className="hide-sm">Events</th></tr></thead>
              {!venues.data ? <SkeletonRows columns={4} rows={3} /> : (
                <tbody>
                  {venues.data.map((v) => (
                    <tr key={v.id} className="clickable" onClick={() => navigate(`/manage/${org.id}/venues/${v.id}`)}>
                      <td><a className="strong-link" {...linkProps(`/manage/${org.id}/venues/${v.id}`)} onClick={(e) => e.stopPropagation()}>{v.name}</a><span className="small muted block">{v.city}</span></td>
                      <td>{v.capacity.toLocaleString("en-GB")}</td>
                      <td className="hide-sm small muted">{v.sections.map((s) => s.name).join(", ")}</td>
                      <td className="hide-sm">{plural(v.events, "event")}</td>
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
