// A sale opening, measured against the running stack: everyone at once for the same seats, then paying, then the gate.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 HOLDS_PER_MINUTE=100000 docker compose up --build -d
//        node tests/load/load.mjs [buyers=1000] [concurrency=64]
const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const BUYERS = Number(process.argv[2] ?? 1000);
const CONCURRENCY = Number(process.argv[3] ?? 64);
const run = Date.now().toString(36);
const password = "load-test-password";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function call(path, token, body, method = body ? "POST" : "GET") {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    redirect: "manual",
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  const ms = performance.now() - started;
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, ms };
}

async function must(path, token, body, method) {
  const r = await call(path, token, body, method);
  if (r.status >= 400) throw new Error(`${method ?? (body ? "POST" : "GET")} ${path}: ${r.status} ${JSON.stringify(r.json)}`);
  return r.json;
}

/** Runs tasks with at most `limit` in flight; returns their results in order. */
async function pool(items, limit, task) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await task(items[i], i);
    }
  }));
  return results;
}

function stats(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const at = (p) => sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))];
  return { count: sorted.length, p50: Math.round(at(50)), p95: Math.round(at(95)), p99: Math.round(at(99)), max: Math.round(sorted.at(-1) ?? 0) };
}

const report = (name, data) => console.log(`${name.padEnd(28)} ${JSON.stringify(data)}`);

// The organizer, a 500-seat hall with a 200-place standing floor, and an event on sale.
const owner = await must("/api/auth/register", null, { email: `owner.${run}@example.com`, displayName: "Yük Testi", password });
const token = owner.accessToken;
const org = await must("/api/organizations", token, { name: "Yük Testi Sahne" });
const rows = Array.from({ length: 20 }, (_, i) => ({ label: "ABCDEFGHIJKLMNOPQRST"[i], seats: 25 }));
const venue = await must(`/api/organizations/${org.id}/venues`, token, {
  name: "Büyük Salon", city: "Samsun", sections: [{ key: "A", name: "Salon", kind: "Seated", rows }, { key: "floor", name: "Ayakta", kind: "Standing", capacity: 200 }],
});
const startsAt = new Date(Date.now() + 20 * 86_400_000).toISOString();
const draft = await must(`/api/organizations/${org.id}/events`, token, {
  venueId: venue.id, title: `Yük testi ${run}`, startsAt, saleStartsAt: new Date(Date.now() - 60_000).toISOString(), maxTicketsPerBuyer: 4,
  categories: [{ name: "Salon", price: 500 }, { name: "Ayakta", price: 250 }], pricing: [{ section: "A", category: "Salon" }, { section: "floor", category: "Ayakta" }],
});
const publish = await call(`/api/organizations/${org.id}/events/${draft.id}/publish`, token, {});
report("publish 700 places", { ms: Math.round(publish.ms) });
const eventId = draft.id;

const registered = await pool(Array.from({ length: BUYERS }, (_, i) => i), 16, async (i) =>
  (await must("/api/auth/register", null, { email: `buyer${i}.${run}@example.com`, displayName: `Alıcı ${i}`, password })).accessToken);
report("buyers registered", { count: registered.length });

// 1. Everyone wants two seats at once, most of them in the first five rows.
const front = "ABCDE";
const all = "ABCDEFGHIJKLMNOPQRST";
const wanted = registered.map((_, i) => {
  const row = Math.random() < 0.7 ? front[Math.floor(Math.random() * front.length)] : all[Math.floor(Math.random() * all.length)];
  const first = 1 + Math.floor(Math.random() * 24);
  return [{ section: "A", row, number: first }, { section: "A", row, number: first + 1 }];
});
let started = performance.now();
const seatRush = await pool(registered, CONCURRENCY, (buyer, i) => call(`/api/events/${eventId}/holds`, buyer, { seats: wanted[i] }));
const seatSeconds = (performance.now() - started) / 1000;
const seatWins = seatRush.filter((r) => r.status === 201);
const heldSeats = seatWins.flatMap((r) => r.json.lines.map((l) => l.placeId));
report("seat rush", {
  requests: seatRush.length, won: seatWins.length, conflicts: seatRush.filter((r) => r.status === 409).length,
  other: seatRush.filter((r) => r.status !== 201 && r.status !== 409).map((r) => r.status).slice(0, 5), perSecond: Math.round(seatRush.length / seatSeconds), ...stats(seatRush.map((r) => r.ms)),
});
report("seats held twice", { seats: heldSeats.length, distinct: new Set(heldSeats).size });

