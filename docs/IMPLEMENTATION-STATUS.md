# Co-op reliability implementation

## Session 5 status (2026-10-09)

Full campaign multiplayer and parity remain unfinished. KCD1 now has sequenced, incarnation/term-checked NPC movement, stale-handle protection, pre-mutation reward baselines and verified item grants. Weather presets are Candidate, default off (`kcdus_weather on` on both clients), with no current-profile or scripted-weather preservation proof. KCD2 whole-item put compensation preserves the observed native instance on definitive refusal; unknown outcomes remain unresolved and cannot trigger a speculative refund. KCD2 source wire is now **15**. Neither game has complete native economy interception, universal quest/XP/effect authority or arbitrary complete character restoration. See [SESSION5-RESULTS.md](SESSION5-RESULTS.md).

## Session 4 current status (2026-10-08)

VERSION stays 0.45.0; **wire protocol is now 14** so older agents/relays are refused. All participants must install this same tagged build. This remains partly shared, with no human acceptance or shared-simulation claim.

Live container/reward parity is **Candidate: synthetic Lua and agent tests only, no fresh KCD2 engine proof**. `kdcmp_containers.lua` extends WO-134 with named horse saddlebags, shop-linked stashes and live chest stock. It uses existing scoped durable host decisions, native inventory readback, bounded multipart stock generations and duplicate/restart quarantine. Guest takes require pack gain and puts require pack loss. Stock is applied only after pending transfers settle; stale generations are ignored. Failed rollback blocks supported character/checkpoint capture.

The host defaults to live shared stock. `mp_shared_containers off` restores the existing personal per-world chest-ledger behavior; the setting is session-local and must be reapplied after restarting the agent. `mp_loot_chests off` disables the new container watcher too. Bodies, loose pickups, drops, NPC/combat/quests, shared pause, DLC rules and checkpoint Candidate mode remain in place.

Native inventory screens move items before polling: this is **not pre-transfer escrow**. Containers use class/count/condition, not exact per-instance metadata. Anonymous moving horses are refused; stable names or position identities must match. Simultaneous host-native UI transfers, streaming, restocks, barter/theft, recipient persistence and interruption conservation need engine/two-computer proof. A refused purchase refunds only the measured personal debit once; wallets are not shared. A rejected/unknown put removes the optimistic local stock without a speculative Henry refund, so items can be lost. Consume/equip before rollback can prevent settlement; the supported save path then refuses rather than declaring success. Crashing/reloading outside that path is not a proved receipt protocol.

`kdcmp_rewards.lua` observes a three-second host inventory window around verified mirrored quest steps. It excludes observed loot and subtracts native guest payment; same-quest cascades coalesce, early messages wait for verified application, and uncertain native creation is not retried. First observations/checkpoints are not rewarded retroactively. The window is heuristic, may confuse unrelated unobserved gains, is not durable across loads/restarts, has no XP or offline backfill, and cannot establish all quest side effects.

See [SESSION4-RESULTS.md](SESSION4-RESULTS.md), [SESSION4-PARITY-DESIGN.md](SESSION4-PARITY-DESIGN.md), and H-43 through H-50 in [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md). The live checkpoint barrier remains Candidate, default off.

Baseline: `6ed6bf1`, isolated worktree `../codex-reliability`, branch `codex/coop-reliability`.
This is an **incomplete development branch**, not a multiplayer release. Original game installations and saves remain unchanged.

## Implemented

