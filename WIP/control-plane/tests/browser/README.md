# Browser evidence scripts

These scripts launch an installed Chrome through `playwright-core`. They are evidence drivers, not a substitute for the .NET adapter integration tests.

```powershell
cd control-plane/tests/browser
npm install
# Log in with curl (or use the running browser) and copy the cp_session cookie value.
node tour.cjs <cp_session> http://127.0.0.1:3000
node lifecycle-ui.cjs <cp_session> WORK-xxxxxxxx http://127.0.0.1:3000
```

`tour.cjs` visits every page, records console/page errors and captures screenshots. `lifecycle-ui.cjs` performs intake through the UI, then contract approval, dispatch, merge authorization (typing the exact review digest), release and supervised serving.

The scripts intentionally use an existing authenticated session. They never log the service token.