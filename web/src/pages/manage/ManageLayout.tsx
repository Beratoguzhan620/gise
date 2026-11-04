import { Building2, CalendarDays, Users } from "lucide-react";
import type { ReactNode } from "react";
import { useAccount, useOrg } from "../../context";
import { initials } from "../../format";
import { linkProps, navigate, type Route } from "../../router";

/** The organizer's area: which organizer, and its three parts. */
export function ManageLayout({ route, children }: { route: Route; children: ReactNode }) {
  const org = useOrg();
  const { me } = useAccount();
  const base = `/manage/${org.id}`;
  const tab = route.page === "venues" || route.page === "venue-new" || route.page === "venue-edit" ? "venues" : route.page === "team" ? "team" : "events";

  return (
    <>
      <div className="manage-head">
        <span className="org-avatar">{initials(org.name)}</span>
        <div className="grow">
          {me.organizations.length > 1 ? (
            <label>
              <span className="sr-only">Organizer</span>
              <select className="org-select" value={org.id} onChange={(e) => navigate(`/manage/${e.target.value}`)}>
                {me.organizations.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
              </select>
            </label>
          ) : <h1 className="org-name">{org.name}</h1>}
          <span className="small muted">{org.role}</span>
        </div>
      </div>
      <nav className="tabs-line" aria-label="Organizer">
        <a {...linkProps(base)} className={tab === "events" ? "active" : ""} aria-current={tab === "events" ? "page" : undefined}><CalendarDays size={16} />Events</a>
        <a {...linkProps(`${base}/venues`)} className={tab === "venues" ? "active" : ""} aria-current={tab === "venues" ? "page" : undefined}><Building2 size={16} />Venues</a>
        <a {...linkProps(`${base}/team`)} className={tab === "team" ? "active" : ""} aria-current={tab === "team" ? "page" : undefined}><Users size={16} />Team</a>
      </nav>
      {children}
    </>
  );
}
