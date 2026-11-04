import { Ticket } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, setToken } from "./api";
import { Shell } from "./components/Shell";
import { Empty, Failed, Loading } from "./components/States";
import { AppCtx, OrgCtx, type Account, type AppContext } from "./context";
import { Register, SignIn } from "./pages/Auth";
import { Catalog } from "./pages/Catalog";
import { EventPage } from "./pages/EventPage";
import { Gate } from "./pages/gate/Gate";
import { EventDashboard } from "./pages/manage/EventDashboard";
import { EventForm } from "./pages/manage/EventForm";
import { ManageLayout } from "./pages/manage/ManageLayout";
import { OrgEvents } from "./pages/manage/OrgEvents";
import { Organizations } from "./pages/manage/Organizations";
import { Team } from "./pages/manage/Team";
import { VenueForm } from "./pages/manage/VenueForm";
import { Venues } from "./pages/manage/Venues";
import { MyTickets } from "./pages/MyTickets";
import { OrderPage } from "./pages/OrderPage";
import { isPublic, linkProps, navigate, orgOf, signInHere, useRoute, type Route } from "./router";
import { loadSession, saveSession } from "./session";
import type { Me, Session } from "./types";

function NotFound() {
  return (
    <Empty title="This page does not exist" icon={<Ticket size={26} />}>
      <span className="muted">Or it belongs to an organizer you are not part of.</span>
      <a className="button" {...linkProps("/")}>See what's on</a>
    </Empty>
  );
}

function OrgPage({ route }: { route: Route }) {
  switch (route.page) {
    case "org-events": return <OrgEvents />;
    case "event-new": return <EventForm />;
    case "event-edit": return <EventForm eventId={route.eventId} />;
    case "event-manage": return <EventDashboard eventId={route.eventId} />;
    case "venues": return <Venues />;
    case "venue-new": return <VenueForm />;
    case "venue-edit": return <VenueForm venueId={route.venueId} />;
    case "team": return <Team />;
    default: return null;
  }
}

function Page({ route, account }: { route: Route; account: Account | null }) {
  switch (route.page) {
    case "catalog": return <Catalog q={route.q} city={route.city} />;
    case "event": return <EventPage eventId={route.eventId} />;
    case "not-found": return <NotFound />;
    default: break;
  }

  if (!account) return <Loading />;
  switch (route.page) {
    case "order": return <OrderPage orderId={route.orderId} />;
    case "tickets": return <MyTickets />;
    case "manage": return <Organizations />;
    default: break;
  }

  const org = account.me.organizations.find((o) => o.id === orgOf(route));
  if (!org) return <NotFound />;
  return (
    <OrgCtx.Provider value={org}>
      {route.page === "gate" ? <Gate key={route.eventId} eventId={route.eventId} /> : <ManageLayout route={route}><OrgPage route={route} /></ManageLayout>}
    </OrgCtx.Provider>
  );
}

export function App() {
  const route = useRoute();
  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signOut = useCallback(() => {
    saveSession(null);
    setToken(null, () => {});
    setSession(null);
    setMe(null);
    navigate("/", true);
  }, []);

  if (session) setToken(session.accessToken, signOut);

  const refresh = useCallback(async () => {
    const value = await api.me();
    setMe(value);
    return value;
  }, []);

  useEffect(() => {
    if (!session) return;
    setError(null);
    refresh().catch((e: Error) => setError(e.message));
  }, [session, refresh]);

  // An organizer in the address that is not among ours yet may be one we were just added to.
  const orgId = orgOf(route);
  useEffect(() => {
    if (me && orgId && !me.organizations.some((o) => o.id === orgId)) void refresh();
  }, [orgId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!session && !isPublic(route)) navigate(signInHere(), true);
    if (session && (route.page === "signin" || route.page === "register")) navigate(route.next ?? "/", true);
  }, [session, route]);

  const signedIn = useCallback((value: Session) => {
    saveSession(value);
    setToken(value.accessToken, signOut);
    setSession(value);
  }, [signOut]);

  const account = useMemo<Account | null>(() => (session && me ? { session, me, refresh, signOut } : null), [session, me, refresh, signOut]);
  const context = useMemo<AppContext>(() => ({ account, signedIn }), [account, signedIn]);

  if (!session && (route.page === "signin" || route.page === "register")) {
    return <AppCtx.Provider value={context}>{route.page === "register" ? <Register next={route.next} /> : <SignIn next={route.next} />}</AppCtx.Provider>;
  }

  if (session && error && !me) return <div className="center"><Failed message={error} onRetry={() => refresh().catch((e: Error) => setError(e.message))} /></div>;

  const wide = route.page === "event" || route.page === "gate";
  return (
    <AppCtx.Provider value={context}>
      <Shell route={route} wide={wide}>
        {session && !me && !isPublic(route) ? <Loading /> : <Page route={route} account={account} />}
      </Shell>
    </AppCtx.Provider>
  );
}
