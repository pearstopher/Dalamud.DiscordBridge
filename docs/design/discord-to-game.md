# Design: Discord → FFXIV relay

> **Status:** implemented in #13–#16 (2026-11-16). User docs: [README](../../README.md). Test: [IN_GAME_TEST_PLAN.md](../IN_GAME_TEST_PLAN.md).
>
> Research for #9 (2026-11-16). **Decision:** Option A + B, phased: local display first (#13–#15), then opt-in owner "send as me" (#16), docs in #17. Plugin is distributed via the owner's custom repo and renamed **Discord Chat Pears** (#11). It is not bound by Dalamud rules; compliance is recorded in [../DALAMUD_RULES.md](../DALAMUD_RULES.md).



## TL;DR
There are two fundamentally different meanings of "show up in the in-game chat":

1. **Display**: the Discord message is printed into **your** chat log only. No server interaction, no risk, and anyone in the Discord channel can use it.
2. **Send**: your character actually **says** it in FC/LS/CWLS so other players see it. Technically easy, but it has real policy/account risk that depends on **who** typed the message.

**Recommendation: Option A + B (phased).** Build Display first as the foundation. Then add an opt-in, owner-only "send as me" mode on top.

---

## Technical findings (verified against the local Dalamud API 15 / FFXIVClientStructs assemblies)

| Need | API | Notes |
|---|---|---|
| Print locally | `IChatGui.Print(XivChatEntry)` | `XivChatEntry { Type, Name, Message, Timestamp, Silent }`. Can use any `XivChatType` (e.g. `FreeCompany`) so it's coloured and filtered into chat tabs like real FC chat, with `Name` = e.g. `[Discord] Alice`. Client-side only. |
| Send to server | `UIModule.Instance()->ProcessChatBoxEntry(Utf8String* message, nint a4, bool saveToHistory)` | Same path as pressing Enter in the chat box. Must run on the **framework thread**. The text is processed like typed input, so a leading `/` **executes a command**. |
| Sanitize | `Utf8String.SanitizeString(AllowedEntities, Utf8String*)` | Used by ChatTwo/ChatAnywhere to reject characters the game won't accept. The game limit is **500 UTF-8 bytes**. |
| Discord receive | existing `DiscordHandler.SocketClientOnMessageReceived` | Already has the `MessageContent` intent. Non-command messages are currently ignored. `DiscordChannelConfig.IsBidirectional` already exists (unused), left by the original author for exactly this. |

**Prior art**
- **ChatTwo** (official repo) and **ChatAnywhere** (a browser chat client, 2026) send the user's own typed text via `ProcessChatBoxEntry` with byte-length and sanitize checks, a pattern we can reuse. ChatAnywhere adds a **send queue with a cancel countdown** as a safety net.
- **ViMaSter/FFXIVDiscordChatBridge** (archived) did full two-way relay by simulating keyboard input, and **required a dedicated FFXIV account on a dedicated machine**. That's a sign of the risk of letting arbitrary Discord users talk through a character.
- Upstream's README says reverse relay was deliberately not done because "Discord allows a lot of features that would not map to chat in FFXIV".

