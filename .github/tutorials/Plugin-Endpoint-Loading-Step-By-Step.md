# Plugin Endpoint Loading Step-by-Step Tutorial

This guide walks through plugin loading by endpoint, from upload to activation to invocation, including failure and DI lifetime validation.

Source of truth for behavior gates and test coverage:
- .github/requirements/Modus.Host.Plugin-Endpoint-Loading.md
- tests/Modus.Host.IntegrationTests/PluginLoadingTutorialUploadFlowTests.cs
- tests/Modus.Host.IntegrationTests/PluginLoadingTutorialDiLifetimeVerificationTests.cs
- tests/Modus.Host.IntegrationTests/PluginLoadingTutorialRuntimeValidationTests.cs

## 0. Prerequisites

1. Host is running and management endpoints are reachable.
2. You have a plugin package and detached signature file.
3. Host is configured with at least one trusted author key.

Required upload request shape:
- Content type: multipart/form-data
- Form fields:
  - package: plugin bundle (zip)
  - signature: detached signature file

Required invocation request shape:
- POST /api/{pluginId}/{operation}
- JSON body includes:
  - correlationId
  - payload

## 1. Upload Plugin Package

Request:

```bash
curl -i -X POST "http://localhost:5000/management/plugins/uploads" \
  -F "package=@plugin.bundle.zip" \
  -F "signature=@plugin.bundle.sig"
```

Expected success contract:
- HTTP 202 Accepted
- Body contains:
  - operationId
  - status (Queued)
- Location header points to /management/plugins/uploads/{operationId}

Expected rejection contract (signature mismatch or missing trust):
- HTTP 401 Unauthorized
- Body contains:
  - status (Rejected)
  - error (deterministic authorization failure text)

## 2. Poll Upload Operation Until Terminal State

Use operationId or Location header from step 1.

```bash
curl -i "http://localhost:5000/management/plugins/uploads/{operationId}"
```

Expected:
- Valid package reaches terminal Completed.
- Invalid package reaches terminal Failed with deterministic diagnostics.

## 3. Verify Activation in Management Status

```bash
curl -i "http://localhost:5000/management/status"
```

Verify:
1. Uploaded plugin appears in active runtime state.
2. Owner resolution is unique for operations expected in your package.
3. No unintended plugins were activated.

## 4. Verify Activated Operations in Capabilities

```bash
curl -i "http://localhost:5000/management/plugins/capabilities"
```

Verify:
1. Activated plugin appears.
2. Expected operations are listed and visible for invocation.
3. Operation ownership does not conflict with another plugin.

## 5. Invoke Runtime Operation with Correlation Continuity

Success-path invocation:

```bash
curl -i -X POST "http://localhost:5000/api/{pluginId}/{operation}" \
  -H "Content-Type: application/json" \
  -d '{
    "correlationId": "tutorial-op-success-corr",
    "payload": "{\"message\":\"tutorial-payload\"}"
  }'
```

Expected success contract:
- HTTP 200 OK
- Body includes:
  - success: true
  - status: Success
  - correlationId equals request correlationId
  - operation-specific business payload

Rejection-path invocation example:

```bash
curl -i -X POST "http://localhost:5000/api/{pluginId}/{operation}" \
  -H "Content-Type: application/json" \
  -d '{
    "correlationId": "tutorial-op-corr-rejected",
    "payload": "please-reject"
  }'
```

Expected rejection contract:
- HTTP 422 Unprocessable Entity
- Body includes:
  - success: false
  - status: Rejected
  - deterministic business rejection payload
  - correlationId equals request correlationId

## 6. Validate DI Lifetime Behavior

Run repeated live calls and compare responder identity in response payload.

Singleton example:
- POST /api/Plugin.Tutorial.Lifetime.Singleton/Tutorial.Lifetime.Singleton.Verify twice
- Expect same instanceId, invocationCount increments (1 then 2)

Scoped example:
- POST /api/Plugin.Tutorial.Lifetime.Scoped/Tutorial.Lifetime.Scoped.Verify twice
- Expect different instanceId per request, invocationCount is 1 each time

Transient example:
- POST /api/Plugin.Tutorial.Lifetime.Transient/Tutorial.Lifetime.Transient.Verify twice
- Expect different instanceId for each call, invocationCount is 1 each time

## 7. Deterministic Failure and Isolation Checks

A. Invalid package upload:
- Upload archive without plugin assemblies.
- Expect terminal Failed and deterministic validation reason.
- Registry snapshots and operation catalog must remain unchanged.

B. Owner mismatch invocation:
- Invoke using pluginId that does not own the requested operation.
- Expect HTTP 500 with deterministic owner-mismatch message.
- correlationId must echo request.
- Side-effect counter should remain zero.

C. Unresolved responder invocation:
- Invoke cataloged operation with no ISyncResponder registered for that plugin.
- Expect HTTP 500 with deterministic unresolved-responder message.
- correlationId must echo request.
- Unrelated responders must not execute.

## 8. Run Behavior-Proof Integration Tests

Run the integration test suite used to enforce this tutorial:

```bash
dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --no-build
```

Focused test classes:
- PluginLoadingTutorialUploadFlowTests
- PluginLoadingTutorialDiLifetimeVerificationTests
- PluginLoadingTutorialRuntimeValidationTests
- PluginLoadingTutorialBehaviorProofComplianceTests

## 9. Troubleshooting Fast Checks

1. Upload returns 401:
- Verify trusted author key configuration and matching signature.

2. Upload never reaches Completed:
- Poll operation endpoint and inspect terminal diagnostics.

3. Invocation returns 500 unexpectedly:
- Confirm status/capabilities show plugin active and operation present.
- Validate pluginId in route matches operation owner.

4. Correlation mismatch:
- Ensure request body includes correlationId and that your client is not rewriting response payload.