- Installer wizard and silent-install preparation check for readable, non-empty Tables.pak, Scripts.pak, IPL_GameData.pak, and Characters.pak before changing mod files. An incomplete workspace receives the WorkspaceSetup explanation. Detection fixtures exercise the actual Inno code.
- Immutable content-addressed checkpoint manifests and verified world/character/ledger artifacts. Every referenced artifact must exist and hash correctly before publication. Ancestry comparison rejects incompatible or ambiguous automatic selection. Artifacts are not automatically pruned.
- `ReconcileService`: verified checkpoint capture, playthrough-seed matching, ancestor/descendant selection, explicit divergent-branch selection, refusal when the selected checkpoint lacks a participant or two participants own the same item instance, and verified preparation of each paired character as a personalized save using the existing splice/check/quest-item filtering. Both branches remain archived. Preparation never writes a game playline or declares a live session ready.
- Normal rejoin prefers an exact world/character pair and otherwise a pair on the host's announced branch. It no longer silently uses the newest unrelated snapshot. This existing branch list is not yet the new manifest ancestry protocol.
- Body take/put requests and replies carry a random connection/load scope. Repeated requests replay their original result without mutating inventory twice; conflicting or uncertain operations do not execute again. Replies from a prior local load/connection scope are rejected. Old protocol peers are refused: protocol 11.
- Host inventory mutations are checked by readback, not merely successful pcall. An inert deletion cannot be acknowledged as successful. Uncertain results remain quarantined in memory; this is **not** a durable transaction/escrow system.
- Mod packaging includes the new Lua operations module and fixed archive timestamps for reproducibility; the Lua synthetic driver executes it with the real mod.

## Checkpoint tool

Runs before normal configuration/log setup and does not contact the game:

```text
KcdMpClient --checkpoint-tool --store <archive folder> --capture <capture-request.json>
KcdMpClient --checkpoint-tool --store <archive folder> --list
KcdMpClient --checkpoint-tool --store <archive folder> --local <checkpoint UUID> --remote <checkpoint UUID>
KcdMpClient --checkpoint-tool --store <archive folder> --local <UUID> --remote <UUID> --choose <one of those UUIDs> --out <new staging folder> --tables <installed Data/Tables.pak>
```

Capture input (JSON uses these exact property names):

```json
{
  "WorldId": "persistent 32-hex UUID",
  "BranchId": "32-hex UUID",
  "ParentId": null,
  "ContentFingerprint": "64-hex SHA-256 of the verified content profile",
  "WorldFile": "path to a verified world save copy",
  "Characters": [{
    "ParticipantId": "persistent 32-hex UUID",
    "CharacterKind": "Henry",
    "CharacterBlock": "path to its paired .hblk",
    "ChestLedger": "path to its paired chest ledger JSON"
  }]
}
```

The caller must supply a consistently captured, paired roster. The offline tool checks file integrity, builds, kinds, ledger shape and references; it cannot establish live capture timing or prove that arbitrary manually selected inputs form a consistent economy. Use disposable copies. `PREPARED.txt` means verified staging only. It does not authorize dropping files into a live playline.

Character snapshots must carry the selected playthrough's seed (capture after joining, not an unrelated pre-join Bring source). Duplicate item-instance ownership is refused; this check does not replace a complete shared economy ledger.

## Verification on 2026-10-06

| Check | Result |
|---|---|
| Client .NET suite | 1,272 passed |
| Relay .NET suite | 62 passed |
| Farkle .NET suite | 59 passed |
| Real Lua world-item synthetic suite | 64 passed |
| Lua drop regression | 55 passed |
| Lua load/presence regression | 50 passed |
| Lua 5.1 main-chunk local-count gate | 6 passed |
| Actual Inno Steam/data detection fixtures | 24 passed |
| Launcher build | Passed, existing nullable warning |
| Development pak build, no install | Passed |

These are offline/synthetic checks. No two-computer or real-engine gameplay pass was performed in this implementation run. The full installer was not compiled against a newly published matching runtime payload.

## Room contract and participant identity (2026-10-07, protocol 12)

