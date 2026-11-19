---
title: Password strength HTTP API
supportedClass: aspnetcore-http-net10
acceptanceContract: contract.json
---

Create an HTTP JSON API that scores password strength. It must run with `dotnet run`.

`POST /strength` accepts `{"password":"..."}` and responds `{"score": <0-4>, "verdict": "<weak|strong>"}`.
The score is the number of satisfied rules: at least 8 characters, contains a digit, contains an
upper-case letter, contains a non-alphanumeric symbol. The verdict is "strong" when the score is 4
and "weak" otherwise. An empty password must return status 422.

## Acceptance expectations
- "Abc123!x" satisfies all four rules, returning 200 with score 4 and verdict "strong".
- "abcdefgh" satisfies only the length rule, returning 200 with score 1 and verdict "weak".
- An empty password returns 422.