// An organizer, a buyer and a gate device, in separate browsers, against the whole Compose stack.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d
//        cd tests/e2e && npm install && npx playwright install chromium && node gise.mjs
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const organizer = { name: "Deniz Yılmaz", email: `deniz.${run}@example.com`, password: "e2e-test-password" };
const buyer = { name: "Elif Şahin", email: `elif.${run}@example.com`, password: "e2e-test-password" };
const title = `Kemençe Gecesi ${run}`;

const browser = await chromium.launch();
const errors = [];
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n")[0]}`);
  }
}

async function open(viewport = { width: 1280, height: 900 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB", timezoneId: "Europe/Istanbul" });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error" && !/Failed to load resource|net::ERR_INTERNET_DISCONNECTED/.test(message.text())) errors.push(message.text());
  });
  return page;
}

async function register(page, person) {
  await page.getByRole("link", { name: "Create an account" }).click();
  await page.getByLabel("Your name").fill(person.name);
  await page.getByLabel("Email").fill(person.email);
  await page.getByLabel("Password").fill(person.password);
  await page.getByRole("button", { name: "Create account" }).click();
}

const deniz = await open();
const elif = await open();
let manageUrl = "";
let eventUrl = "";
let orderUrl = "";
let codes = [];

await step("an organizer signs up, creates an organizer and a venue with a floor plan", async () => {
  await deniz.goto(`${BASE}/manage`);
  await deniz.getByRole("heading", { name: "Sign in" }).waitFor();
  await register(deniz, organizer);
  await deniz.getByRole("heading", { name: "Sell tickets" }).waitFor();
  await deniz.getByLabel("Name").fill("Karadeniz Sahne");
  await deniz.getByRole("button", { name: "Create organizer" }).click();
  await deniz.getByRole("heading", { name: "New venue" }).waitFor();
  await deniz.getByLabel("Name", { exact: true }).first().fill("Atatürk Kültür Merkezi");
  await deniz.getByLabel("City", { exact: true }).fill("Samsun");
  await deniz.getByText("228 places in 2 sections").waitFor();
  await deniz.getByRole("button", { name: "Create venue" }).click();
  await deniz.getByRole("link", { name: "Atatürk Kültür Merkezi" }).waitFor();
  manageUrl = deniz.url().replace(/\/venues$/, "");
});

await step("they prepare an event as a draft and publish it", async () => {
  await deniz.goto(manageUrl);
  await deniz.getByRole("link", { name: "New event" }).click();
  await deniz.getByLabel("Title").fill(title);
  await deniz.getByLabel("Description").fill("Karadeniz ezgileri, iki perde.");
  await deniz.getByRole("button", { name: "Create draft" }).click();
  await deniz.getByRole("heading", { name: title }).waitFor();
  await deniz.getByRole("button", { name: "Publish" }).click();
  await deniz.getByRole("dialog").getByRole("button", { name: "Publish" }).click();
  await deniz.getByText("Tickets sold", { exact: true }).waitFor();
  eventUrl = `${BASE}/events/${deniz.url().split("/").pop()}`;
});

await step("a visitor finds the event without an account and sees the seat map", async () => {
  await elif.goto(BASE);
  await elif.getByPlaceholder("Artist, play or venue").fill(title);
  await elif.getByRole("link", { name: new RegExp(title) }).click();
  await elif.getByRole("heading", { name: title }).waitFor();
  await elif.getByRole("button", { name: /Stalls, row A, seat 1,/ }).waitFor();
  if (!(await elif.getByText("from").count()) && !(await elif.getByText("Up to 6 tickets per person").count())) throw new Error("No rules shown");
});

await step("choosing places asks them to sign up first, then brings them back", async () => {
  await elif.getByRole("button", { name: /Stalls, row C, seat 7,/ }).click();
  await elif.getByRole("button", { name: "Hold 1 place" }).click();
  await elif.getByRole("heading", { name: "Sign in" }).waitFor();
  await register(elif, buyer);
  await elif.getByRole("heading", { name: title }).waitFor();
});

await step("two seats and a standing place are held with a countdown", async () => {
  await elif.getByRole("button", { name: /Stalls, row C, seat 7,/ }).click();
  await elif.getByRole("button", { name: /Stalls, row C, seat 8,/ }).click();
  await elif.getByRole("button", { name: "One more in Standing" }).click();
  await elif.getByRole("button", { name: "Hold 3 places" }).click();
  await elif.getByRole("heading", { name: "Your places" }).waitFor();
  await elif.getByRole("timer").waitFor();
  await elif.getByText("Stalls C7").waitFor();
});

