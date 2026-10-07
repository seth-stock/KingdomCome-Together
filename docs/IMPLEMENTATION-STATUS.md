# Co-op reliability implementation

Baseline: `6ed6bf1`, isolated worktree `../codex-reliability`, branch `codex/coop-reliability`.
This is an **incomplete development branch**, not a multiplayer release. Original game installations and saves remain unchanged.

## Implemented

- Installer wizard and silent-install preparation check for readable, non-empty Tables.pak, Scripts.pak, GameData.pak, and Characters.pak before changing mod files. An incomplete workspace receives the WorkspaceSetup explanation. Detection fixtures exercise the actual Inno code.
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
