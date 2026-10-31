# TP-01 evidence

Date: 31 October 2026, Africa/Lagos.
Branch: codex/tp-01-local-host.
Scope: fake-backed host, shared contracts, persistence and local API.

## Implemented slice

- Separate .NET 10 contracts/host/test projects in ThermalPilot.Hub.slnx; no reference to the inherited hardware application.
- Loopback-only, token-protected local API with strict JSON contracts, explicit fake headers/payloads and unavailable sensor readings.
- SQLite schema/migration identity, WAL, exclusive host lease, versioned profiles, transactional simulation/audit records, synthetic reports and stored disabled-collection/cloud policy.
- Stable command idempotency, expiry/version checks and explicit simulation outcomes.
- Offline SQLite backup and restore to a fresh directory, preserving original data.
- Pinned package lockfiles, build/test/process-check script and a CI step using it.

## Verified in the implementation checkout

| Check | Result |
|---|---|
| Locked restore | Passed |
| Release build | Passed; warnings treated as errors for hub/tests |
| Unit/API/native-SQLite tests | 29 passed, 0 failed, 0 skipped |
| Profile/command/report retrieval after host recreation | Passed |
| Concurrent duplicate simulation requests | One command/result, one corresponding audit event |
| Invalid/missing/unknown JSON fields and enums | Rejected without writes |
| SQL-like user text | Stored as data; tables preserved |
| Future/foreign/corrupt schema | Refused without replacing original content |
| Native SQLITE_READONLY / SQLITE_FULL | Detected; report and audit transaction rolled back |
| HTTP full-storage response | 503; no false success or partial report/event |
| Backup/restore and idempotency after restore | Passed |
| Malformed policy JSON in backup | Normalized storage failure; original preserved; retry to same fresh target passed |
| Authentication/Origin/Host checks | Passed |
| Fake-only/no-token configuration | Real adapter/missing token rejected before startup |
| C# formatting and PowerShell parsing | Passed |

## Separate-process proof

The acceptance script launched only the standalone fake host, using a generated token in the child environment and a temporary loopback port. It:

1. Created one profile, one simulated command and one synthetic report.
2. Abruptly stopped that owned process after the API acknowledged committed writes.
3. Started a new process using the same database.
4. Retrieved the saved records and replayed the same command, preserving its ID.
5. Stopped the owned host, made an offline backup and restored into a fresh directory.
6. Started another host on the restored data and verified the same identities and three audit events.

Result: Restart PASS, BackupRestore PASS, Idempotency PASS; one profile, one command, one report, three events. Private databases/logs remain ignored under .private; no access token, local device evidence or private report was committed. All started synthetic host processes were cleaned up.

## Review

Focused C# persistence/contracts and local API/scripts reviews were performed. One finding about malformed-backup JSON error normalization was fixed with a regression test and re-reviewed. No remaining actionable findings were reported. Final formatting was verified after review.

## Dedicated checkout and remote evidence

Source checkpoint: 91125ce8ea3237cf130e04d1ec07f03d125e96fd, verified on origin/codex/tp-01-local-host. [Draft PR #3](https://github.com/THEA1GUY/ThermalPilot/pull/3) is stacked on TP-00.

The permanent dedicated checkout repeated locked restore, Release tests/publish and the separate-process recovery check: all passed, including 29 tests with no failures/skips and the same one-profile/command/report, three-event result. The real G-Helper process identity/start time and configuration checksum matched the private before snapshot.

[GitHub Actions](https://github.com/THEA1GUY/ThermalPilot/actions/runs/41148488625) passed for the source checkpoint, including the unchanged inert-baseline guard and the full hub/persistence script. BUILD_STATUS.md is the current resume point. Owner review is required before TP-02.

## Limits

This is not a hardware, thermal or AI acceptance test. No dashboard, phone transport, scheduler, MCP server, DeepSeek call, sensor collector or hardware-setting operation was added. The inert G-Helper baseline remains selected and unchanged.

Stored retention values do not mean a background sweeper exists. The SQLite page cap is not a total filesystem quota. These limitations are documented in LOCAL_HOST.md and remain gates before ongoing collection and release.