await step("paying on the provider's page settles the order and shows three QR tickets", async () => {
  await elif.getByRole("button", { name: /^Pay / }).click();
  await elif.getByText("Test payment · no money moves").waitFor();
  await elif.getByRole("button", { name: /^Pay / }).click();
  await elif.getByRole("heading", { name: "You're going!" }).waitFor({ timeout: 30_000 });
  await elif.getByRole("img", { name: /QR code for/ }).nth(2).waitFor();
  orderUrl = elif.url();
  codes = await elif.evaluate(async () => {
    const session = JSON.parse(localStorage.getItem("gise.session"));
    const response = await fetch("/api/tickets", { headers: { Authorization: `Bearer ${session.accessToken}` } });
    return (await response.json()).map((t) => t.code);
  });
  if (codes.length !== 3) throw new Error(`Expected 3 codes, got ${codes.length}`);
});

await step("the seats show as taken for everyone else", async () => {
  const other = await open();
  await other.goto(eventUrl);
  const seat = other.getByRole("button", { name: /Stalls, row C, seat 7, taken/ });
  await seat.waitFor();
  if (!(await seat.isDisabled())) throw new Error("A sold seat can still be chosen");
  await other.context().close();
});

await step("the organizer sees the sale on the event's dashboard", async () => {
  await deniz.reload();
  await deniz.locator(".stat", { hasText: "Tickets sold" }).getByText("3", { exact: true }).waitFor();
  await deniz.getByRole("cell", { name: /Elif Şahin/ }).waitFor();
});

await step("the gate lets a ticket in once, online", async () => {
  await deniz.getByRole("link", { name: "Open the scanner" }).click();
  await deniz.getByText(/list of 3 tickets/).waitFor();
  await deniz.getByLabel("Ticket code").fill(codes[0]);
  await deniz.getByRole("button", { name: "Check" }).click();
  await deniz.locator(".scan-result.ok").getByText("Admitted").waitFor();
  await deniz.getByLabel("Ticket code").fill(codes[0]);
  await deniz.getByRole("button", { name: "Check" }).click();
  await deniz.locator(".scan-result.warn").getByText("Already used").waitFor();
});

await step("with the connection gone the gate keeps checking, and syncs when it is back", async () => {
  await deniz.context().setOffline(true);
  await deniz.getByText("Offline").waitFor();
  await deniz.getByLabel("Ticket code").fill(codes[1]);
  await deniz.getByRole("button", { name: "Check" }).click();
  await deniz.locator(".scan-result.ok").getByText("Checked on this device").waitFor();
  await deniz.getByLabel("Ticket code").fill(codes[1]);
  await deniz.getByRole("button", { name: "Check" }).click();
  await deniz.locator(".scan-result.warn").getByText("Already used").waitFor();
  await deniz.getByLabel("Ticket code").fill("GS1.forged.code");
  await deniz.getByRole("button", { name: "Check" }).click();
  await deniz.locator(".scan-result.bad").getByText("Not a valid ticket").waitFor();
  await deniz.context().setOffline(false);
  await deniz.getByText("Online").waitFor();
  await deniz.locator(".stat", { hasText: "Waiting to sync" }).getByText("0", { exact: true }).waitFor({ timeout: 20_000 });
});

await step("the buyer refunds and the tickets stop working", async () => {
  await elif.goto(orderUrl);
  await elif.getByRole("button", { name: "Refund" }).click();
  await elif.getByRole("dialog").getByRole("button", { name: "Refund" }).click();
  await elif.getByRole("heading", { name: /Refund on its way|Refunded/ }).waitFor();
  await elif.getByText("No longer valid").first().waitFor();
});

await step("on a phone the catalog and the seat map fit the screen", async () => {
  const phone = await open({ width: 390, height: 844 });
  await phone.goto(eventUrl);
  await phone.getByRole("heading", { name: title }).waitFor();
  const overflow = await phone.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  if (overflow > 1) throw new Error(`The page scrolls sideways by ${overflow}px`);
  await phone.context().close();
});

await step("no errors in the browser console", async () => {
  if (errors.length) throw new Error(errors.join(" | "));
});

await deniz.screenshot({ path: `${OUT}last-organizer.png` });
await elif.screenshot({ path: `${OUT}last-buyer.png` });
await browser.close();
console.log(failures ? `\n${failures} step(s) failed` : "\nall steps passed");
process.exit(failures ? 1 : 0);
