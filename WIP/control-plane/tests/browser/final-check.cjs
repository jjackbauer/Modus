const { chromium } = require("playwright-core");

const session = process.argv[2];
const base = process.argv[3] || "http://127.0.0.1:3000";

(async () => {
  const browser = await chromium.launch({
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
    headless: true,
    args: ["--no-sandbox", "--disable-gpu"],
  });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
  await context.addCookies([
    { name: "cp_session", value: session, domain: "127.0.0.1", path: "/", httpOnly: true, sameSite: "Strict" },
  ]);
  const page = await context.newPage();
  const logs = [];
  page.on("console", (msg) => { if (msg.type() === "error") logs.push(`console.error: ${msg.text()}`); });
  page.on("pageerror", (error) => logs.push(`pageerror: ${error.message}`));

  await page.goto(`${base}/i/local/overview`, { waitUntil: "networkidle", timeout: 180000 });
  await page.waitForTimeout(1500);
  const overview = await page.locator("main").innerText();
  console.log("BLOCKED_METRIC", overview.match(/Blocked or failed\s+(\d+)/)?.[1]);
  console.log("ATTENTION", overview.match(/Needs attention\s+(\d+) item/)?.[1]);
  console.log("CRUMBS", (await page.locator(".crumbs").innerText()).replace(/\s+/g, " "));
  await page.getByRole("button", { name: "Theme" }).click();
  await page.waitForTimeout(300);
  console.log("THEME_AFTER_CLICK", await page.evaluate(() => document.documentElement.getAttribute("data-theme")));
  await page.screenshot({ path: "final-light.png", fullPage: false });
  await page.getByRole("button", { name: "Theme" }).click();

  await page.goto(`${base}/i/local/work/WORK-8f16de10`, { waitUntil: "networkidle", timeout: 180000 });
  await page.waitForTimeout(1500);
  const detail = await page.locator("main").innerText();
  console.log("RAW_IF_PRESENT", detail.includes("if serving.active") || detail.includes("else Start serve"));
  console.log("SERVE_BUTTONS", await page.getByRole("button", { name: "Start serve" }).count(), await page.getByRole("button", { name: "Stop serve" }).count());
  console.log("LEGAL_TARGETS", detail.includes("Factory legal targets:"));
  await page.getByLabel("Priority").selectOption("high");
  await page.getByLabel("Notes").fill("Operator note: prioritize the explicit-contract path.");
  await page.getByRole("button", { name: "Save planning metadata" }).click();
  await page.waitForTimeout(1500);
  const planning = await page.evaluate(async () => {
    const response = await fetch("/api/instances/local/work-items/WORK-8f16de10");
    return (await response.json()).planning;
  });
  console.log("PLANNING", planning.priority, planning.notes);
  await page.screenshot({ path: "final-detail.png", fullPage: false });

  await page.goto(`${base}/i/local/work`, { waitUntil: "networkidle", timeout: 180000 });
  await page.getByRole("button", { name: "New work item" }).click();
  await page.waitForTimeout(500);
  console.log("TEMPLATE_PICKER", await page.getByLabel("Approved template").count());

  await page.goto(`${base}/i/local/pipeline`, { waitUntil: "networkidle", timeout: 180000 });
  await page.waitForTimeout(800);
  const input = page.getByLabel("Reconstruct the pipeline at a past instant");
  await input.fill("2026-11-19T13:20");
  await input.dispatchEvent("change");
  await page.waitForTimeout(1800);
  const pipeline = await page.locator("main").innerText();
  console.log("PLAYBACK", pipeline.includes("Historical playback"));
  await page.getByRole("button", { name: "Live" }).click();
  await page.waitForTimeout(800);

  console.log("ERRORS", logs.slice(0, 20).join("\n"));
  await browser.close();
})().catch((error) => {
  console.error("FINAL_CHECK_FAILED", error);
  process.exit(1);
});
