# Reproduction and verification

This guide reproduces the local-first environment used for engineering evidence. It does not replace the .NET adapter integration suite or independent evaluator acceptance.

## 1. Build

```powershell
dotnet build Wip.Factory.ControlApi/Wip.Factory.ControlApi.csproj
powershell -File tools/build-control-plane.ps1
```

`tools/build-control-plane.ps1` builds the release Rust server, builds the wasm client for `wasm32-unknown-unknown`, runs `wasm-bindgen` into `control-plane/server/static/pkg`, and builds the adapter.

## 2. Start

```powershell
powershell -File tools/start-control-plane.ps1 -TargetRepository F:\wip-demo-target -DataRoot F:\wip-demo-runtime
```

Open <http://127.0.0.1:3000>. Read the generated first-admin password from `F:\wip-demo-runtime\bootstrap-admin.txt`.

The adapter is bound to loopback. The Rust server holds the service token. Do not send the token to a browser.

## 3. Exercise a complete local lifecycle

1. Sign in as the administrator.
2. **Work queue → New work item**. Fill title, description, functional requirements, observable acceptance expectations, and the explicit acceptance contract JSON. Use a title unique in the target repository; the factory deduplicates by requirement digest.
3. Validate and submit.
4. **Start dispatch**; wait for the contract proposal.
5. Open **Approve exact digest**, inspect the digest and scenarios, confirm.
6. **Start dispatch** again for implementation and independent review.
7. When review is accepted, **Authorize merge**, type the exact review digest.
8. **Start dispatch** to perform merge, post-merge verification and release.
9. **Start serve**; wait for the verified preview URL.
10. **Stop serve** to end the bounded window.

Every step above goes through `FactoryRuntime` and writes factory events. The UI does not fake any execution.

## 4. Automated evidence

```powershell
dotnet test Wip.Factory.ControlApi.Tests/Wip.Factory.ControlApi.Tests.csproj
dotnet test Wip.Factory.Core.Tests/Wip.Factory.Core.Tests.csproj
dotnet test Wip.Factory.Agents.Tests/Wip.Factory.Agents.Tests.csproj
dotnet test Wip.Factory.Host.Tests/Wip.Factory.Host.Tests.csproj

cd control-plane
cargo test --workspace
cargo check -p control-plane-web --features hydrate --target wasm32-unknown-unknown
```

Browser evidence (optional, requires Chrome and `playwright-core`):

```powershell
cd control-plane/tests/browser
npm install
node tour.cjs <cp_session> http://127.0.0.1:3000
```

## 5. Failure injection that was actually checked

- Stop the adapter while the UI is open: SSE status changes to reconnecting/disconnected; stale data remains labelled and no success is shown.
- Submit the same requirement twice: the factory deduplicates by requirement digest; the UI navigates to the existing item. The adapter does not pretend a new contract was applied.
- Approve a contract with a wrong proposal digest: the adapter returns a typed 409 and the UI shows the error.
- Authorize merge without a review: the adapter refuses; the UI cannot enable merge without an accepted review.
- Approve a non-executable proposal: the adapter refuses with `contract-not-executable`; the UI disables approval and explains why.
- Restart the Rust server: in-memory sessions are lost; the user signs in again. Durable factory work is unaffected.
- Restart the adapter: read models recover from the journal. Jobs that were in progress are not fabricated as complete.