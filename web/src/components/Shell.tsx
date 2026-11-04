import { LogOut, Menu, Ticket, X } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { useApp } from "../context";
import { initials } from "../format";
import { linkProps, signInHere, type Route } from "../router";

function UserMenu() {
  const { account } = useApp();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  if (!account) {
    return <a className="button small" {...linkProps(signInHere())}>Sign in</a>;
  }

  return (
    <div className="user-menu" ref={ref}>
      <button className="avatar-button" onClick={() => setOpen(!open)} aria-expanded={open} aria-haspopup="menu" aria-label={`Account: ${account.session.user.displayName}`}>
        <span className="avatar">{initials(account.session.user.displayName)}</span>
      </button>
      {open && (
        <div className="menu" role="menu">
          <div className="menu-head">
            <strong className="ellipsis block">{account.session.user.displayName}</strong>
            <span className="small muted ellipsis block">{account.session.user.email}</span>
          </div>
          <a role="menuitem" className="menu-item" {...linkProps("/tickets")} onClick={(e) => { linkProps("/tickets").onClick(e); setOpen(false); }}>My tickets</a>
          <a role="menuitem" className="menu-item" {...linkProps("/manage")} onClick={(e) => { linkProps("/manage").onClick(e); setOpen(false); }}>Sell tickets</a>
          <div className="menu-divider" />
          <button role="menuitem" className="menu-item" onClick={account.signOut}><LogOut size={15} />Sign out</button>
        </div>
      )}
    </div>
  );
}

const sections = (route: Route) => ({
  events: route.page === "catalog" || route.page === "event",
  tickets: route.page === "tickets" || route.page === "order",
  manage: route.page.startsWith("manage") || ["org-events", "event-new", "event-manage", "event-edit", "venues", "venue-new", "venue-edit", "team"].includes(route.page),
});

export function Shell({ route, children, wide = false }: { route: Route; children: ReactNode; wide?: boolean }) {
  const { account } = useApp();
  const [menu, setMenu] = useState(false);
  const active = sections(route);
  useEffect(() => setMenu(false), [route]);

  const links = (
    <>
      <a {...linkProps("/")} className={`top-link${active.events ? " active" : ""}`} aria-current={active.events ? "page" : undefined}>Events</a>
      {account && <a {...linkProps("/tickets")} className={`top-link${active.tickets ? " active" : ""}`} aria-current={active.tickets ? "page" : undefined}>My tickets</a>}
      {account && <a {...linkProps("/manage")} className={`top-link${active.manage ? " active" : ""}`} aria-current={active.manage ? "page" : undefined}>Sell tickets</a>}
    </>
  );

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-inner">
          <a className="brand" {...linkProps("/")} aria-label="Gişe, all events"><span className="brand-mark"><Ticket size={16} /></span>Gişe</a>
          <nav className="top-links" aria-label="Main">{links}</nav>
          <div className="grow" />
          <UserMenu />
          <button className="icon-button only-sm" onClick={() => setMenu(!menu)} aria-label={menu ? "Close menu" : "Open menu"} aria-expanded={menu}>
            {menu ? <X size={20} /> : <Menu size={20} />}
          </button>
        </div>
        {menu && <nav className="mobile-links" aria-label="Main">{links}</nav>}
      </header>
      <main className={`page${wide ? " wide" : ""}`}>{children}</main>
    </div>
  );
}