// 2. Everyone without seats goes for the 200 standing places, one each.
const losers = registered.filter((_, i) => seatRush[i].status !== 201);
started = performance.now();
const floorRush = await pool(losers, CONCURRENCY, (buyer) => call(`/api/events/${eventId}/holds`, buyer, { standing: [{ section: "floor", quantity: 1 }] }));
const floorWins = floorRush.filter((r) => r.status === 201);
report("standing rush", {
  requests: floorRush.length, won: floorWins.length, soldOut: floorRush.filter((r) => r.status === 409).length,
  perSecond: Math.round(floorRush.length / ((performance.now() - started) / 1000)), ...stats(floorRush.map((r) => r.ms)),
});

// 3. The seat map while that happens: everyone refreshing it.
const reads = await pool(Array.from({ length: 2000 }, (_, i) => i), CONCURRENCY, () => call(`/api/events/${eventId}/availability`));
report("seat map reads", { errors: reads.filter((r) => r.status !== 200).length, ...stats(reads.map((r) => r.ms)) });

// 4. Every winner checks out and pays; the webhook settles each order.
const winners = [
  ...registered.filter((_, i) => seatRush[i].status === 201).map((buyer, i) => ({ buyer, hold: seatWins[i].json })),
  ...losers.filter((_, i) => floorRush[i].status === 201).map((buyer, i) => ({ buyer, hold: floorWins[i].json })),
];
const settled = await pool(winners, CONCURRENCY, async ({ buyer, hold }) => {
  const checkout = await call(`/api/holds/${hold.id}/checkout`, buyer, {});
  const paymentId = checkout.json.checkoutUrl.split("/").pop();
  const paidAt = performance.now();
  await call(`/sandbox/checkout/${paymentId}/pay`, null, {}, "POST");
  for (let i = 0; i < 100; i++) {
    const order = await call(`/api/orders/${checkout.json.id}`, buyer);
    if (order.json.status === "Paid") return { checkout: checkout.ms, settle: performance.now() - paidAt, order: order.json };
    await sleep(100);
  }
  return { checkout: checkout.ms, settle: null, order: null };
});
const paid = settled.filter((s) => s.order);
report("checkout", stats(settled.map((s) => s.checkout)));
report("payment to paid (webhook)", { unsettled: settled.length - paid.length, ...stats(paid.map((s) => s.settle)) });

const sales = await must(`/api/organizations/${org.id}/events/${eventId}/sales`, token);
const map = await must(`/api/events/${eventId}/availability`);
report("books", {
  ticketsSold: sales.ticketsSold, placesHeldByWinners: heldSeats.length + floorWins.length, available: map.available,
  soldPlusAvailable: sales.ticketsSold + map.available, capacity: 700, net: sales.net,
});

// 5. The gate: two devices sync every ticket, offline backlogs of 200, each ticket scanned at both.
const tickets = [];
for (const { buyer } of winners) tickets.push(...(await must("/api/tickets", buyer)).filter((t) => t.eventId === eventId).map((t) => t.code));
const scans = (gate) => tickets.map((code, i) => ({ scanId: crypto.randomUUID(), code, gate, device: gate, scannedAt: new Date(Date.now() - (tickets.length - i) * 1000).toISOString() }));
const outcomes = {};
const batches = [];
for (const gate of ["Kuzey", "Güney"]) {
  const list = scans(gate);
  for (let i = 0; i < list.length; i += 200) {
    const r = await call(`/api/organizations/${org.id}/events/${eventId}/check-ins`, token, { scans: list.slice(i, i + 200) });
    batches.push(r.ms);
    for (const result of r.json) outcomes[result.outcome] = (outcomes[result.outcome] ?? 0) + 1;
  }
}
report("gate sync (200 per batch)", { tickets: tickets.length, outcomes, ...stats(batches), perScan: Math.round((batches.reduce((a, b) => a + b, 0) / (tickets.length * 2)) * 10) / 10 });

// 6. A waiting room: everyone joins before the sale opens.
const queued = await must(`/api/organizations/${org.id}/events`, token, {
  venueId: venue.id, title: `Kuyruk ${run}`, startsAt, saleStartsAt: new Date(Date.now() + 3_600_000).toISOString(), waitingRoom: true, admitPerMinute: 100,
  categories: [{ name: "Salon", price: 500 }, { name: "Ayakta", price: 250 }], pricing: [{ section: "A", category: "Salon" }, { section: "floor", category: "Ayakta" }],
});
await must(`/api/organizations/${org.id}/events/${queued.id}/publish`, token, {});
const joins = await pool(registered, CONCURRENCY, (buyer) => call(`/api/events/${queued.id}/queue`, buyer, {}));
const positions = await pool(registered, CONCURRENCY, async (buyer) => (await call(`/api/events/${queued.id}/queue`, buyer)).json.position);
report("waiting room join", { errors: joins.filter((r) => r.status !== 200).length, distinctPositions: new Set(positions).size, ...stats(joins.map((r) => r.ms)) });
