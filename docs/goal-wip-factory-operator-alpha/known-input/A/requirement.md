---
title: Temperature conversion HTTP API
supportedClass: aspnetcore-http-net10
acceptanceContract: contract.json
---

Create an HTTP JSON API that converts temperatures between Celsius and Fahrenheit. It must run with `dotnet run`.

The endpoint takes the source unit, the target unit and the value as query parameters:
`GET /convert?from=c&to=f&value=100`. Supported units are `c` and `f` only, lower case.
The response body is `{"value": <number>, "unit": "<target unit>"}` with the converted value
rounded to two decimal places. A request naming an unsupported unit must return status 400.

## Acceptance expectations
- Converting 100 Celsius to Fahrenheit returns 200 with value 212.
- Converting 32 Fahrenheit to Celsius returns 200 with value 0.
- Converting 25 Celsius to Celsius returns 200 with value 25.
- Requesting an unsupported unit returns 400.