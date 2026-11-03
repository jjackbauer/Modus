# WP-1.5 — Windows helper CI (pending)

**Correction:** the earlier "verified" run did not exercise containment. The helper installation block was missing from the committed installer, so the Windows containment test skipped.

Commit `8d29c70` adds the helper task installation and re-runs CI. Do not treat Windows C1 as verified until the new run passes with the helper installed and the **TokenIsAppContainer** check active.
