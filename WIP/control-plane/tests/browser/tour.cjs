const { chromium } = require("playwright-core");

const session = process.argv[2];
const base = process.argv[3] || "http://127.0.0.1:3000";
const routes = [
  ["overview", "/i/local/overview"],
  ["work", "/i/local/work"],
  ["work-detail", "/i/local/work/WORK-8f16de10"],
  ["pipeline", "/i/local/pipeline"],
  ["agents", "/i/local/agents"],
  ["agent-detail", "/i/local/agents/factory.implementations-analyst"],
  ["quality", "/i/local/quality"],
  ["releases", "/i/local/releases"],
  ["release-detail", "/i/local/releases/WORK-8f16de10"],
  ["analytics", "/i/local/analytics"],
  ["improvement", "/i/local/improvement"],
  ["settings", "/i/local/settings"],
];

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
  page.on("console", (msg) => {
    if (msg.type() === "error") logs.push(`console.error: ${msg.text()}`);
  });
  page.on("pageerror", (error) => logs.push(`pageerror: ${error.message}`));
  page.on("response", (response) => {
    if (response.status() >= 400) logs.push(`http ${response.status()}: ${response.url()}`);
  });

  for (const [name, route] of routes) {
    logs.length = 0;
    try {
      await page.goto(base + route, { waitUntil: "networkidle", timeout: 180000 });
      await page.waitForTimeout(1200);
      const body = await page.locator("body").innerText();
      const bad = body.includes("could not load") || body.includes("panicked") || body.includes("This view is being wired");
      console.log(`PAGE ${name} URL=${page.url()} LEN=${body.length} BAD=${bad}`);
      if (bad) console.log(body.slice(0, 800));
      if (name === "overview" || name === "settings" || name === "quality") {
        await page.screenshot({ path: `tour-${name}.png`, fullPage: false });
      }
      if (logs.length) console.log(`LOGS ${name}:`, logs.slice(0, 8).join("\n"));
    } catch (error) {
      console.log(`PAGE ${name} FAILED ${error.message}`);
    }
  }
  await browser.close();
})().catch((error) => {
  console.error("TOUR_FAILED", error);
  process.exit(1);
});
