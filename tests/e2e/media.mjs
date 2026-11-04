// Screenshots and walkthrough frames for the README, from the running stack with a few events and sales.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 HOLDS_PER_MINUTE=100000 docker compose up --build -d && node media.mjs <output-dir>
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const MAIL = process.env.MAIL_URL ?? "http://localhost:8025";
const OUT = (process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname).replace(/\/?$/, "/");
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-1";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const days = (n, hour = 20) => { const d = new Date(); d.setUTCDate(d.getUTCDate() + n); d.setUTCHours(hour - 3, 0, 0, 0); return d.toISOString(); };

async function call(path, token, body, method = body ? "POST" : "GET") {
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

async function person(key, name) {
  const email = `${key}.${run}@example.com`;
  const session = await call("/api/auth/register", null, { email, displayName: name, password });
  return { name, email, token: session.accessToken };
}

const deniz = await person("deniz", "Deniz Yılmaz");
const org = await call("/api/organizations", deniz.token, { name: "Karadeniz Sahne" });
const o = `/api/organizations/${org.id}`;
const kapi = await person("kapi", "Murat Kaya");
await call(`${o}/members`, deniz.token, { email: kapi.email, role: "Staff" });
const rows = (count, seats) => Array.from({ length: count }, (_, i) => ({ label: "ABCDEFGHIJKLMNOP"[i], seats }));

const akm = await call(`${o}/venues`, deniz.token, {
  name: "Atatürk Kültür Merkezi", city: "Samsun", address: "Cumhuriyet Mah., Atakum",
  sections: [
    { key: "A", name: "Parter", kind: "Seated", rows: rows(9, 22) },
    { key: "B", name: "Balkon", kind: "Seated", rows: rows(4, 18) },
    { key: "floor", name: "Ayakta", kind: "Standing", capacity: 120 },
  ],
});
const club = await call(`${o}/venues`, deniz.token, {
  name: "Liman Sahnesi", city: "Trabzon", address: "Kemerkaya Mah.",
  sections: [{ key: "floor", name: "Ayakta", kind: "Standing", capacity: 400 }, { key: "L", name: "Loca", kind: "Seated", rows: rows(2, 12) }],
});

async function event(venue, body) {
  const created = await call(`${o}/events`, deniz.token, { venueId: venue.id, maxTicketsPerBuyer: 6, refundCutoffHours: 48, ...body });
  await call(`${o}/events/${created.id}/publish`, deniz.token, {});
  return created;
}

const three = [{ name: "Parter", price: 650 }, { name: "Balkon", price: 420 }, { name: "Ayakta", price: 300 }];
const threePricing = [{ section: "A", category: "Parter" }, { section: "B", category: "Balkon" }, { section: "floor", category: "Ayakta" }];
const kemence = await event(akm, {
  title: "Karadeniz Ezgileri: Kemençe Gecesi", startsAt: days(17), saleStartsAt: days(-3, 10),
  description: "Kemençe, tulum ve kaval ile iki perdelik bir akşam. Ara 20 dakika.\n\nKapılar 19.00'da açılır.",
  categories: three, pricing: threePricing,
});
const festival = await event(club, {
  title: "Fırtına Vadisi Caz Gecesi", startsAt: days(24, 21), saleStartsAt: days(-1, 10), waitingRoom: true, admitPerMinute: 50,
  categories: [{ name: "Ayakta", price: 380 }, { name: "Loca", price: 950 }], pricing: [{ section: "floor", category: "Ayakta" }, { section: "L", category: "Loca" }],
});
await event(akm, {
  title: "Hamlet, iki perde", startsAt: days(31, 20), saleStartsAt: days(-2, 10), refundCutoffHours: null,
  categories: three, pricing: threePricing,
});
const upcoming = await event(club, {
  title: "Yılbaşı Akustik", startsAt: days(56, 22), saleStartsAt: days(0, 23) > new Date().toISOString() ? days(0, 23) : days(1, 12), waitingRoom: true, admitPerMinute: 100,
  categories: [{ name: "Ayakta", price: 450 }, { name: "Loca", price: 1200 }], pricing: [{ section: "floor", category: "Ayakta" }, { section: "L", category: "Loca" }],
});
await event(akm, {
  title: "Çocuk Korosu Yılbaşı Konseri", startsAt: days(40, 15), saleStartsAt: days(-1, 10),
  categories: [{ name: "Herkes", price: 150 }], pricing: [{ section: "A", category: "Herkes" }, { section: "B", category: "Herkes" }, { section: "floor", category: "Herkes" }],
});

async function buy(buyer, eventId, request) {
  const hold = await call(`/api/events/${eventId}/holds`, buyer.token, request);
  const order = await call(`/api/holds/${hold.id}/checkout`, buyer.token, {});
  await fetch(`${BASE}/sandbox/checkout/${order.checkoutUrl.split("/").pop()}/pay`, { method: "POST", redirect: "manual" });
  for (let i = 0; i < 40; i++) {
    const now = await call(`/api/orders/${order.id}`, buyer.token);
    if (now.status === "Paid") return now;
    await sleep(500);
  }
  throw new Error("payment did not settle");
}

// Sales: a scattering of seats in the stalls and balcony, and the standing area.
const names = ["Ayşe Kaya", "Can Demir", "Zeynep Arslan", "Emre Çelik", "Selin Yıldız", "Burak Şahin", "Gizem Aydın", "Okan Kurt", "Ece Öztürk", "Mert Koç", "Derya Polat", "Kaan Erdem"];
const buyers = [];
for (const [i, name] of names.entries()) buyers.push(await person(`buyer${i}`, name));
const picks = [
  ["A", "C", [9, 10, 11, 12]], ["A", "D", [5, 6]], ["A", "A", [10, 11, 12, 13]], ["A", "E", [15, 16, 17]], ["A", "B", [1, 2]], ["B", "A", [8, 9]],
  ["A", "F", [10, 11]], ["A", "G", [3, 4, 5]], ["B", "B", [1, 2, 3, 4]], ["A", "C", [18, 19]], ["A", "H", [11, 12]], ["A", "B", [20, 21, 22]],
];
const orders = [];
for (const [i, [section, row, numbers]] of picks.entries()) {
  orders.push(await buy(buyers[i], kemence.id, { seats: numbers.map((number) => ({ section, row, number })), standing: i % 3 === 0 ? [{ section: "floor", quantity: 2 }] : [] }));
}
await call(`/api/orders/${orders[4].id}/refund`, buyers[4].token, {});

// Gate: a few people already in, one ticket used twice.
const codes = (await call("/api/tickets", buyers[0].token)).map((t) => t.code);
const more = (await call("/api/tickets", buyers[1].token)).map((t) => t.code);
const scans = [...codes.slice(0, 4), ...more].map((code, i) => ({ scanId: crypto.randomUUID(), code, gate: i % 2 ? "Güney" : "Kuzey", device: `tablet-${(i % 2) + 1}` }));
await call(`${o}/events/${kemence.id}/check-ins`, kapi.token, { scans });
await call(`${o}/events/${kemence.id}/check-ins`, kapi.token, { scans: [{ scanId: crypto.randomUUID(), code: codes[0], gate: "Güney", device: "tablet-2" }] });

// A queue for the waiting-room picture: people who arrived before the sale opens.
for (let i = 0; i < 30; i++) {
  const early = await person(`early${i}`, `Erken ${i}`);
  await call(`/api/events/${upcoming.id}/queue`, early.token, {});
}

const browser = await chromium.launch();
async function signedIn(who, viewport = { width: 1280, height: 820 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB", timezoneId: "Europe/Istanbul", deviceScaleFactor: 1 });
  const page = await context.newPage();
  if (who) {
    await page.goto(`${BASE}/signin`);
    await page.getByLabel("Email").fill(who.email);
    await page.getByLabel("Password").fill(password);
    await page.getByRole("button", { name: "Sign in" }).click();
    await page.waitForURL(`${BASE}/`);
  }
  return page;
}

const shot = async (page, name, full = false) => {
  await page.waitForLoadState("networkidle").catch(() => {});
  await sleep(500);
  await page.screenshot({ path: `${OUT}${name}.png`, fullPage: full });
  console.log(`saved ${name}.png`);
};
let frame = 0;
const still = async (page) => {
  await sleep(250);
  await page.screenshot({ path: `${OUT}frames/${String(frame++).padStart(3, "0")}.png` });
};

const elif = await person("elif", "Elif Şahin");
const page = await signedIn(elif);
await page.goto(BASE);
await page.getByRole("heading", { name: "What's on" }).waitFor();
await shot(page, "catalog");
await still(page);

await page.getByRole("link", { name: /Kemençe Gecesi/ }).click();
await page.getByRole("button", { name: /Parter, row C, seat 13,/ }).waitFor();
await still(page);
await page.getByRole("button", { name: /Parter, row C, seat 13,/ }).scrollIntoViewIfNeeded();
for (const n of [13, 14]) {
  await page.getByRole("button", { name: new RegExp(`Parter, row C, seat ${n},`) }).click();
  await still(page);
}
await page.getByRole("button", { name: "One more in Ayakta" }).click();
await page.evaluate(() => window.scrollTo({ top: 0 }));
await shot(page, "seat-map");
await still(page);
await page.getByRole("button", { name: "Hold 3 places" }).click();
await page.getByRole("heading", { name: "Your places" }).waitFor();
await sleep(1500);
await shot(page, "held");
await still(page);

await page.getByRole("button", { name: /^Pay / }).click();
await page.getByText("Test payment · no money moves").waitFor();
await shot(page, "payment");
await still(page);
await page.getByRole("button", { name: /^Pay / }).click();
await page.getByRole("heading", { name: "You're going!" }).waitFor({ timeout: 30000 });
await page.getByRole("img", { name: /QR code for/ }).nth(2).waitFor();
await shot(page, "tickets", true);
await still(page);

const queuePage = await signedIn(await person("queue", "Sıradaki Kişi"));
await queuePage.goto(`${BASE}/events/${upcoming.id}`);
await queuePage.getByRole("heading", { name: "You're in line" }).waitFor();
await shot(queuePage, "waiting-room");

const organizer = await signedIn(deniz);
await organizer.goto(`${BASE}/manage/${org.id}/events/${kemence.id}`);
await organizer.getByText("Tickets sold", { exact: true }).waitFor();
await organizer.getByText("At the gate").waitFor();
await shot(organizer, "dashboard", true);
await organizer.goto(`${BASE}/manage/${org.id}`);
await organizer.getByRole("link", { name: "Hamlet, iki perde" }).waitFor();
await shot(organizer, "events");
await organizer.goto(`${BASE}/manage/${org.id}/venues/${akm.id}`);
await organizer.getByText("Floor plan, nearest the stage first").waitFor();
await shot(organizer, "venue-editor");

const gate = await signedIn(kapi, { width: 1100, height: 760 });
await gate.goto(`${BASE}/gate/${org.id}/${kemence.id}`);
await gate.getByText(/list of \d+ tickets/).waitFor();
await gate.context().setOffline(true);
await gate.getByText("Offline").waitFor();
const fresh = (await call("/api/tickets", buyers[2].token)).map((t) => t.code);
await gate.getByLabel("Ticket code").fill(fresh[0]);
await gate.getByRole("button", { name: "Check" }).click();
await gate.locator(".scan-result.ok").waitFor();
await gate.getByLabel("Ticket code").fill(fresh[1]);
await gate.getByRole("button", { name: "Check" }).click();
await gate.getByLabel("Ticket code").fill(fresh[0]);
await gate.getByRole("button", { name: "Check" }).click();
await gate.locator(".scan-result.warn").waitFor();
await gate.getByLabel("Ticket code").fill(fresh[2]);
await gate.getByRole("button", { name: "Check" }).click();
await gate.locator(".scan-result.ok").waitFor();
await shot(gate, "gate-offline");
await still(gate);

const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, locale: "en-GB", timezoneId: "Europe/Istanbul", isMobile: true, hasTouch: true });
const mobile = await phone.newPage();
await mobile.goto(`${BASE}/signin`);
await mobile.getByLabel("Email").fill(elif.email);
await mobile.getByLabel("Password").fill(password);
await mobile.getByRole("button", { name: "Sign in" }).click();
await mobile.waitForURL(`${BASE}/`);
await mobile.goto(`${BASE}/tickets`);
await mobile.getByRole("img", { name: /QR code for/ }).first().waitFor();
await shot(mobile, "phone-tickets");
await mobile.goto(`${BASE}/events/${kemence.id}`);
await mobile.getByRole("button", { name: /Parter, row A, seat 1,/ }).waitFor();
await shot(mobile, "phone-event");

const api = await signedIn(null);
await api.goto(`${BASE}/scalar`);
await api.getByText("Getting started").first().waitFor({ timeout: 30000 });
await shot(api, "api-reference");

await api.goto(MAIL);
await api.getByText("Your tickets: Karadeniz Ezgileri").first().click();
await sleep(1500);
await shot(api, "email");

console.log(`organizer ${deniz.email} / ${password}; festival ${festival.id}`);
await browser.close();