**Policy** ([Dalamud plugin restrictions](https://dalamud.dev/plugin-publishing/restrictions))
- Plugins must not interact with game servers in a way that's **"automatic … making requests without direct interaction from the user"** or **"outside of specification"**.
- Display (A) doesn't touch the server at all.
- Owner-typed send (B) is the same category as ChatTwo/ChatAnywhere: you typed it, just somewhere else.
- Relaying *other people's* messages through your character (C) is automatic by definition and would be rejected from the official repo. On Square Enix's side, spam/harassment through your character is on your account.
- This is a personal fork that isn't distributed, so the rules aren't enforced on us, but they're a good proxy for account risk.

## Cross-cutting design points (apply to all options)
- **Loop prevention:** a printed or sent message comes back through `ChatMessage` (sent ones definitely do, as FC chat from you; printed ones are expected to, to be confirmed in-game). It would then be relayed back to Discord. Track pending inbound messages and drop the matching echo, plus a marker on printed entries.
- **Channel mapping:** each Discord channel gets an explicit inbound target, e.g. `xl!setinbound fc` (replacing the unused `IsBidirectional` with `InboundChatType`). A channel can subscribe to many outbound types, so the inbound target can't be inferred.
- **Who may relay:** owner only / a Discord role / everyone (Display mode only).
- **Content conversion** (pure, unit-testable): use `CleanContent` for mentions; custom emoji `<:name:id>` → `:name:`; Unicode emoji the game font can't render → strip or `:shortcode:`; attachments/stickers → `[image]`/`[sticker]`; replies → `↪ Bob: …`; collapse newlines; truncate to 500 bytes with `…`; run SanitizeString.
- **Only while logged in.** Otherwise drop the message, or queue it for a short time and react ⏳/❌ on Discord.
- **Feedback on Discord:** react ✅ when delivered and ❌ when rejected (too long, not logged in, not in an FC…).
- **Multiple bridge users** on one channel: with Display, each sees it locally, so no duplicates. With Send, only the author's own plugin may send (owner-only solves this naturally).

---

## Option A: Display only ("mirror")
Discord messages from bridged channels are printed into your chat log, styled as the mapped chat type, e.g. `[FC] [Discord] Alice: hello`.
- ✅ Zero server interaction, so no ToS or Dalamud-rule exposure. Works for every Discord member. No duplicate problems. No command injection possible.
- ❌ Other players in-game don't see it. It's a read-only window onto Discord.
- Effort: small–medium (≈4 issues).

## Option B: Owner "send as me" (opt-in)
When **you** (the configured owner) post in a channel with send enabled, your character says it in the mapped channel (`/fc`, `/l1`, `/cwl1`, `/p`…). Everyone else's Discord messages fall back to Display (A).
- ✅ Lets you actually talk to your FC/LS from your phone. Precedent: ChatTwo/ChatAnywhere. No duplicate sends.
- ⚠️ Requires the game running and logged in. Moderate risk. Guards required: owner-only, refuse anything starting with `/`, the plugin adds the channel command itself, 500-byte limit, sanitize, rate limit (≥1–2 s between sends), off by default, optional ✅ confirm-reaction or cancel window.
- Effort: medium (A + ≈2 issues).

## Option C: Full relay through your character
Any allowed Discord member's message is sent into game by your character as `[D] Alice: hello`.
- ✅ True two-way chat: FC members in-game see Discord users.
- ❌ Highest risk: your character speaks for others automatically (exactly what Dalamud bans, and what SE could treat as spam). Needs role allowlists, rate limits, and moderation. Only sensible on a **dedicated alt account**, like the prior-art bridge.
- Effort: medium-large.

## Option D: Separate companion app / out-of-game client
Not a plugin feature at all: use or point users to an external tool. Listed for completeness and not recommended. It's out of scope for this repo.

---

## Recommended plan (A then B): draft issues
1. **Inbound config & routing:** `InboundChatType` per channel (migrate the unused `IsBidirectional`), `xl!setinbound <slug|off>`, `xl!setinboundperm <owner|role:@x|everyone>`, `MessageReceived` routes non-command messages into a new inbound queue, `help` + docs.
2. **Discord → game text converter** (pure + unit tests): mentions, emoji, attachments, replies, newlines, 500-byte truncation, sanitize.
3. **Local display:** print on the framework thread via `IChatGui.Print` with the mapped type and a `[Discord] name` sender, only when logged in, toggle in `/pdiscord`, ✅/❌ reactions.
4. **Loop prevention:** suppress the outbound relay of our own printed/sent messages (with tests for the matcher).
5. **(B) Owner send-as-me:** `ProcessChatBoxEntry` sender with the channel-command map, slash-command guard, rate limit, opt-in `xl!togglesend`, falls back to display for non-owners.
6. **Docs & in-game test plan** for the whole feature.

Depends on: #2 (test project) for the unit tests in 2 and 4.
