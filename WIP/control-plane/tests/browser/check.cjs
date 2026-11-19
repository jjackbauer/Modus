const { chromium } = require("playwright-core");

const session = process.argv[2];
const url = process.argv[3] || "http://127.0.0.1:3000/i/local/overview";
const shot = process.argv[4] || "overview.png";

(async () => {
  const browser = await chromium.launch({
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
    headless: true,
    args: ["--no-sandbox", "--disable-gpu"],
  });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  await context.addCookies([
    { name: "cp_session", value: session, domain: "127.0.0.1", path: "/", httpOnly: true, sameSite: "Strict" },
  ]);
  const page = await context.newPage();
  const logs = [];
  page.on("console", (msg) => logs.push(`console.${msg.type()}: ${msg.text()}`));
  page.on("pageerror", (error) => logs.push(`pageerror: ${error.message}`));
  page.on("requestfailed", (request) => logs.push(`requestfailed: ${request.url()} ${request.failure()?.errorText}`));
  page.on("response", (response) => {
    if (response.status() >= 400) logs.push(`http ${response.status()}: ${response.url()}`);
  });

  await page.goto(url, { waitUntil: "networkidle", timeout: 180000 });
  await page.waitForTimeout(2500);
  console.log("URL:", page.url());
  const text = await page.locator("body").innerText();
  console.log("TEXT_START");
  console.log(text.slice(0, 4000));
  console.log("TEXT_END");
  console.log("LOGS_START");
  console.log(logs.slice(0, 80).join("\n"));
  console.log("LOGS_END");
  await page.screenshot({ path: shot, fullPage: true });
  await browser.close();
})().catch((error) => {
  console.error("CHECK_FAILED", error);
  process.exit(1);
});
