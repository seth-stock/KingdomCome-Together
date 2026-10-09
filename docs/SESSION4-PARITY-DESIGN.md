> Session 4 historical design. Session 5 uses wire 15, original whole-item compensation on definitive refusal, and unresolved/suspended handling for unknown puts. Read SESSION5-RESULTS.md.

# Session 4 KCD2 live container and reward design

Status: Candidate, 2026-10-08. No fresh KCD2 native gameplay or human multiplayer proof.

## Paths and protocol

`kdcmp_containers.lua` and `kdcmp_rewards.lua` are loaded by `kdcmp.lua`; both deterministic Windows/Linux pak builders include them. `GameBridge.Containers.cs` routes WO-134 events/frames; `SharedContainerRules.cs` bounds identifiers, categories, quantities, condition, charge, stock parts and reward rows. KCD2 wire 14 refuses old parsers; VERSION is unchanged.

Existing relay LootAsk/Host channels carry ask kinds ContainerTake=6, ContainerPut=7, ContainerOpen=8 and host kinds ContainerResult=8, ContainerState=9, ContainerMode=10, QuestReward=11. Every request is scoped to the guest connection/load; replies echo that scope. Broadcast stock/mode/rewards are scoped to the host incarnation and the guest records that scope from host mode. Relay host-role checks remain in place. Token reuse with different content conflicts; durable unknown host mutations quarantine.

Host mutation sequence: validate -> existing durable Begin flush -> native class delta with readback -> correlated result -> durable completion flush -> response. The journal guarantees host decision replay, not recipient delivery or global item conservation. One native Lua operation cache refuses uncertain mutation retries. Inventory UI operations by the host are not wholly intercepted by this queue.

Snapshots: stable name, or static decimetre position fallback for supported containers; anonymous horses refused. Up to 640 classes, 10 per part, 64 parts. A newer generation replaces older partial state; all parts required. Pending same-container transfers defer stock application. Native reductions/creations require measured class counts. Inventory instance metadata, theft flags and economic provenance are not cloned exactly.

Guest observation: compare nearby stock (9 m, shops 40 m) and Henry pack. Intersect stock decrease with pack increase for takes and stock increase with pack decrease for puts. Restocks alone generate no ask. Aggregate observed money loss is consumed once across a trade batch, not refunded per class. This polling design can miss or misattribute native transactions between polls and cannot stop optimistic use/consumption before host judgement.

Settlement: failed/expired take removes optimistic quantity and refunds a measured purchase debit once; partial rollback remains pending. Native exceptions/uncertain creation are retained, not blindly retried. Failed puts discard local optimistic stock without refunding Henry, because host may already contain it: loss is possible. Supported character/checkpoint capture refuses unresolved rollback. Ordinary autosaves and abrupt process crashes are not a complete recipient receipt/escrow protocol.

Compatibility: live mode defaults on, host console `mp_shared_containers off` selects the previous personal per-world chest ledgers. Mode is session-local. Old body/loose/drop features and `mp_loot_chests` kill switch are preserved. Legacy pending-body loop now retains asks past 15 s so 20 s native rollback actually runs.

## Quest rewards

`GameBridge.Wo137.cs` ignores first-observed native states for reward inference, arms subsequent host changes, captures guest inventory before applying the mirrored quest step, and marks receipt ready only after exact native new-state readback. SHA-256-derived keys include actual sequence/path/old/new. Three-second windows coalesce same-quest cascades, exclude observed item/container gains and subtract native guest rewards. Early reward packets wait for ready receipts; unknown native creation never replays. No persistent reward receipts, XP sharing, offline backfill or complete scripted reward interception. Unobserved gains and overlapping windows remain attribution risks.

## Verification and acceptance

`Test-WO134ParitySynthetic.ps1`: 35 synthetic checks (shared stock/retry/overdraw, inert APIs, pack deltas, refunds, timeout/capture refusal, legacy mode/kill switch, reward reorder/subtraction/idempotence). Existing WO-134 suite: 77 checks including the actual body loop's 15 s regression. `SharedContainerTests.cs`: wire validation, durable replay/conflict/restart quarantine, reward keys and bounds. These stand-ins do not establish actual engine UI behavior. H-43 through H-50 require two-computer native-screen acceptance. Full economic escrow/receipt and combat/quest authority remain open.
