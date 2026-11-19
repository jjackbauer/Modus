---
title: Text statistics HTTP API
supportedClass: aspnetcore-http-net10
acceptanceContract: contract.json
---

Create an HTTP JSON API that reports statistics about a piece of text. It must run with `dotnet run`.

`POST /analyze` accepts `{"text": "..."}` and responds
`{"words": <count>, "characters": <count>, "longest": "<word>"}`.
Words are separated by single spaces; `characters` counts every character including spaces;
`longest` is the longest word, and the first such word on a tie. An empty or missing `text`
must return status 422.

## Acceptance expectations
- Analysing "one two three" returns 200 with words 3, characters 13 and longest "three".
- Analysing "a" returns 200 with words 1, characters 1 and longest "a".
- Analysing "aa b" returns 200 with longest "aa" (first word wins a tie).
- Analysing an empty string returns 422.