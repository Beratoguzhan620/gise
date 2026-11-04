import { useEffect, useState } from "react";

/** The pages of the app. Ids are in the address, so every page can be bookmarked and shared. */
export type Route =
  | { page: "catalog"; q?: string; city?: string }
  | { page: "event"; eventId: string }
  | { page: "order"; orderId: string }
  | { page: "tickets" }
  | { page: "signin"; next?: string }
  | { page: "register"; next?: string }
  | { page: "manage" }
  | { page: "org-events"; orgId: string }
  | { page: "event-new"; orgId: string }
  | { page: "event-manage"; orgId: string; eventId: string }
  | { page: "event-edit"; orgId: string; eventId: string }
  | { page: "venues"; orgId: string }
  | { page: "venue-new"; orgId: string }
  | { page: "venue-edit"; orgId: string; venueId: string }
  | { page: "team"; orgId: string }
  | { page: "gate"; orgId: string; eventId: string }
  | { page: "not-found" };

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isId = (value: string | undefined): value is string => !!value && guid.test(value);

/** Only addresses within the app are followed after signing in. */
function safeNext(value: string | null): string | undefined {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : undefined;
}

export function parseRoute(pathname: string, search = ""): Route {
  const params = new URLSearchParams(search);
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const [a, b, c, d, e] = parts;
  const n = parts.length;

  if (n === 0) return { page: "catalog", q: params.get("q") ?? undefined, city: params.get("city") ?? undefined };
  if (n === 1 && a === "signin") return { page: "signin", next: safeNext(params.get("next")) };
  if (n === 1 && a === "register") return { page: "register", next: safeNext(params.get("next")) };
  if (n === 1 && a === "tickets") return { page: "tickets" };
  if (n === 2 && a === "events" && isId(b)) return { page: "event", eventId: b };
  if (n === 2 && a === "orders" && isId(b)) return { page: "order", orderId: b };
  if (n === 3 && a === "gate" && isId(b) && isId(c)) return { page: "gate", orgId: b, eventId: c };
  if (a !== "manage") return { page: "not-found" };
  if (n === 1) return { page: "manage" };
  if (!isId(b)) return { page: "not-found" };
  if (n === 2) return { page: "org-events", orgId: b };
  if (c === "events" && n === 4 && d === "new") return { page: "event-new", orgId: b };
  if (c === "events" && n === 4 && isId(d)) return { page: "event-manage", orgId: b, eventId: d };
  if (c === "events" && n === 5 && isId(d) && e === "edit") return { page: "event-edit", orgId: b, eventId: d };
  if (c === "venues" && n === 3) return { page: "venues", orgId: b };
  if (c === "venues" && n === 4 && d === "new") return { page: "venue-new", orgId: b };
  if (c === "venues" && n === 4 && isId(d)) return { page: "venue-edit", orgId: b, venueId: d };
  if (c === "team" && n === 3) return { page: "team", orgId: b };
  return { page: "not-found" };
}

/** Pages anyone can open without an account. */
export const isPublic = (route: Route) => ["catalog", "event", "signin", "register", "not-found"].includes(route.page);

export const orgOf = (route: Route): string | undefined => ("orgId" in route ? route.orgId : undefined);

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  window.scrollTo({ top: 0 });
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.search);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }, []);
  return route;
}

/** Props for an anchor that moves within the app without reloading the page. */
export function linkProps(href: string) {
  return {
    href,
    onClick: (e: React.MouseEvent) => {
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      navigate(href);
    },
  };
}

/** The sign-in page, coming back here afterwards. */
export const signInHere = () => `/signin?next=${encodeURIComponent(location.pathname + location.search)}`;
