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

The NVIDIA device's Windows-reported most-recent power state was D3 before collection and at subsequent spot checks. This establishes those observations only; it does not prove absence of every brief wake. No NVIDIA library or nvidia-smi polling was used. Workload/activity/power transitions confound discharge comparisons; no collector-attributable battery saving or discharge penalty is claimed.

## Pending final acceptance

- Thirty-minute hidden/background sampling and final overhead summary.
- Actual owned-controller disconnect/restart, stale quality and recorded recovery gap.
- Actual-snapshot backup/restore and malformed-frame recovery.
- Permanent-checkout repeat and remote CI.
- Final before/after G-Helper process/config/service comparison.

These items are pending until observed, not simulated passes. BUILD_STATUS.md records the final resume point. No TP-03 implementation is authorized by TP-02.