* One shared contract (`Coop.Contract`; the same source and test vectors as the KCD1 branch) decides who may share a room. A client's handshake carries the game id, release, wire and contract version, agent/Lua/native/engine/content hashes and an honest capability table. The relay refuses another game, contract or wire version, and a different or unverifiable `kdcmp.pak`, with a sentence the player can read. See [CAPABILITIES.md](CAPABILITIES.md).
* Participants are bound to a P-256 key by challenge/response; the relay persists the bindings (`Contract:BindingsFile`). Replayed or stolen participant ids are refused.
* Every room has a mode (Presence, Partial or Shared simulation), announced to every ready client (`0x49`) and surfaced in the launcher and game. A normal room reports **partly shared**: loot durability and the live checkpoint barrier are open. Admission takes the **weakest** agreement with anyone already present.
* Installs with other DLC or mods are admitted as presence only; `mp_join_request` is refused for them.
* `OperationJournal` (hash-chained, durable, forward-only states, quarantine on recovery) and `AuthorityGuard` (epoch and incarnation checks, double validation under one lock) exist with tests. They are **not yet attached** to the loot, economy or combat paths. That attachment is the remaining work for the loot gate below.
* `Contract:Required false` and `Contract:AllowUnverifiedPayload true` exist for synthetic test peers and payload smoke tests only; the product defaults are strict.

Validation on 2026-10-07: relay 71, client 1,290, Farkle 59 .NET tests pass; the 46 synthetic Lua suites (including static gates) exit 0; native `KCDMP_NativeTests.exe` 403 passed; `KCDMP.dll` and the injector build in Release. No two-computer or real-engine gameplay pass was performed. Human tests: [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md).

## Gates not completed

| Plan part | Remaining work |
|---|---|
| Reconnect end to end | Live consistent capture, persistent player identities, manifest exchange/import, selected-guest world promotion, authority epochs, save-watcher integration, barrier/ready/abort recovery and player UI |
| Combat | One health/death authority coordinated with NPC movement claims, unique hit IDs, death revisions and stale-state rejection |
| Loot | Pre-transfer hook or controlled escrow/UI; exact item identities and metadata; host-local operations in the same authority queue; loose items/drops/takedowns; durable operation journal; save barriers; recovery of ambiguous mutations |
| Save/load isolation | Global authority incarnation in all gameplay messages and cancellation of queued old-world mutations. Current scopes only protect body request/reply correlation and retries; they are not full world epochs |
| Compatibility | Lua pak and DLC/mod content profile are enforced at admission (2026-10-07); native plugin and agent hashes are reported but not enforced; a matching-package audit on real installs is outstanding |
| Release | Two/four-computer gameplay, long sessions, installer compile with a newly published matching payload, upgrade/rollback and final installers |

Automatic recovery of uncertain inventory transfers is intentionally not invented: a journal and a running engine are not one atomic transaction. Do not ship this branch as duplication-proof. The existing optimistic loot screen can still let a player use an unconfirmed item; preventing that requires the pre-transfer/escrow gate.

VERSION stays at 0.45.0 (the user owns the version). Protocol 12 distinguishes these agent/relay sources from the baseline and now validates the Lua pak and content profile; the native plugin is reported but not enforced. Do not mix this branch's agent with an older pak.

## Continue from here

1. Preserve this development branch and read the KCD1 sibling's capability report.
2. Prove pre-transfer control in disposable KCD2 worlds and implement a durable authority actor/save barrier. Do not bypass that gate with further optimistic rollback.
3. Integrate live checkpoint capture/manifest exchange/promotion with that authority barrier. Offline selection and preparation are already tested.
4. Prove KCD1 exact character and animation bindings on disposable worlds; implement its required native bridge where Lua is insufficient.
5. Complete both games' live/human acceptance gates before rebuilding a distributable release or installing over the original dev mods.

## Session 2 continuation: 2026-10-07

This remains a playtest build. No capability has human acceptance, and the two games do **not** yet have equivalent shared simulation. VERSION remains KCD1 0.1.0 / KCD2 0.45.0; distinguish downloads by release tag, commit and SHA-256, not VERSION alone.

