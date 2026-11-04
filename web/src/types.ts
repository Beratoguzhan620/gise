export type Role = "Staff" | "Manager" | "Owner";
export type SectionKind = "Seated" | "Standing";
export type EventStatus = "Draft" | "Published" | "Cancelled";
export type SaleState = "Draft" | "Upcoming" | "OnSale" | "SoldOut" | "Ended" | "Cancelled";
export type HoldStatus = "Active" | "Released" | "Expired" | "Ordered";
export type OrderStatus = "PendingPayment" | "Paid" | "Failed" | "Expired" | "RefundPending" | "Refunded";
export type RefundReason = "Buyer" | "Organizer" | "EventCancelled" | "LatePayment";
export type TicketStatus = "Valid" | "Void";
export type CheckInOutcome = "Admitted" | "AlreadyUsed" | "Void" | "WrongEvent" | "Invalid";

export interface User { id: string; email: string; displayName: string }
export interface Session { accessToken: string; expiresAt: string; user: User }
export interface Organization { id: string; name: string; role: Role; members: number; venues: number; events: number; createdAt: string }
export interface Me { user: User; organizations: Organization[] }
export interface Member { userId: string; displayName: string; email: string; role: Role; joinedAt: string }
export interface Page<T> { items: T[]; total: number; pageNumber: number; pageSize: number }

export interface Row { label: string; seats: number }
export interface Section { key: string; name: string; kind: SectionKind; places: number; rows: Row[]; capacity: number }
export interface Venue { id: string; name: string; city: string; address: string; capacity: number; sections: Section[]; events: number; createdAt: string }
export interface SectionInput { key: string; name: string; kind: SectionKind; rows: Row[]; capacity: number }
export interface VenueInput { name: string; city: string; address: string; sections: SectionInput[] }

export interface Category { id: string; name: string; price: number }
export interface Inventory { available: number; held: number; sold: number }

export interface OrganizerEvent {
  id: string; organizationId: string; venueId: string; title: string; description: string; venueName: string; city: string; address: string;
  startsAt: string; saleStartsAt: string; status: EventStatus; saleState: SaleState; maxTicketsPerBuyer: number; refundCutoffHours: number | null;
  waitingRoom: boolean; admitPerMinute: number; currency: string; categories: Category[]; pricing: { section: string; categoryId: string }[];
  layout: Section[]; capacity: number; inventory: Inventory; createdAt: string; publishedAt: string | null; cancelledAt: string | null;
}

export interface EventInput {
  venueId: string; title: string; description: string; startsAt: string; saleStartsAt: string; maxTicketsPerBuyer: number;
  refundCutoffHours: number | null; waitingRoom: boolean; admitPerMinute: number;
  categories: { name: string; price: number }[]; pricing: { section: string; category: string }[];
}

export interface EventSummary {
  id: string; title: string; organizer: string; venueName: string; city: string; startsAt: string; saleStartsAt: string; saleState: SaleState;
  priceFrom: number; currency: string; available: number; capacity: number; waitingRoom: boolean;
}

export interface PublicSection { key: string; name: string; kind: SectionKind; categoryId: string; places: number; rows: Row[]; capacity: number }

export interface EventDetail {
  id: string; title: string; description: string; organizer: string; venueName: string; city: string; address: string; startsAt: string;
  saleStartsAt: string; saleState: SaleState; maxTicketsPerBuyer: number; refundDeadline: string | null; waitingRoom: boolean; currency: string;
  categories: Category[]; sections: PublicSection[]; available: number; capacity: number;
}

export interface SectionAvailability { key: string; kind: SectionKind; available: number; places: number; rows: { label: string; seats: string }[] }
export interface Availability { eventId: string; saleState: SaleState; available: number; sections: SectionAvailability[]; checkedAt: string }

export interface QueueStatus {
  eventId: string; waitingRoom: boolean; admitted: boolean; position: number | null; ahead: number | null; saleStartsAt: string;
  admittedAt: string | null; shopUntil: string | null; estimatedWaitSeconds: number | null; admitPerMinute: number;
}

export interface Line { placeId: string; section: string; sectionName: string; row: string | null; number: number; categoryId: string; category: string; price: number }
export interface Hold { id: string; eventId: string; status: HoldStatus; expiresAt: string; lines: Line[]; total: number; currency: string }

export interface Order {
  id: string; number: string; status: OrderStatus; event: { id: string; title: string; venueName: string; city: string; startsAt: string; status: EventStatus };
  lines: Line[]; amount: number; currency: string; createdAt: string; paymentDeadline: string; checkoutUrl: string | null; paidAt: string | null;
  refundReason: RefundReason | null; refundRequestedAt: string | null; refundedAt: string | null; canRefund: boolean; refundDeadline: string | null;
}

export interface OrderSummary {
  id: string; number: string; status: OrderStatus; eventId: string; eventTitle: string; venueName: string; startsAt: string; tickets: number;
  amount: number; currency: string; createdAt: string;
}

export interface MyTicket {
  id: string; orderId: string; orderNumber: string; eventId: string; eventTitle: string; venueName: string; city: string; startsAt: string;
  label: string; section: string; row: string | null; number: number; category: string; price: number; holder: string; status: TicketStatus;
  code: string | null; checkedInAt: string | null;
}

export interface OrganizerOrder {
  id: string; number: string; status: OrderStatus; buyerName: string; buyerEmail: string; tickets: number; amount: number; currency: string;
  createdAt: string; paidAt: string | null; refundReason: RefundReason | null;
}

export interface SalesSummary {
  eventId: string; currency: string; gross: number; refunded: number; net: number; paidOrders: number; ticketsSold: number; pendingOrders: number;
  refundedOrders: number; categories: { id: string; name: string; price: number; capacity: number; sold: number; held: number; available: number; revenue: number }[];
  days: { date: string; tickets: number; revenue: number }[];
}

export interface GateTicket { id: string; label: string; category: string; holder: string; status: TicketStatus; checkedInAt: string | null; gate: string | null }
export interface GatePackage { eventId: string; title: string; startsAt: string; venueName: string; keyId: string; publicKey: string; tickets: GateTicket[]; generatedAt: string }
export interface ScanResult { scanId: string; outcome: CheckInOutcome; ticket: GateTicket | null; scannedAt: string; firstCheckedInAt: string | null; firstGate: string | null }
export interface CheckInSummary {
  eventId: string; tickets: number; admitted: number; remaining: number; gates: { gate: string; admitted: number }[];
  repeats: { ticketId: string; label: string; holder: string; firstAt: string; firstGate: string | null; againAt: string; againGate: string; againDevice: string }[];
  recent: { scanId: string; outcome: CheckInOutcome; label: string | null; gate: string; device: string; scannedAt: string }[]; rejected: number;
}
