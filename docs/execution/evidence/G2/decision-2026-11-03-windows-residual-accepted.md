# G2 addendum — Windows residual accepted (2026-11-03)

**Decision:** The user explicitly selected option **2**: accept the Windows C1 residual under the existing **conditional G2** and proceed with **trusted-only** work.

## Terms

- Windows AppContainer launch remains **unimplemented/fail-closed**; the helper installer/launcher may exist, but Windows is not claimed as OS-contained.
- Linux C1 is verified (unshare+chroot; security CI).
- Worker remains **trusted-only**, default-off; no untrusted model/provider code.
- Full G2 is **not** approved; WIP 1.0/G5 remain blocked until the 2026-12-03 re-review or a Windows containment fix.
- WP-1.3 may proceed only on trusted strategies under this residual.

## Evidence

- Linux containment: [os-containment-linux.md](../security/os-containment-linux.md)
- Windows error-5 blocker: [os-containment-windows-attempt.md](../security/os-containment-windows-attempt.md)
- Security CI (Linux/Windows suite): run 41655112634, later invalidated for Windows containment because the helper skipped; Linux remains verified.
