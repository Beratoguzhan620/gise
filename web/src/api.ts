import type {
  Availability, CheckInSummary, EventDetail, EventInput, EventSummary, GatePackage, Hold, Me, Member, MyTicket, Order, OrderStatus, OrderSummary,
  Organization, OrganizerEvent, OrganizerOrder, Page, QueueStatus, Role, SalesSummary, ScanResult, Session, Venue, VenueInput,
} from "./types";

/** An API error with the stable code from the problem response, such as "place_unavailable". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }

  /** The request never reached the server: the device is offline or the server is down. */
  get offline() {
    return this.status === 0;
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (token && !headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again." : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }

  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown = {}): RequestInit => ({ method: "POST", body: json(body) });
const put = (body: unknown): RequestInit => ({ method: "PUT", body: json(body) });
const org = (id: string) => `/api/organizations/${id}`;
const ev = (orgId: string, eventId: string) => `${org(orgId)}/events/${eventId}`;

export function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && String(value).trim() !== "") search.set(key, String(value).trim());
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

export const api = {
  register: (body: { email: string; displayName: string; password: string }) => request<Session>("/api/auth/register", post(body)),
  login: (email: string, password: string) => request<Session>("/api/auth/login", post({ email, password })),
  me: () => request<Me>("/api/me"),

  events: (params: { q?: string; city?: string; page?: number }) => request<Page<EventSummary>>(`/api/events${query({ ...params, pageSize: 24 })}`),
  event: (id: string) => request<EventDetail>(`/api/events/${id}`),
  availability: (id: string) => request<Availability>(`/api/events/${id}/availability`),
  joinQueue: (id: string) => request<QueueStatus>(`/api/events/${id}/queue`, post()),

  hold: (eventId: string, body: { seats: { section: string; row: string; number: number }[]; standing: { section: string; quantity: number }[] }) =>
    request<Hold>(`/api/events/${eventId}/holds`, post(body)),
  getHold: (id: string) => request<Hold>(`/api/holds/${id}`),
  release: (id: string) => request<void>(`/api/holds/${id}`, { method: "DELETE" }),
  checkout: (id: string) => request<Order>(`/api/holds/${id}/checkout`, post()),

  orders: (page = 1) => request<Page<OrderSummary>>(`/api/orders${query({ page })}`),
  order: (id: string) => request<Order>(`/api/orders/${id}`),
  refund: (id: string) => request<Order>(`/api/orders/${id}/refund`, post()),
  tickets: (past = false) => request<MyTicket[]>(`/api/tickets${past ? "?past=true" : ""}`),
  ticketsPdf: async (orderId: string) => (await send(`/api/orders/${orderId}/tickets.pdf`)).blob(),

  createOrganization: (name: string) => request<Organization>("/api/organizations", post({ name })),
  members: (orgId: string) => request<Member[]>(`${org(orgId)}/members`),
  addMember: (orgId: string, email: string, role: Role) => request<Member>(`${org(orgId)}/members`, post({ email, role })),
  changeRole: (orgId: string, userId: string, role: Role) => request<Member>(`${org(orgId)}/members/${userId}`, { method: "PATCH", body: json({ role }) }),
  removeMember: (orgId: string, userId: string) => request<void>(`${org(orgId)}/members/${userId}`, { method: "DELETE" }),

  venues: (orgId: string) => request<Venue[]>(`${org(orgId)}/venues`),
  createVenue: (orgId: string, body: VenueInput) => request<Venue>(`${org(orgId)}/venues`, post(body)),
  updateVenue: (orgId: string, id: string, body: VenueInput) => request<Venue>(`${org(orgId)}/venues/${id}`, put(body)),
  deleteVenue: (orgId: string, id: string) => request<void>(`${org(orgId)}/venues/${id}`, { method: "DELETE" }),

  organizerEvents: (orgId: string) => request<OrganizerEvent[]>(`${org(orgId)}/events`),
  organizerEvent: (orgId: string, id: string) => request<OrganizerEvent>(ev(orgId, id)),
  createEvent: (orgId: string, body: EventInput) => request<OrganizerEvent>(`${org(orgId)}/events`, post(body)),
  updateEvent: (orgId: string, id: string, body: EventInput) => request<OrganizerEvent>(ev(orgId, id), put(body)),
  deleteEvent: (orgId: string, id: string) => request<void>(ev(orgId, id), { method: "DELETE" }),
  publish: (orgId: string, id: string) => request<OrganizerEvent>(`${ev(orgId, id)}/publish`, post()),
  cancel: (orgId: string, id: string) => request<{ eventId: string; ordersToRefund: number; refunded: number }>(`${ev(orgId, id)}/cancel`, post()),
  sales: (orgId: string, id: string) => request<SalesSummary>(`${ev(orgId, id)}/sales`),
  eventOrders: (orgId: string, id: string, params: { q?: string; status?: OrderStatus | ""; page?: number }) =>
    request<Page<OrganizerOrder>>(`${ev(orgId, id)}/orders${query(params)}`),
  refundOrder: (orgId: string, id: string, orderId: string) => request<OrganizerOrder>(`${ev(orgId, id)}/orders/${orderId}/refund`, post()),

  gate: (orgId: string, id: string) => request<GatePackage>(`${ev(orgId, id)}/gate`),
  scan: (orgId: string, id: string, scans: { scanId: string; code: string; gate: string; device: string; scannedAt: string }[]) =>
    request<ScanResult[]>(`${ev(orgId, id)}/check-ins`, post({ scans })),
  checkIns: (orgId: string, id: string) => request<CheckInSummary>(`${ev(orgId, id)}/check-ins`),
};