* KCD2 protocol 13 refuses older agents/relays. Durable host body take/put and loose-item decisions are journaled before the Lua mutation and before the reply. Interrupted unknown mutations are quarantined, never retried automatically. `HostDecisionComplete` means the host decision is durable, not that the recipient received/kept an item. Loose-item replies now carry the same connection/load scope as body replies. Unknown loose items are refused; successful removal requires entity readback.
* KCD2 checkpoint barrier is **Candidate, default off**. Host console: `mp_checkpoint_mode candidate` (off reverses it for this agent session). It uses a fixed relay-verified participant roster, authority incarnation and checkpoint UUID, acknowledged native holds, loot settlement, a verified host save, bounded/acknowledged guest character uploads, matching save MD5s, chest ledgers, cross-participant item-instance checks, durable prepared artifacts, commit acknowledgements, and manifest publication before release. Disconnect, roster/load change, missing data or the 90-second deadline aborts. The native hold expires 20 seconds after the agent's last refresh. A guest keeps its paired personal artifacts and receipt; it does not yet receive a full host checkpoint archive for offline reconciliation.
* Checkpoints still need proof that native saves work while held and that **all** inventory/quest/combat mutations are excluded. The Lua pickup gate is not complete native inventory interception. Ordinary autosaves still follow the existing path; interrupted capture is not automatically promoted. Offline checkpoint selection/preparation remains separate from live reconnect promotion.
* KCD1 quest mirror is **Candidate, default off**. Console `kcdus_quest_mode candidate` on both test clients enables one-way host snapshots; `off` disables it. Identifiers come from the installed quest tables. Only open, base-game main/side/activity quests are eligible. DLC, rails/mixed scenes, local system/random events and unknown objectives are vetoed. It reads native state before applying and after mutation, replays completed objectives without repeating the call, refuses inactive objectives and unverified quest completion, and never treats cancel/deactivate as success. Objective reward/spawn side effects and full quest/variable coverage remain unproved.
* Private KCD1 engine evidence: `q_revenge/findVonAulitz` changed from started/not-completed to completed through the real adapter; repeated application read back completed. This proves one objective binding, not full quest authority. `t_scale=0` stopped observed calendar progression; it was restored to 1. Production shared pause remains **Absent**: there is no proved crash-safe engine lease or menu-pause interception. A Lua timer cannot safely release a freeze that stops its timers.
* KCD1 enemies/combat decision: **do not enable shared combat/NPC drive**. Existing damage probes did not prove the ordinary hit path; presence NPCs are not host-authoritative combatants. Damage interception, AI suppression/drive, targeting all players, one death/reward revision and a shared inventory ledger remain implementation gates. Do not replace this with cosmetic attacks or health setters.
* KCD2 shared pause and its off option remain Candidate until the two-computer tests pass. DLC uses the host's lower-content world: richer guests are allowed and extra DLC quest mirroring is gated; poorer guests cannot receive an existing richer host save. Disable extra DLC in Steam and restart as instructed. The mod does not rewrite a DLC save or deactivate Steam entitlements. Different other mods still cap the room at presence and block world/character moves.
* Linux packages are experimental. A successful WSL build/fake Steam test is not proof of Proton gameplay. KCD1's Windows startup adapter paths are not proved under the native Linux agent. No Linux engine feature is upgraded by rebuilding a tarball.

Human acceptance is pending. KCD1 ESC-menu Multiplayer entry and its Status/Host/Join/Leave/Game world/Story/Keys/Settings/Back page were visually observed in the disposable loaded game. That does not prove buttons in a connected session. KCD2 pause-menu behavior still needs verification. Run the new test cases on disposable copies, record release/commit hashes and logs, and leave failed capabilities Candidate. No real saves or firewall rules were changed by this continuation's guarded probes.

## Session 3 (2026-10-08): KCD1 notes above are superseded

The KCD1 lines in the Session 2 paragraphs above ("Production shared pause remains Absent", "do not enable shared combat/NPC drive", "existing damage probes did not prove the ordinary hit path") no longer hold. The KCD1 damage probe had passed health 0 (`DealDamage(3, 0, ...)` is stamina 3, health 0); with health damage the ordinary path lowers health and kills. KCD1 now has shared OUTCOMES of fights (health and death of the NPCs near each player), host-decided loot of corpses and stashes, and a crash-safe shared pause lease, each proved in a private engine with a synthetic second player. See the KCD1 repository's `docs/CAPABILITIES.md` (sections "Shared fights and loot" and "Pausing"). This KCD2 repository is unchanged by that work; KCD2 keeps its own native combat, loot journal, shared pause and candidate checkpoint as described above.
