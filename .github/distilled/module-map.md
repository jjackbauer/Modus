# Module Map

> **Scope**: Module responsibilities, public boundaries, and ownership lines.
> **Confidence**: Low
> **Last updated**: 2026-05-16
> **Source entries**: OBS-000

- ❓ Core defines stable contracts and shared abstractions.
- ❓ Host composes dependencies and controls plugin registration.
- ❓ Modules implement bounded business capabilities behind explicit interfaces.
- ❓ Plugins implement extension contracts and are validated before activation.
- ❓ Adapters isolate infrastructure concerns from domain modules.
