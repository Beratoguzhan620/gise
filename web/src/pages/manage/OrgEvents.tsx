import { CalendarPlus, ScanLine } from "lucide-react";
import { api } from "../../api";
import { Meter, SaleBadge } from "../../components/Badges";
import { Empty, Failed, SkeletonRows } from "../../components/States";
import { useOrg } from "../../context";
import { canManage, plural, when } from "../../format";
import { useLoad } from "../../hooks";
import { linkProps, navigate } from "../../router";

export function OrgEvents() {
  const org = useOrg();
  const [events, reload] = useLoad(() => api.organizerEvents(org.id), [org.id]);
  const manager = canManage(org.role);

  return (
    <>
      <div className="page-head">
        <div><h2>Events</h2><p className="muted small">Drafts stay private until you publish them.</p></div>
        {manager && <a className="button primary" {...linkProps(`/manage/${org.id}/events/new`)}><CalendarPlus size={16} />New event</a>}
      </div>
      <div className="card table-card">
        {events.state === "failed" && !events.data ? <Failed message={events.error} onRetry={reload} /> : events.data?.length === 0 ? (
          <Empty title="No events yet" icon={<CalendarPlus size={26} />}>
            <span className="muted">{manager ? "Add a venue first, then create an event and publish it." : "A manager adds events here."}</span>
          </Empty>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Event</th><th className="hide-sm">When</th><th>Sales</th><th className="hide-sm">Sold</th><th /></tr></thead>
              {!events.data ? <SkeletonRows columns={5} /> : (
                <tbody>
                  {events.data.map((e) => (
                    <tr key={e.id} className="clickable" onClick={() => navigate(`/manage/${org.id}/events/${e.id}`)}>
                      <td><a className="strong-link" {...linkProps(`/manage/${org.id}/events/${e.id}`)} onClick={(ev) => ev.stopPropagation()}>{e.title}</a><span className="small muted block">{e.venueName}, {e.city}</span></td>
                      <td className="hide-sm nowrap">{when(e.startsAt)}</td>
                      <td><SaleBadge state={e.saleState} /></td>
                      <td className="hide-sm sold-cell">
                        <span className="small">{plural(e.inventory.sold, "ticket")} of {e.capacity.toLocaleString("en-GB")}</span>
                        <Meter value={e.inventory.sold} max={e.capacity} label={`${e.title}: sold`} />
                      </td>
                      <td className="actions">
                        {e.status === "Published" && <a className="button small" {...linkProps(`/gate/${org.id}/${e.id}`)} onClick={(ev) => ev.stopPropagation()}><ScanLine size={14} />Gate</a>}
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
