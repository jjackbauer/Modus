---
title: Invoice total HTTP API
supportedClass: aspnetcore-http-net10
acceptanceContract: contract.json
---

Create an HTTP JSON API that totals an invoice. It must run with `dotnet run`.

`POST /invoices` accepts `{"items":[{"name":"a","quantity":2,"unitPrice":10}, ...]}` and responds
`{"total": <sum of quantity * unitPrice>, "itemCount": <number of items>}` with the total as a
number. An invoice with an empty `items` array must return status 422.

## Acceptance expectations
- An invoice with two items, 2 x 10 and 1 x 5, returns 200 with total 25 and itemCount 2.
- An invoice with one item, 3 x 4.5, returns 200 with total 13.5.
- An invoice with no items returns 422.