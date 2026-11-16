# Design: friends list in Discord, numbered tells (#36)

Status: **research done, awaiting the owner's decision.** Only a read-only research command (`/dcpfriends`) exists. Typed-name tells already work: `!t Ane Mora@Famfrit hello` (#26).

## What the client holds

FFXIVClientStructs (the version Dalamud API 15 ships) exposes the friend list as `InfoProxyFriendList` (`InfoProxyFriendList.Instance()`), a list of `InfoProxyCommonList.CharacterData`:

| Field | Use |
|---|---|
| `NameString` | Character name |
| `HomeWorld`, `CurrentWorld` | World row IDs → `World` sheet names. `HomeWorld` is what `/tell Name@World` needs. |
| `State` (`OnlineStatus` flags, 64-bit) | `Online`, `Busy`, `AwayFromKeyboard`, `InDuty`, `ViewingCutscene`, `PartyMember`, `AnotherWorld`… |
| `IsOtherServer` (`ExtraFlags` 0x1000000) | Friend is on another server; the client gets less data (no FC tag). Matches the owner's note that other-server online status is unreliable. |
| `WaitingForFriendListApproval` | Pending request, not a friend yet |
| `ContentId`, `AccountId` | **Never read.** Dalamud rule R3 forbids collecting other players' account IDs, and we don't need either. |

Methods include `GetEntryCount()`, `GetEntryByName(name, world)` and `RequestData()`. `AgentFriendlist.RequestFriendInfo(contentId)` is documented as "triggers a network packet send".

## How loading works

- The list is filled from server packets in **pages** (`AddPage` / `EndRequest`). That's the "loads incrementally" behaviour the owner saw.
- It's requested when the Friend List window opens (the game calls `RequestData()`), and possibly partly at login. **To verify in game:** after login, before opening the window, does `/dcpfriends` show 0, some, or all friends?
- "Finished loading" can be detected as `EntryCount` stable and the proxy's `CurrentRequestId == NextRequestId`. To confirm with the probe.
- Up to 200 friends.

## Refreshing: what's allowed

| Approach | Server traffic | Rules |
|---|---|---|
| **Read what's already loaded** (owner opened the Friend List at some point) | None | ✅ Same as reading chat. |
| Call `RequestData()` from Discord | Same request the game sends when you open the window, but **triggered from Discord without in-game input** | ⚠️ R2 ("automatic" requests). Similar risk class to send-as-me: opt-in only. |
| `RequestFriendInfo` per friend | One packet per friend | ❌ Not proposed. |

## Online status honesty

| Friend | What we can show |
|---|---|
| Same world / same data centre | 🟢 online (plus busy / AFK / in duty from the flags) or ⚫ offline. Reliable once the list has loaded. |
| `IsOtherServer` | ❔ unknown unless the `Online` flag is set. Never claim "offline". |
| List not loaded yet | Say so: "open your Friend List in game once to load it". |

The status is a **snapshot** of when the client last received the list, not live. The listing says how old it is.

## Proposed UX (for approval)

1. `xl!friends` (owner only) → an embed, paged by 25:
   ```
   Friends (loaded 3 min ago, 42 total)
   1. Ane Mora@Famfrit 🟢 (in duty)
   2. Bob Smith@Gilgamesh ❔ other server
   3. Cid Garlond@Famfrit ⚫
   ```
   Online friends first, then alphabetical (same order every time the list is unchanged).
2. `!t 1 hello` (or `!1 hello`) sends a tell to friend #1 from the **last listing**. Numbers come from a snapshot stored when `xl!friends` ran, so they don't shift if the list changes. Snapshots expire after e.g. 30 minutes; after that, ❌ "run xl!friends again".
3. A number resolves to `Name@HomeWorld` and then goes through exactly the same path as `!t Name@World` (display, or send-as-me if ticked). Nothing new reaches the server.
4. Threading: read on the framework thread (`RunOnFrameworkThread`), snapshot to plain records, format and send from the gateway.

Rules: steps 1–3 read local data only (✅). The optional auto-refresh (`RequestData()` from Discord) would be ⚠️ R2 and opt-in.

## Research command

`/dcpfriends` (hidden, read-only) prints every friend the client currently holds: `n. Name@HomeWorld (on CurrentWorld): online/offline/unknown [request pending] (flags 0x…)`. Nothing is sent to the server or to Discord.

**Owner checks, please:**
1. Right after login, without opening the Friend List: how many friends does `/dcpfriends` show?
2. After opening the Friend List and waiting a few seconds: all of them? Are the statuses right for same-world friends?
3. For friends on another world or data centre: what does it show for someone you know is online?

## Questions for the owner

1. Approve the read-only `xl!friends` + `!t <number>` design above?
2. If the list isn't loaded after login, is "open your Friend List once" acceptable? Or do you want the ⚠️ opt-in auto-refresh?
3. Number syntax: `!t 3 hi` only, or also the shorter `!3 hi`?
