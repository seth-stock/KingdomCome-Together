# Continuation build record ? 2026-10-11

Development packages for `KCD2`, branch `codex/coop-reliability` on
`seth-stock/KingdomCome-Together`. The four requested features remain incomplete;
see [CONTINUATION-RESULTS.md](CONTINUATION-RESULTS.md).
VERSION was not changed. No human multiplayer, Proton gameplay or frame-rate
soak acceptance is claimed.

## Validation

| Check | Result |
|---|---|
| KCD1 managed/Lua | 446 passed |
| KCD1 native bounded-stream/schema | 25 passed; exact-engine bridge compiled |
| KCD2 client | 1,345 passed |
| KCD2 real-relay tests | 74 passed |
| KCD2 native tests | 434 passed |
| KCD2 Lua suites/static checks | 44 suites and three checks passed |
| Windows packaged agent/relay smoke | Passed for both games |
| Linux package hashes/fake-Steam doctor/link/install | Passed for both games |
| Canonical Windows/Linux Lua pak equality | Passed for both games |
| Native Linux agent/relay startup and handshake | Passed; KCD2 wire 16 |
| Real save baseline | All 401 files unchanged |
| Full-engine export audit | All 910,489 discovered functions attempted; 16 failures retained |

Logs, TRX files, the export audit and artifact metadata remain in ignored
`_work/continuation`. Full Ghidra databases/shards and original analysis logs
remain in the KCD1 worktree's ignored `_work/ghidra-full-session9`; no repeat
decompilation was performed. Private engine code and game binaries are excluded
from commits. Windows builds used their explicit development/skip-soak switches.

## Local artifacts

Matching files are in `release/continuation-20261011`, with `SHA256SUMS.txt`.
The Windows zip contains the installer and continuation/test guides. The Linux
archive carries self-contained agents/relays and matching Lua; KCD2 also carries
the Windows plugin/injector for Proton. These are local development outputs,
not a declaration that the missing engine integrations work.

| Artifact | SHA-256 |
|---|---|
| `KingdomComeTogether-continuation-20261011.zip` | `8ec57304ebb6eb20ede534a91323581c5da9b087715dacb211d8e65303449643` |
| `KingdomComeTogether-Linux-0.45.0.tar.gz` | `05b9f7c1397390e39089b4e3c9a7674de905ab5a77a57bfabbcbd37987ec7ce5` |
| `KingdomComeTogether-Setup-0.45.0.exe` | `de04ea0853f1824d0907b88fc773a38550423e153e0fc5cafce4a1e6acfb910a` |

The earlier intermediate test failures and build attempts are preserved in the
logs. The world-offer race was fixed by reannouncing on roster changes. Relay
refusal now publishes disconnected state before its final refusal notification.
Final package gates pass; these checks do not prove native damage/quest effects
or arbitrary reference restoration.
