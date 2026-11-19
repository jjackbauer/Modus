const { chromium } = require("playwright-core");

const session = process.argv[2];
const workItem = process.argv[3];
const base = process.argv[4] || "http://127.0.0.1:3000";
const shot = process.argv[5] || "work-detail.png";

async function waitForText(page, text, timeout = 180000) {
  await page.getByText(text, { exact: false }).first().waitFor({ state: "visible", timeout });
}

(async () => {
  const browser = await chromium.launch({
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
    headless: true,
    args: ["--no-sandbox", "--disable-gpu"],
  });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1200 } });
  await context.addCookies([
    { name: "cp_session", value: session, domain: "127.0.0.1", path: "/", httpOnly: true, sameSite: "Strict" },
  ]);
  const page = await context.newPage();
  const logs = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") logs.push(`console.error: ${msg.text()}`);
  });
  page.on("pageerror", (error) => logs.push(`pageerror: ${error.message}`));
  page.on("response", (response) => {
    if (response.status() >= 400) logs.push(`http ${response.status()}: ${response.url()}`);
  });

  const url = `${base}/i/local/work/${workItem}`;
  await page.goto(url, { waitUntil: "networkidle", timeout: 180000 });
  await page.waitForTimeout(1500);
  console.log("initial:", (await page.locator(".work-title-block").innerText()).replace(/\s+/g, " ").slice(0, 200));

  if (await page.getByRole("button", { name: "Start dispatch" }).count()) {
    await page.getByRole("button", { name: "Start dispatch" }).click();
    console.log("dispatch 1 requested");
  }

  await waitForText(page, "Contract proposal", 120000);
  await page.waitForTimeout(1500);

  const approve = page.getByRole("button", { name: "Approve contract" });
  if (await approve.count()) {
    await approve.click();
    await page.waitForTimeout(400);
    await page.getByText("I inspected the proposal, scenarios and unresolved questions.").click();
    await page.getByRole("button", { name: "Confirm" }).click();
    console.log("contract approval confirmed");
    await page.waitForTimeout(1500);
  }

  const start2 = page.getByRole("button", { name: "Start dispatch" });
  if (await start2.count()) {
    await start2.click();
    console.log("dispatch 2 requested");
  }

  await waitForText(page, "Authorize merge", 180000);
  await page.waitForTimeout(1000);

  const detail = await page.evaluate(async (id) => {
    const response = await fetch(`/api/instances/local/work-items/${id}`);
    return response.ok ? await response.json() : null;
  }, workItem);
  const reviewDigest = detail?.reviews?.filter((review) => review.outcome === "accepted").at(-1)?.reviewDigest;
  console.log("review digest:", reviewDigest);
  if (!reviewDigest) throw new Error("no accepted review digest");

  await page.getByRole("button", { name: "Authorize merge" }).click();
  await page.waitForTimeout(400);
  await page.getByLabel("Type the exact review digest to confirm").fill(reviewDigest);
  await page.getByRole("button", { name: "Confirm" }).click();
  console.log("merge authorized");
  await page.waitForTimeout(1200);

  const start3 = page.getByRole("button", { name: "Start dispatch" });
  if (await start3.count()) {
    await start3.click();
    console.log("dispatch 3 requested");
  }

  await waitForText(page, "Release and engineering evidence", 240000);
  await waitForText(page, "Start serve", 120000);
  console.log("released");

  await page.getByRole("button", { name: "Start serve" }).click();
  await page.waitForTimeout(400);
  await page.getByRole("button", { name: "Confirm" }).click();
  console.log("serve requested");

  await waitForText(page, "Open verified preview", 180000);
  console.log("preview available");
  await page.waitForTimeout(1000);
  await page.screenshot({ path: shot });
  console.log("LOGS:", logs.slice(0, 30).join("\n"));
  await browser.close();
})().catch((error) => {
  console.error("WORKFLOW_FAILED", error);
  process.exit(1);
});
