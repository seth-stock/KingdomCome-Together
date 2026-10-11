# Claude handoff continuation — 2026-10-11

The four requested features remain incomplete. These are development builds;
no two-computer gameplay, Proton gameplay or soak acceptance is established.
VERSION remains 0.45.0; the wire protocol remains 16.

The existing Ghidra projects and logs are preserved under ignored
`_work/ghidra-full-session9` in the KCD1 reliability worktree. A fresh streaming audit was saved at
`_work/continuation/ghidra-audit.json`: 37 images, 910,489 discovered functions
attempted, 16 failed exports and 160,023 functions with warnings. The six decoder
repairs remain in the separate review project. Audit exit 2 deliberately
reports the remaining failures. Nothing was re-decompiled or published.

Claude's KCD2 commit adds joiner custody. This continuation:

- Refuses forwarding new takes when their custody journal cannot be written;
  never falls back silently to volatile memory.

- Retains completed receipts on disk and retries at most 32 every ten seconds,
  rotating through the journal. There is no receipt acknowledgement protocol;
  repeated host compensation is fenced by its durable restore intent.
- Preserves the world identity with new takes and filters recovery/receipts to
  that world. Legacy records without world evidence remain preserved, rather
  than asking an unrelated host to settle them.
- Quarantines partial returns and resumes durable settlement forward without
  repeating engine work after a process restart.
- Refuses repeated take events with changed metadata; memory mode no longer
  resets a previously recorded host answer.
- Preserves native recovery evidence exactly when the common journal
  quarantines an interrupted operation.

The KCD1 continuation also fixes extended combat sample routing, native probe
mutation gates, numeric actor handles, target cleanup, quest XP pagination and
late-guest world announcements. Those changes are game-specific, not KCD2
native integrations. No offsets were transplanted between the games.

## Remaining implementation requirements

Independent participant targeting, contacts, damage, blocks, reactions and
credit still need native integration and multiplayer proof. Crash economy
recovery still lacks a consistent checkpoint and persistent item lineage;
world time and class counts cannot prove item custody. Host compensation lacks
native durable acknowledgement, and consumption/barter/crime are not one
authoritative ledger. Corrupt/unavailable guest journals refuse connected transfers; their original
files are preserved.

Quest item rewards and XP receipt deduplication are still load-local; a native
64-row observation ring is not a durable effect queue. Spawn/script effects
and guest quest execution fencing remain open. Character restoration still
refuses active world-referenced core state and lacks complete arbitrary native
construction/clock/reference mapping. Linux's native adapter/injection path
and actual Proton gameplay remain unproved.

Build and test logs are retained in ignored `_work/continuation`. The packages
do not change installed games, real saves or firewall rules. Artifact hashes and local build outputs are recorded in
[CONTINUATION-BUILDS.md](CONTINUATION-BUILDS.md).

## Verified checks

KCD2: 1345 managed/Lua tests passed. 74 relay tests, 434 native checks, all 44 Lua synthetic suites and three static checks passed.

Windows development installers passed their payload smoke gates; the frame-rate
soak gate was explicitly skipped and no gameplay capability was promoted.
Linux self-contained builds use the existing WSL .NET SDK. Native Linux
execution/package smoke evidence is recorded separately from Proton gameplay.
All 401 real-save baseline files across both games remain unchanged.
