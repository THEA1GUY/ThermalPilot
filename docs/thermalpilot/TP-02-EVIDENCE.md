# TP-02 evidence

Date: 31 October 2026, Africa/Lagos.
Branch: codex/tp-02-readonly-telemetry.
Scope: real read-only controller, typed local pipe, sampling, durable raw history and recovery.

## Source boundary

The controller links only the extracted ASUS DSTS/native Windows readers and contracts. No inherited application bootstrap, config, control/timer/UI, GPU-vendor provider, Pawn module or hardware-write operation is reachable. The inert baseline remains selected.

Both focused C#/native/protocol and host/persistence/script reviews initially found recoverable error-path issues. Linked sampler cancellation, malformed-frame handling, host retry handling, independent retention pruning and schema-1 backup migration were fixed and re-reviewed with no remaining actionable findings.

## Observed real-device checks

Actual ASUS CPU temperature, CPU/GPU fan readings and Windows battery/AC/CPU-use/RAM measurements were received and persisted on the target laptop. GPU temperature and the mid-fan query were unavailable; no substitute data was generated. A successful GPU-fan zero is distinct from those unavailable values.

The owner performed an actual sleep/wake and AC/battery transition. Persisted sampling changed from ten seconds on battery to two on AC. A 66.5-second sampling gap was recorded and Windows Kernel-Power event IDs 506/507 corroborated entry/exit. Their messages/device identifiers were not collected into public evidence.

The owner reported G-Helper's CPU temperature and fan percentage. Nearby actual readings and separately inspected fan-display settings are kept in private evidence; percentage is normalized against the configured maximum, without claiming that maximum is a measured calibration. Desktop automation failed to initialize, so no automated screenshot comparison is claimed.

The NVIDIA device's Windows-reported most-recent power state was D3 before collection and at early spot checks; a later check after the owner's power transitions reported D0. This is an inconclusive causal wake/energy result, not evidence that polling never wakes the GPU. No NVIDIA library or nvidia-smi polling was used. Workload/activity/power transitions confound discharge comparisons; no collector-attributable battery saving or discharge penalty is claimed.

Actual pipe ACL inspection confirmed a protected DACL, matching owner, no other allowed identity and NETWORK deny. Real handshake/version/unsupported-operation checks passed without interrupting subsequent snapshots. Missing API credentials returned 401, browser Origin 403 and removed simulation POST 405.

The existing G-Helper process and relevant ASUS services remained unchanged at the midpoint. Its configuration checksum changed during the owner's power-transition sequence; G-Helper's own log recorded handling those transitions. A checksum alone cannot attribute that change. The new controller has no config-writing/bootstrap path, but this run does not claim the independent writer left all laptop settings/config unchanged.

## Build and CI

27 SQLite/API/protocol tests passed in both implementation and permanent checkouts. Schema-1 restore and expiry-without-collection regressions are covered with real SQLite operations. No simulated sensor provider is used.

Initial CI found that dotnet test did not build the standalone controller; explicit solution build before publish fixed the clean-checkout failure. [Push CI](https://github.com/THEA1GUY/ThermalPilot/actions/runs/41151267117) and [PR CI](https://github.com/THEA1GUY/ThermalPilot/actions/runs/41151273456) passed for 87ad94ff2a51321ae274ad030e239a0390d72ccc. The actual Windows-native acceptance script passed malformed-frame continuity, stale quality, owned-controller reconnect, recorded gap and actual-snapshot backup/restore on the runner. This is separate from target-laptop acceptance.

## Pending final acceptance

- Thirty-minute hidden/background sampling and final overhead summary.
- Actual owned-controller disconnect/restart, stale quality and recorded recovery gap.
- Actual-snapshot backup/restore and malformed-frame recovery.
- Full target-laptop acceptance script from the permanent checkout.
- Final before/after G-Helper process/config/service observations, preserving the known checksum-change limitation.

These items are pending until observed, not simulated passes. BUILD_STATUS.md records the final resume point. No TP-03 implementation is authorized by TP-02.
