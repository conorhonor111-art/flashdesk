> # HANDOVER — 2026-08-06. READ THIS BEFORE ANYTHING ELSE.
>
> Written because a session was about to be cleared and everything not in a file would have been
> lost. It lives here rather than in `short.txt` because that file is gitignored and rewritten on
> every reply — a handover kept only there would be destroyed by the next message.
>
> **Two features are HALF BUILT. Neither is finished and neither is published.**
>
> ## Read these first, in this order
> 1. **`CLAUDE.md`** — the charter. §4 rule 1 changed today; read it.
> 2. **This handover**, then the rest of `PROGRESS.md`.
> 3. **`C:\Users\PC\.claude\plans\file-transfer-both-directions.md`** — the approved file-transfer
>    plan with every decision and its reasoning. The other plan file, `fluffy-petting-blossom.md`,
>    is SUPERSEDED.
> 4. `src\RemoteDesktop.Shared\Files\` — `RemotePath`, `FileWire`, `FileMessages`, `ExecutableContent`.
> 5. `src\RemoteDesktop.Host\Files\OpenedPath.cs` — the handle re-check, and why a string cannot do it.
> 6. `src\RemoteDesktop.Shared\Displays\DisplayLayout.cs` and `src\RemoteDesktop.Host\Capture\DisplayEnumerator.cs`.
>
> ## Committed today — HEAD is `0c85244`, `dotnet test` 162 passing
>
> | | |
> |---|---|
> | `b47f053` | Design pass: page, simple view, consent dialog, mark |
> | `d0b180f` | Version 0.3.1 |
> | `4cd127e` | Desktop layout, asset check in Check-LiveBuild, wording fix |
> | `7776a2a` | Site: new contact address, warning moved and calmed, Windows screens corrected |
> | `0bccac0` | **Injected input stamped** — the remote hand can no longer answer a consent prompt |
> | `0298115` | Message cap 64 MB → 16 MB, both boundaries asserted |
> | `461fec5` | `RemotePath` read-side rules + tests |
> | `329974f` | File wire format; capability appended to the handshake, older builds unaffected |
> | `ff701a6` | Multi-monitor coordinate translation + 20 tests |
> | `5da0333` | **Charter fix**: consent dialog shows the verified number, never a name |
> | `72fa7de` | Write-side path rules; session-log promise retired deliberately |
> | `79d4218` | Arriving files judged by first bytes, not by name |
> | `cd79bb6` | **Handle re-check**, proved against a real junction |
> | `4040481` | Listings paged |
> | `0c85244` | Multi-monitor capture switching + `VIRTUALDESK` — **UNVERIFIED** |
>
> ## Next, in order
> 1. **Temp-file cleanup on start** — a hard drop leaves a partial upload on the client's disk.
> 2. **Link-sized chunks** — 256 KB is fixed; size it from the measured rate so one chunk never
>    holds the send lock longer than ~200 ms. On a slow uplink a fixed chunk freezes the picture
>    rather than slowing it.
> 3. **The host file service** — listing (paged, off the message-loop thread), chunked read, chunked
>    write, the two consent prompts, the overwrite question, cancel from either side.
> 4. **The viewer file panel** — must NOT take focus: any focusable control there releases every held
>    key and suspends remote control.
> 5. **The client indicator** — second line under the amber band, direction arrow, restore-without-focus.
> 6. Per-monitor DPI, as its own stage.
>
> ## UNVERIFIED — most of multi-monitor. Do not describe any of it as working.
> - that a second display is ever **enumerated**. Never seen one.
> - that `SwitchTo` actually changes screens. On one screen it rebuilds the same output, so the
>   branch that matters has never executed.
> - that a click lands on the right screen. The arithmetic has 20 unit tests; that DXGI **feeds** it
>   the right numbers is untested.
> - that `VIRTUALDESK` behaves as documented. Set, never observed.
> - that DXGI's output order matches what a person calls left and right.
> - mixed DPI. Blocked on the deferred per-monitor decision.
> - the fallback when the captured monitor is unplugged mid-session.
>
> Also unverified: the claim that a transfer slows the picture. Reasoned from the code, never
> measured. Report the real number after a real transfer.
>
> ## Decisions Conor has already made. DO NOT ASK AGAIN.
> - **Do NOT republish the exe.** `0.3.1` stays the public download until BOTH file transfer and
>   multi-monitor are finished. `Check-LiveBuild` section 3 failing is EXPECTED. A half-built build
>   is worse than an old one.
> - **Per-monitor DPI is deferred** to its own stage. For now: detect mixed scaling and TELL the
>   operator rather than misclick silently. The client window gets checked by eye at 100/150/200 %.
> - **Sorting is disabled while a listing has more pages**, with the reason on screen. Folders before
>   files always. A sorted window that looks like a sorted folder is a lie.
> - **Transfer, never manipulation.** No delete, rename, move or new folders — the operator has mouse
>   control for those, so a file API would only make them invisible. Transfer is the one thing screen
>   control cannot do; that is the feature and its limit.
> - **Consent identifies the caller by the verified 9-digit number, NEVER by a name.** A name is
>   whatever the caller types, so a scammer would put "Microsoft Support" in it. Governs every
>   consent surface, present and future.
> - **Restore the window without taking keyboard focus** when file activity starts while minimised —
>   someone may be typing a password. On a state CHANGE, not per file, not more than once every few
>   seconds. Promise it on the download page.
> - **The two-display rig is unreachable and may stay that way.** `.223` is itself a VMware guest;
>   no VMware, no `vmrun`, no `.vmx`, no datastore share. Do NOT touch `TrustedHosts`; there is no
>   password for `.222`. **STOP WAITING FOR IT.** The only real two-screen machine is the one
>   belonging to the person Conor helped, and that is where multi-monitor gets verified.
> - **FlashDesk must run in `.222`'s CONSOLE session, not over RDP,** to see two screens: an RDP
>   session has its own virtual display with as many monitors as the RDP *client* offers.
> - The session log now records file names, deliberately. The old "no file names" promise is retired
>   with its reasoning written into `SessionLog.cs`.
> - The `.bat`-renamed-to-`.pdf` gap is **accepted and closed as a question**: a file only runs by its
>   extension, renaming it back happens on screen and lands in the log. Do not spend more on it.
>
> ## Outstanding for Conor, not for the next session
> - Confirm `support@flashdesk.org` is a real mailbox. The page now sends wary strangers there.

> ## WHERE THIS PROJECT IS — read this first (2026-08-05)
>
> **FlashDesk works, end to end, from the file a stranger actually downloads.** A full session was
> run on two machines on 2026-08-05: GitHub download, two 9-digit numbers, consent dialog, screen
> shared, control working — and the screen was locked mid-session to watch the recovery behaviour
> appear and clear. All four capture fixes are **CONFIRMED IN A LIVE SESSION**, not inferred. The
> browser download block is **gone**, solved for €0 by moving the file to a GitHub release after
> proving the block was caused by the HOST and not the file. One Windows warning remains, it is
> passable, and the download page now quotes both of its screens word for word.
>
> **What happens next:** Conor sends `tester-message.txt` to two people who have never seen
> FlashDesk and watches using `watch-list.txt`. That is the test this whole project was built for —
> two strangers, one file, no help. The single measurement that matters is whether each person gets
> past the Windows security screen **unaided**; that count is now the trigger for buying a code
> signing certificate. The one thing still never proven is **adaptive picture quality across two
> genuinely separate home internet connections** — every measurement so far has been a simulated
> link or a deliberate throttle, and the testers' `sessions.txt` will be the first real evidence.

# PROGRESS

Short running log of what was built, decided, and left unfinished. Newest stage at the bottom.

## Stage 0 — Set up the workshop (done 2026-07-29)

.NET 8 SDK 8.0.423 and git 2.55 installed on the dev PC. Repo at `C:\Dev\RemoteDesktop`,
solution with three projects (Host, Viewer, Shared), first commit made. Test machine confirmed
reachable and set to a Private network profile. Charter `CLAUDE.md` written, then committed on its
own so it survives a code rollback.

**Decided:** test roles — HOST is the physical dev PC `192.168.1.223` (so the DXGI capture path
real clients use actually gets exercised); VIEWER is the VM `192.168.1.222`. UI is WinForms, not WPF.

## Stage 1 — See the other screen (built + verified on two machines 2026-07-29)

Built two programs plus the shared protocol. Host captures the primary monitor and streams changed
128×128 tiles as JPEG over TCP 7789; Viewer connects by IP and shows the assembled picture.

**Shared (`RemoteDesktop.Shared`)** — platform-neutral wire protocol: `ProtocolConstants`
(port 7789, tile 128, JPEG quality 70), `MessageType`, length-prefixed `MessageChannel`,
`Handshake`, `ScreenInfo`, `FramePacket`/`TileUpdate`, `PingPayload`, and `RateMeter` (1-second
FPS/bytes window).

**Host (`RemoteDesktop.Host`)** — `IScreenCapture` with `DxgiScreenCapture` (Vortice
Direct3D11+DXGI 3.8.3) and `GdiScreenCapture`, chosen by `ScreenCaptureFactory` (DXGI first, GDI on
failure, reason reported). `TileDiffer` (FNV-1a hash per tile) + `JpegTileEncoder`. `HostServer`
runs capture→diff→encode→send off the UI thread and answers latency pings on a second task.
`MainForm` shows local IP, capture method, viewer state, FPS, KB/s.

**Viewer (`RemoteDesktop.Viewer`)** — `ViewerClient` (outbound connect, handshake, background
receive + ping loops). `RemoteScreen` holds ONE session bitmap and stamps tiles in via LockBits.
`ScreenCanvas` is a double-buffered control that draws it scaled-to-fit and invalidates only
changed rectangles. `MainForm` has the IP box, Connect button and a status bar (FPS / latency / KB/s).

**Decided this stage:** latency is round-trip ping on the viewer's own clock (no cross-machine clock
compare). Host keeps sending — and its counters keep moving — while the viewer is minimised, because
the viewer's receive loop is a background thread (this is what makes single-monitor measurement work).

**Stage 1 baseline — measured on the real two-machine setup (Host .223 → Viewer .222), at the
original JPEG quality 70:**

| Condition | Capture | FPS | Bandwidth | Latency |
|---|---|---|---|---|
| IDLE (static desktop) | DXGI Desktop Duplication | 13 | 0.2 KB/s | 1 ms |
| BUSY (screen changing) | DXGI Desktop Duplication | 13 | 76.4 KB/s | 1 ms |

Capture ran on the real DXGI path (not the GDI fallback), as intended by pinning the host to the
physical PC.

### Stage 1 legibility fix (2026-07-29) — text was too soft to read

Text legibility is now the primary quality metric, ahead of framerate. Changes:
- **Default JPEG quality 70 → 85**, as named constants in `Shared` (`DefaultJpegQuality`,
  `MinJpegQuality` 60, `MaxJpegQuality` 95). Idle costs ~0.2 KB/s, so the headroom buys sharpness.
- **Live quality control** on the host window (dropdown 60–95 in steps of 5), changeable during a
  session so the operator tunes by eye; `HostServer.JpegQuality` -> `JpegTileEncoder.SetQuality`
  swaps the encoder parameters atomically.
- **Scaling was the main softness cause.** The viewer was downscaling the full host resolution into
  a ~1000 px window; downscaling blurs every glyph regardless of JPEG. Fixes: high-quality
  interpolation (`HighQualityBicubic`) in Fit mode, plus an **"Actual size (1:1)"** toggle with
  scrollbars to see true pixels. JPEG quality is the secondary softener (chroma on coloured edges).
- **Chroma subsampling:** GDI+ exposes no dedicated subsampling flag — only Quality — and it drops
  subsampling toward 4:4:4 as quality rises, so raising quality is also what sharpens coloured text.
  If coloured edges are still poor at 90–95, the next lever is a different JPEG encoder (deferred).

**Left for later:** encoding still allocates a small Bitmap per changed tile (fine for a support
screen; revisit only if measured too slow). Re-measure IDLE/BUSY KB/s at quality 85 during the
legibility retest.

**Legibility retest result (Debug host — see the Release rule now in CLAUDE.md):** quality 80 good,
85 better (kept 85 as default); 1:1 vs fit about the same on the tested content. Downscaling
confirmed as the main softener. IDLE ~13–16 KB/s and BUSY 16→400 KB/s were read from a Debug host,
so they are not trustworthy — re-measure under `-c Release`.

## Stage 2 — Take control (built 2026-07-29; live input test pending, uses SWAPPED roles)

Mouse and keyboard from the viewer are injected on the host with Win32 `SendInput`; each frame now
also carries the host cursor position so the viewer can draw the remote pointer.

- **Shared:** `InputEvent` (mouse move / button / wheel, and keys by **scan code**; fixed 17-byte
  layout), `MessageType.Input`, and `FramePacket` gains cursor x / y / visible.
- **Host:** `InputInjector` — `SendInput` with absolute mouse (0..65535 across the primary screen),
  keys by scan code with the extended flag, wheel; tracks held keys/buttons and `ReleaseAll` on
  disconnect, timeout and shutdown so nothing sticks. `HostServer` injects incoming input on its
  inbound loop and reports the cursor (`GetCursorPos`) in every frame.
- **Viewer:** `InputCapture` forwards mouse/keyboard only while **Control remote is ticked AND the
  picture has focus** (safe opt-in). Coordinates mapped to host pixels by `ScreenCanvas` (Fit and
  1:1); keys converted VK → scan code via `MapVirtualKey`. `ViewerClient` sends input through an
  ordered queue (one sender task) so events stay in order regardless of the frame stream.
  `ScreenCanvas` draws the remote pointer as an arrow at the mapped position.

**Decided:** input is opt-in (Control remote default OFF) and only sends while the picture has focus,
so the operator's own machine is never driven by accident. SendInput (not the deprecated
`mouse_event`/`keybd_event`) because those inject one event at a time and do not integrate with the
modern raw-input path.

**Test setup swap (in CLAUDE.md):** for input testing only, `.222` is the HOST and `.223` is the
VIEWER, to avoid a mouse feedback loop (driving `.223`'s real cursor, which sits under the VM window).
All video/capture work keeps `.223` as host. Both sides publish as self-contained single-file win-x64
exes (Host 64.8 MB, Viewer 64.7 MB).

**Stage 2 verified on two machines (2026-07-29):** connected, mouse + keyboard both worked, session
usable. Trap found and now documented in CLAUDE.md: a brand-new host silently blocks TCP 7789 with
NO firewall prompt — fixed with a `New-NetFirewallRule` one-liner from an Administrator PowerShell.

### Measurement built in + defaults changed (2026-07-29)

- Added a **Run diagnostics** button (and `RemoteDesktop.Host.exe --diagnostics [path] [idleSeconds]`)
  that runs a fixed, repeatable benchmark and writes a text report — numbers now come from the
  program, not from a human reading a moving window. Two conditions: IDLE (real capture, untouched
  screen, paced 15 fps) and PATTERN (program-drawn full-motion 1920x1080, identical on every
  machine), each at quality 70/85/95, recording fps, KB/s, encode ms/frame, capture ms/frame, tiles.
- Defaults changed by eye test: JPEG quality **95** (LAN-only — Stage 3 must add adaptive quality),
  and the viewer starts in **1:1 (Actual size)**, with Fit still available.

**Baseline diagnostics — .223 (DXGI, Release, 2 logical CPUs, 1920×1080):**

| Quality | IDLE fps / KB/s | PATTERN fps / KB/s | encode ms/frame |
|---|---|---|---|
| 70 | 12.9 / 7.2 | 17.0 / 3607 | 36.0 |
| 85 | 12.9 / 8.1 | 16.3 / 3841 | 36.5 |
| 95 | 12.9 / 11.9 | 15.7 / 4799 | 38.3 |

**Investigation A (idle anomaly) — answered.** Stage 2's cursor field adds only ~9 bytes/frame
(~0.1 KB/s), so it is NOT the cause. The diagnostic shows ~0.2 tiles genuinely change per frame even
when "idle" (taskbar clock, blinking carets, etc.), and higher quality makes those few tiles bigger —
so idle KB/s does scale with quality (q95 11.9 > q70 7.2). The earlier claim that "quality can't
affect idle" was wrong and is corrected. Stage 1's 0.2 KB/s vs the later 13–16 KB/s reflects how
static the captured screen happened to be at each reading, not a regression.

**Investigation B (stutter) — pending .222 numbers.** On .223 (2 cores) a fully-changing screen maxes
~16 fps, and encoding is the cost (~36 ms/frame). The VM (GDI, no GPU) is expected to be slower;
confirm by running diagnostics on .222 and comparing the END-TO-END rows.

### Frame rate + end-to-end measurement (2026-07-29)

- **Frame-rate cap raised 15 → 30.** Frames are **dropped, never queued** (comment in
  `HostServer.FrameLoopAsync`): one frame in flight at a time, capture returns the *latest* screen
  (DXGI coalesces, GDI grabs current), and `await SendAsync` back-pressure throttles capture instead
  of building a backlog. A higher cap only adds smoothness when there is spare time — it can never
  become queued input lag.
- **Added an END-TO-END diagnostic phase** (real capture + real encode against a program-animated
  moving screen — the only phase measuring capture and encode together). Baseline **.223 (DXGI,
  Release, 2 cores), full motion:** q70 11.5 fps, q85 12.7, q95 12.9; ~1.4–1.7 MB/s; encode
  ~43–50 ms/frame. So a fully-changing screen tops out ~13 fps on .223 even on DXGI — encode is the
  ceiling. Real support screens change little and hit the 30 fps cap; full motion is the worst case.
  For Investigation B, run diagnostics on .222 (GDI) and compare the END-TO-END rows directly.

### Investigation B closed + defaults confirmed (2026-07-29)

- **.222 does DXGI too** (VMware adapter provides Desktop Duplication), so the .222 run was DXGI, not
  GDI. Added a **"Run diagnostics (force GDI)" button** (and `--diagnostics … gdi`) so the GDI path can
  be measured without a command-line flag — deliberately a button, since Stage 4 runs this on clients.
- **B answered on identical hardware (.223):** DXGI end-to-end ~13 fps vs **GDI ~7 fps** (GDI capture
  ~48–76 ms/frame vs DXGI ~7 ms). GDI roughly halves the frame rate. Full-motion END-TO-END baselines
  now in CLAUDE.md (.222 DXGI ~17, .223 DXGI ~13, .223 GDI ~7).
- **Confirmed:** 30 fps cap is free on idle (0.6–1.0 KB/s, 0 tiles); PATTERN overstates real bytes by
  ~40 % (quote END-TO-END, not PATTERN); quality 95 is ~7 % on real content vs ~41 % on noise, so the
  q95 LAN default stands.
- **GDI idle-CPU burn → Stage 3 gets adaptive frame rate** (drop to ~1–2 fps when static), with the
  justification recorded, not just the feature name.

## Design system (decided 2026-07-29 — full detail + reasoning in CLAUDE.md "Design system")

WinForms **re-examined and kept** once the UI started to matter (analysis in CLAUDE.md): flat design
is WinForms' comfort zone, the video control already works and porting it to WPF is the highest-risk
change for zero visual gain, and it keeps one language for Conor. Central `RemoteDesktop.UI` project
with `Theme.cs` (palette with meanings, 4 type sizes / 2 weights, spacing scale). Semantic palette —
**one colour one meaning; the client's live indicator is AMBER not green** (green = "ignore me", which
defeats the always-visible-session rule). Host window restyled (green/amber state, address = Stage-3
placeholder, red Stop); viewer wears a graphite operator header so the two sides never look alike.

**Reversed 2026-07-29:** Conor withdrew the square-corner rule — **rounded corners are now wanted**
(GraphicsPath + AntiAlias, DPI-scaled radius as a Theme value, DWM corner preference on Win11). This
plus the icon, hover/pressed states, vertical rhythm, window sizes, and code-area-as-hero is the
**pending design pass** (the next session's job).

## Audit before Stage 3 (2026-07-29) — fixed the clear defects, reported the rest

**Fixed:**
- **DXGI ACCESS_LOST no longer crashes the session** (highest-impact): it fired on UAC, lock,
  resolution/monitor change, user switch — exactly when the tool is used. `DxgiScreenCapture` recreates
  the duplication (adopting a new resolution); `ResilientScreenCapture` falls back to GDI if DXGI can't
  recover in ~5 s; `HostServer` resends `ScreenInfo` + updates the injector on a size change; the window
  reads capture method live. Diagnostics after the refactor matched before → no regression.
- **Wire hardening** — `FramePacket.FromBytes` bounds-checks every read and never allocates on an
  attacker-supplied count/length (fails closed). Matters once Stage 3 is on the internet.
- **Stuck modifier** — `InputCapture` tracks held keys/buttons and releases them on focus-loss /
  control-off / disconnect; and the **host releases all modifiers on startup**, so a run that died with
  a key held is cleared by simply starting again.
- **Close-race** — viewer marshalling goes through `SafeBeginInvoke`; **timers disposed** on both windows.
- **EncoderParameters checked, NOT a race, deliberately left:** `Encode` snapshots the `volatile`
  reference and the old object is never disposed, so an in-flight encode always holds a valid object.
  Disposing it to "fix" the tiny leak is exactly what WOULD create the race — so do not.

**Deferred (reported, judgement calls):** host hard-kill still can't self-clean (mitigated by the
startup release); DXGI-vs-GDI duplication of a header constant (left); handshake timeout + 16 MB cap
folded into the Stage 3 line items.

## Capture-health self-reporting (2026-07-29)

`CaptureHealthLog`: two counters in the host window — **Interruptions survived** (lost and came back)
and **Capture failures** (fell back to GDI; turns red if non-zero) — plus a timestamped log file on the
Desktop and an "Open capture log" button. Counters persist across start/stop. This is so Conor (or a
client on the phone at Stage 4) verifies resilience by reading two numbers, not by watching indicators
through a lock and a UAC prompt.

## Protocol tests (2026-07-29)

`RemoteDesktop.Tests` (xUnit) — **protocol only**: round-trip encode/decode, truncated message,
oversized length prefix, malformed handshake, `FromBytes` bounds. 16/16 pass. Run with `dotnet test`
after every stage (now in CLAUDE.md). Do not grow into a general suite.

## Decisions for Stage 3 (recorded in CLAUDE.md)

- **Consent dialog + session log move to Stage 3** (from Stage 4), so nothing is internet-reachable
  without a human deciding; the log records what consent decided, so they ship together.
- **Adaptive quality AND frame rate** at Stage 3 (two justifications: home-upload bandwidth; GDI idle
  CPU burn).
- **Relay: location first.** Conor + clients are in **Kyiv** → datacentre nearest Kyiv, chosen before
  price. **Decided:** use a bought `.com` (not DuckDNS). **NOT decided:** the provider (an earlier
  DigitalOcean lean was **retracted**, deferred until the region is recommended — location decides it),
  the specific DC, the actual `.com` purchase, and whether 1 vCPU/1 GB holds ~3 concurrent sessions.
- **Client download hosted** at `https://<domain>/download` (one static file on the relay host).
- **Package size ~65 MB is the floor** (trimming blocked: `NETSDK1175`); deliver by link, not attachment.

## Where we are / next session

Stage 2 complete and audited; capture resilience + health in place; tests green. **Next: the design
pass** (rounded corners + icon + hover/pressed + rhythm + window sizes + code-area hero), then show
both windows, then answer the relay-location question (nearest DC + latency measurement), then
**Stage 3** (relay, 6-digit code, outbound, consent dialog + session log, adaptive quality/frame rate).

**Open — not yet decided (full list in CLAUDE.md "Open — not yet decided"):** relay provider, the
datacentre, the `.com` purchase, whether 1 vCPU is enough, and Conor's own ACCESS_LOST verification
(not yet run). **Resolved:** `.223` has **2** logical processors — confirmed by OS query, not a guess
(Xeon Gold 6262 @ 1.9 GHz, two single-core sockets), so "encoding is the ceiling" stands; and that CPU
signature means `.223` is itself likely virtualised (the "physical PC" label is probably loose).

## Design pass — HOST window half (2026-07-30, AWAITING Conor's verdict; Viewer deliberately untouched)

Conor's constraints: host window first then stop (one window redone if the direction is wrong, not
two); icon = a geometric mark readable at 16 px; every colour/size through Theme. Built, in
`RemoteDesktop.UI`: `Theme` gains CornerRadius 8 (DPI-scaled via `ScaledRadius`), sanctioned
hover/pressed shades per accent, disabled colours, `ButtonColors()`, `RoundedPath()`, deliberate
host window sizes; `RoundedButton` (GraphicsPath + AntiAlias, never Control.Region; hover /
pressed / disabled / focus states); `CardPanel` (rounded white card, hairline border);
`WindowCorners` (DWMWA_WINDOW_CORNER_PREFERENCE — rounds the frame on Win11, cleanly refused on
Win10). Icon: two overlapping rounded squares (grey behind, green in front, transparent gap),
generated by `assets\make-icon.ps1` → `assets\RemoteDesktop.ico` (16/24/32/48 BMP + 256 PNG),
embedded in the Host exe (`ApplicationIcon` + embedded resource for the title bar). Host
`MainForm` relaid: hero card for the address, session card for details, wrapping button row —
rhythm on the spacing scale. Verified: Release build 0 warnings, tests 16/16, screenshot sent.
Literal audit outside Theme.cs: Host+UI = 0; Viewer has 5 (its pass is pending); diagnostics has 4
(benchmark pattern colours + fixed 1920×1080 pattern size — measurement constants, deliberately
not Theme). NOT committed — awaiting Conor's approval of the direction. Note: rebuilding restyles
the Viewer's existing buttons via the shared Theme automatically, but its layout was not touched.

## FlashDesk rename + design brief (2026-07-30)

Conor named the product **FlashDesk** (domain bought — exact string not yet recorded; ask at
Stage 3) and issued a design brief: simple/technical split of the host window (client sees the
code, one status line, one button, zero numbers — rationale now in CLAUDE.md "TWO VIEWS"), a
non-generic logo connected to the name, brand colour outside the semantic set (rule in CLAUDE.md),
and craft fixes (code much larger, one radius, green used once, window sized to content, product
phrasing). Renamed everything visible: exes `FlashDesk.exe` / `FlashDeskViewer.exe`
(`<AssemblyName>`; Task Manager + CLAUDE.md kill-switch names updated), window titles, capture log
`FlashDesk-capture-log.txt`, diagnostics report + header, `assets\FlashDesk.ico`. Deliberately NOT
renamed (recorded in CLAUDE.md "Naming"): namespaces, project/folder names, solution, repo.
Build clean (0 warnings), tests 16/16, exe names verified in bin. Three logo options rendered on
test sheets (A: violet plate + negative bolt; B: naked pointing bolt; C: dark plate + italic F
with violet cut). **Round 2 (same day): A and C shortlisted.** A got a custom bolt (flat top,
vertical spine, needle tip — not the stock glyph) and a real colour test: the 16 px mark composited
into the actual taskbar row (real extracted Chrome/Brave/VS Code/Explorer/PowerShell icons;
AnyDesk drawn stand-in) on light/dark/greyscale, four plate candidates — K1 deep violet-indigo
#473D8C (recommended), K2 ink, K3 plum, K4 slate. C got a bold upright F with an opened counter
and the violet as a clipped cut THROUGH the letter, plus a fourth variant where the F's middle arm
IS the bolt. **Round 3 (same day), driven by Conor's five criticisms:** A's identity moved from edge detail
into MASSING (heavy chevron over a hairline strike — proportion survives 16 px, edges do not);
C-refined dropped (a diagonal through a letter reads "crossed out" — conceded); the C-bolt
one-colour test run as demanded (all-white and all-violet) — the glyph collapses without the
colour split, confirming the two-object diagnosis, so the whole C/F family is closed (F's three
arms + counter are structurally hostile at 16 px — agreed, not an execution problem); B redrawn
bold and naked on the real-icon strip — most findable on the LIGHT taskbar, nearly invisible on
the DARK one and in greyscale, so it loses on evidence, not on taste; and the LOCKUP designed
(mark + "FlashDesk" in Segoe UI Semibold, gap = 0.32 × mark, optical midline, theme text colours,
header + small, light + dark). Recommendation was **A revised on K1 #473D8C** — and then **Conor reframed the brief (round 4,
final): depict the PRODUCT, not the word; no plate (rounded-square plates read as phone apps on a
Windows taskbar); the mark's own OUTLINE must carry it.** New gate adopted: the silhouette test —
solid black, 16 px, no internal detail; if the outline alone does not identify it, it fails (this
gate would have killed every earlier round). Three directions built: D1 screen with a foreign
cursor entering it (pass), D2 two screens fused into one outline (borderline — outline reads
"screen with a corner block"), D3 a bolt striking through the screen's top edge (pass, strongest
silhouette). Colour re-derived: K1-as-field does not survive plateless marks on dark taskbars
(round-3 evidence), so ink = structure, white face = dark-bg pop, **K1 = accent** (cursor / second
screen / lit face). Recommendation was **D1** — **rejected in round 5 (the hard-capped final round): the monitor is
THIS category's cliche** (Windows RDC, TeamViewer, VNC all use it), D1 was two objects again
(monitor + cursor read as a scene), the stand/hollow-outline construction is fragile at 16 px, and
D3's edge-bite reads as a BROKEN display — disqualifying subtext for a support tool. New hard
constraints: ONE mass, ONE cut, no stand, no outline, no plate, two colours max; new gate run
FIRST — the sibling test (16 px directly beside the real RDC icon from mstsc.exe and a TeamViewer
stand-in). Built: **E1 — the pointer IS the mark** (fat cursor mass in K1, one white cut implying
a lit screen; passes sibling + silhouette; honest caveat: the white cut effectively vanishes at
16 px, where the mark is carried by mass and colour alone) and **E2 — solid screen face with a
cursor bitten from its corner** (partially FAILS the sibling test — its rounded landscape slab is
TeamViewer-pill shape family — and the bite fragments at 16 px). E3 skipped, reasoned: under
one-mass-one-cut the two poles are cursor-mass/screen-cut and screen-mass/cursor-cut; any third
idea is a blend the constraints exclude. **Recommendation: E1, which beats fallback A/K1 on the
evidence** (A fails the silhouette test — its outline is just a square — and its plate reads
phone-app on a Windows taskbar). **Awaiting Conor's pick (E1 / E2 / A-K1); the window build is
next regardless.** Still no commit.

## Round 6 — the icon becomes an instrument; the HOST WINDOW IS BUILT (2026-07-30)

Conor ended the icon hunt with the correct diagnosis: originality does not exist at 16 px; six
rounds proved every depiction lands on a cliché or a competitor. Decisions now in CLAUDE.md: the
base mark is a plain K1 disc; the icon's real job is the safety system — **IDLE quiet disc, LIVE
amber-badged disc (badge breaks the silhouette too) while a viewer is connected**, extending hard
rule 2 to the taskbar where it survives minimising. OS mechanics checked and recorded: Form.Icon
swap is instant and uncached; exe-file icon stays idle (a closed app cannot be live); the tray is
hidden by default so it is never the safety surface; overlay badges vanish with Win10 small
taskbar buttons so they are never the mechanism. Two marks were rendered (disc/tile); tile sits in
PowerShell's dark-tile family at 16 px, so **disc chosen** (Conor delegated the tie-break).

**Host window rebuilt** (Release build 0 warnings, tests 16/16): simple/technical split — simple =
lockup + one status line + the address as hero (`Theme.Hero` 30 pt semibold, the one sanctioned
size outside the four-step scale) + Copy + one action + quiet "Technical details" link, not one
number; technical expands below with capture method, fps, KB/s, health counters, all addresses,
quality, diagnostics, log. Craft fixes: radius 8→12 (ONE radius), green once (monitoring line is
now neutral), window sized to content per view (500×350 / 500×660 client), title "FlashDesk" with
lockup inside (no duplicate phrasing), placeholder sentence now reads as product copy. **LIVE
state verified end-to-end**: a real loopback viewer handshake (magic RDK1) flipped the window to
amber and the taskbar icon to the badged variant, screenshots taken of simple / technical / live
views and the taskbar itself; disconnect returned it to idle. Literal audit outside Theme.cs:
Host + UI = 0; total 9 (5 in the not-yet-passed Viewer, 4 deliberate benchmark constants in
diagnostics). **Trap resurfaced:** renaming the exe made Windows Firewall treat FlashDesk.exe as a
new program — on `.223` it PROMPTED (unlike the silent Server 2022 block); Conor must click Allow
(or add the documented rule); pressing Cancel would create silent BLOCK rules. **Next: the Viewer
pass, after Conor's verdict on the window. Nothing committed yet.**

## FlashDesk ID system — researched spec recorded (2026-07-30)

Conor delivered a researched Stage 3 spec replacing the ephemeral 6-digit code with a permanent
per-installation **9-digit ID (three groups of three)** + a private secret, AnyDesk-style, with an
**allow-list** as the deliberate advantage over AnyDesk (unlisted operator IDs refused before the
consent dialog — targets the scam-call pattern). Full spec + the governing principle ("the ID is
an ADDRESS, not a credential" — what protects the machine is allow-list → consent → hardened
unattended password) now in CLAUDE.md under Stage 3. Assessment given: spec confirmed except one
real correction — **a clone carries the same secret, so wrong-secret refusal cannot catch clones;
the relay must also refuse a second live registration with liveness expiry** — plus honest
allow-list limits recorded (guards only the FlashDesk door; needs secret-verified operator
identity now and public-key pinning at Stage 5; the add-operator confirmation must ignore
injected input). Refinements added: first digit 1–9, salted-hash secret storage server-side,
capped collision retries. Storage decided: `%APPDATA%\FlashDesk\`, no admin rights, survives
updates/re-downloads, per-user caveat noted, secret DPAPI-protected. **Not building yet — Stage 3
work; the window verdict and the Viewer come first.**

## Window approved; live-state round + ID-spec additions (2026-07-30)

Conor approved the direction (disc + simple view) and demanded one real fix: idle and live looked
the same INSIDE the window. Built: a full-width status band — invisible when idle (green dot +
word, the only green), SOLID AMBER with dark icon + words when live — readable across a room, no
animation, never colour-alone. Craft fixes: the one action changes weight with state (neutral
idle / red live / blue "Start sharing" stopped); Copy demoted to a quiet chip so the Hero-36
address wins the glance; actions anchored to the window bottom in BOTH views; TextSecondary
nudged hue-neutral (#5A626C → #606266 — ClearType fringing made it read greenish at Small sizes;
pixel-sampled to confirm the value itself was never green). **Fix 2 delivered differently than
asked, by OS constraint (verified by test): `ShowIcon=false` makes the taskbar fall back to the
exe's static icon, killing the amber LIVE badge — the title-bar icon is the taskbar's feed, so it
must stay. The in-window lockup was removed instead: brand appears once, in the title bar.**
Verified same turn: build 0 warnings, tests 16/16, loopback-handshake live test — amber band +
badged taskbar icon both captured; screenshots (idle|live side by side, technical, taskbar zoom)
sent. **Recorded into the ID spec (not built): Stage-6 service must ADOPT the per-user ID
(migration designed at Stage 3, %ProgramData% move); local clone detection via a machine
fingerprint (separate value — ID stays random); heartbeat 20 s / expiry 60 s / retry 5→30 s with
a neutral "reconnecting" state during the gap.** Awaiting Conor's glance-test verdict → then the
Viewer, then Stage 3. Still no commit.

**Clone-fingerprint correction (same day, Conor's catch):** MachineGuid lives on the disk, so a
clone copies it and the local check would never fire — replaced by SMBIOS UUID + MAC as
candidates, with the FINAL pick made by the Stage 3 clone A/B test (values read before/after on
`.222` and its clone). Evidence from `.223` itself: VMware's auto-MAC tail IS the SMBIOS UUID
tail, so the two move together when a copy is acknowledged; "I moved it" changes neither — the
relay's second-live-registration refusal stays the backstop. Added the false-alarm guard (ask the
relay "is this ID live elsewhere?" before regenerating — NIC swaps and docking must never cost a
number) and the two-account adoption rule (installer's account, then most-recent; retired IDs
announce themselves). Process note, recorded deliberately: the previous session message CLAIMED
these CLAUDE.md corrections before they were actually written — the same-turn-verification rule
caught it one turn late; they are in the file now.

**Pre-clone fingerprint baseline for the Stage 3 clone A/B test (read by Conor on `.222`,
2026-07-30):** SMBIOS UUID `D4A34D56-1C85-B30B-AE72-8B8DC962FBB3` · MachineGuid
`a38d7200-3a39-4484-bb24-404637f9f738` · MAC (Ethernet0) `00-0C-29-62-FB-B3`. The VMware
UUID→MAC derivation holds on `.222` too (UUID tail `…C962FBB3` = MAC `62-FB-B3`), matching
`.223`. At the clone test: clone `.222` answering "I copied it", run the same one-liner on both,
and expect UUID+MAC to differ on the clone while MachineGuid stays identical — the fingerprint is
then chosen from what actually differed.

## Icon round 7 — Conor's new direction, one round only (2026-07-30, AWAITING his pick)

Conor found a shape he responds to (bold two-stroke angular form, vivid colour on a near-black
tile) and reopened the closed icon decision for exactly one round. Built flat in code: three
proportions with the DESCENDING stroke as the long one (a checkmark is short-down/long-up — the
check reading is killed by proportion, not detail). Verdict on the sheets: P1 still reads
check-ish, P2 reads as a letter V, **P3 (long steep descent, short ascent) stops reading as a
check**; honest caveat — at 16 px any two-stroke angular form regains a little check-likeness.
Proposed system change (NOT yet applied anywhere): idle status becomes NEUTRAL GREY, green leaves
the semantic set and becomes the brand colour (vivid `#2BD16B` on tile `#17191E`); amber keeps
LIVE; the live icon turns the whole mark amber + badge bump (strongest live signal so far —
colour AND silhouette). Green inventory for the all-at-once change: CLAUDE.md palette row, the
amber-never-green paragraph, the brand-colour rule, READY state spec, the TWO-VIEWS band note
(+ the untouched colourblind line), `Theme.Green` + its ONE usage (MainForm idle dot), and
`make-icon.ps1`. Evidence strip vs the chosen purple disc: **the green chevron wins on
findability in all three rows** (light/dark/greyscale — the glowing stroke on the dark tile is
the highest-contrast object in the row); no resemblance to RDC or TeamViewer. Sheets sent.
**Waiting on TWO verdicts now: this icon pick AND the window glance-test.** Still no commit.

## Green system change, COMMIT, and the Viewer pass (2026-07-30)

Conor approved both open verdicts (P3 green mark; the live state reads at a glance). The whole
colour system changed in one move: green left the semantic set (idle = neutral grey dot ● + word;
Stopped stays ■ — glyphs keep them apart without colour), brand green `#2BD16B` on tile `#17191E`
lives only in the mark, both icons regenerated as the P3 chevron (idle green / live amber + badge
top-right), all six load-bearing green spots in CLAUDE.md rewritten, `Theme.Green` removed and
`BrandGreen`/`BrandTile` added. Verified end-to-end with a loopback handshake: idle window (grey
status, green mark in title), live window (amber band, amber-badged icon), live taskbar button
captured. **Committed as `419e7ec`** — the full design pass, 15 files (rename, design system,
two-view window, icon instrument, ID-system spec in CLAUDE.md).

**Viewer pass (approved by Conor and committed 2026-07-30; design declared CLOSED — recorded in
CLAUDE.md at the top of the design system):** FlashDesk mark embedded in
`FlashDeskViewer.exe` and its title bar (static — the safety swap belongs to the client side, not
the operator's); the 5 remaining literals moved into Theme (`ViewerWindowSize` /
`ViewerWindowMinimum`, `MediumFieldWidth` for the address box, `CanvasBackdrop` for the black
letterbox in both MainForm and ScreenCanvas); layout deliberately untouched — the graphite
operator header stays. Build 0 warnings, tests 16/16 (a transient build failure was just the
running FlashDesk locking its DLLs — closed and rebuilt clean). **Literal audit final: ZERO
colour/size literals outside Theme.cs across Host + UI + Viewer**; only the 4 deliberate
diagnostics benchmark constants remain. Next: Conor's viewer verdict → commit → Stage 3 (relay,
FlashDesk ID system, consent + allow-list, adaptive quality/frame rate, deployment writeup).

## Stage 3 planning facts (measured 2026-07-30 from `.223` — keep, they decide the relay)

- **SSH works from `.223`**: OpenSSH_for_Windows 9.5p1 at `C:\Windows\System32\OpenSSH\ssh.exe`
  (no `~/.ssh` yet — created at key generation). Conor's server workflow (he clicks the provider
  UI, hands over the address, the session does everything over SSH) is feasible as specified.
- **Latency from `.223` (8 ICMP pings each, avg/min/max):** Vultr Warsaw **14.0 / 14 / 14 ms** ·
  Vultr Frankfurt 27 / 27 / 27 · Hetzner Falkenstein 27 / 27 / 27 · Hetzner Nuremberg 29 ·
  Hetzner Helsinki 44.6 · DigitalOcean FRA/AMS and OVH Warsaw: no ICMP reply (endpoints block
  ping — but no Frankfurt/Amsterdam location can beat Warsaw's geography anyway).
- **Conclusion offered to Conor: Vultr Warsaw**, Cloud Compute 1 vCPU / 1 GB (~$5–6/mo, hourly
  billed, 1 TB traffic). Sizing question answered: the relay is traffic-bound, not CPU-bound —
  1 vCPU/1 GB genuinely holds ~3 concurrent sessions (relay CPU is trivial byte-piping; RAM
  ~200 MB; 1 TB/month ≈ hundreds of session-hours at 0.2–1 MB/s). TLS is free (Let's Encrypt via
  Caddy); the domain is already bought; no other purchases in Phase 1.
- Stage 3 plan sent for approval: his 6 testable steps, SSH-key workflow (public key pasted at
  server creation — no passwords in chat), DNS = one A record added by Conor with literal click
  instructions once the IP exists, third-machine test minimum = any Windows laptop on a phone
  hotspot. **No Stage 3 code until Conor approves the plan.**

## The download site — flashdesk.org (built 2026-07-30, AWAITING Conor's approval + details)

Domain confirmed and recorded everywhere: **flashdesk.org**. Full site spec (Conor's brief) now in
CLAUDE.md "The download site": one narrow job (phone call → get file → run → read number),
trust over looks, the scam warning verbatim and prominent, SmartScreen handled BEFORE it happens,
phone-first, palette locked to Theme (BrandTile bg / BrandGreen as the ONE clickable green /
OperatorHeaderText text — lock comments in both Theme.cs and the HTML), stable /download URL,
site-cannot-break-relay separation with a /health check URL, honest holding page until the
9-digit build is real (public page never lies; private random URL for Conor's own testing), and
the installer answer: portable file stays THE download, in-app "Install on this computer" later,
install ADOPTS the existing ID (same adopt-never-regenerate rule as the Stage-6 migration — the
install elevation moment IS the migration moment; no collision). Built as ONE static file —
`site/index.html`, no JS, no fonts, no tracking — with visible {{PLACEHOLDER}}s for Conor's name,
one-line who/where, phone, email. Verified at a true 375 px viewport (headless-Chrome-on-Windows
has a ~500 px minimum window that CROPS screenshots — caught and worked around with an iframe
harness; the page itself was fine) and at 1280 px. NOT committed — awaiting Conor's approval,
his contact details, and his call on page language (currently English; clients may need
Ukrainian). Deploys alongside the relay at Stage 3 step 1.

## Site approved + committed; Stage 3 step 1 begins (2026-07-30)

Conor approved the page; details filled (name "Michael", line "Billionaire Club CEO", contact
a personal email address, language English — one language per page, a second language would be a
separate URL later). The scam warning moved ABOVE the fold, directly under the download button,
with more presence (his call: it outranks the SmartScreen box; kept calm, no red). Committed as
`8af8d98`. **Flag raised to Conor before deploy (charter §8 duty): the "Billionaire Club CEO"
line and the Michael-vs-real-name mismatch work against the page's own trust spec — the
deploy is gated behind the holding page anyway, and the line is a 30-second swap when he supplies
a real one.** Latency RE-measured before purchase, 20 pings: Vultr Warsaw 14.0 avg (14/14, 20/20
replies, zero jitter) vs Vultr Frankfurt 27.0 and Hetzner Falkenstein 27.0 — Warsaw stands.
**Step 1 started:** ed25519 keypair generated at `C:\Users\PC\.ssh\flashdesk_relay` (private key
never leaves `.223`); public key + literal Vultr click-path handed to Conor (Warsaw, Ubuntu 24.04
LTS, $5–6 shared-CPU plan, paste the public key at deploy, hostname flashdesk-relay); waiting on
the server IP, then the whole server setup runs over SSH from `.223`.

## Conor's hosting assessed from the cPanel screenshot (2026-07-30)

**It is shared cPanel hosting** (Jupiter theme; user `flas01151844`; home `/home/flas01151844`;
shared IP `64.187.97.203`; **Primary Domain = flashdesk.org, already attached** — so the site can
go live immediately; fresh account, 149/200,000 inodes). Verdict on the four questions:
static site YES (File Manager → public_html); 65 MB download file YES (per-file no problem; the
plan's disk/bandwidth quota not visible in the panel crop — ~100 client downloads ≈ 6.5 GB/month,
fine for typical plans, check the provider's plan page if downloads grow); **long-lived custom
process NO** (shared hosting runs short-lived website scripts only, no root, no service manager —
the relay CANNOT live here); root NO, jailed SSH unknown (below the crop) and not needed — File
Manager covers the site. **Split architecture confirmed: flashdesk.org = cPanel (site +
/download), relay.flashdesk.org = Warsaw VPS (relay only).** DNS for the `relay` subdomain is
expected in cPanel → Domains → Zone Editor (A record `relay` → VPS IP; propagation minutes to
~1 h, TTL up to 4 h worst). Upload package prepared for Conor at
`C:\Users\PC\Desktop\flashdesk-upload\index.html` (the holding page, renamed for serving at the
root). Waiting: his upload → check https://flashdesk.org; his real who-line; then the Vultr
deploy resumes (instructions + public key already in his hands).

## Namecheap VPS measured — WRONG CONTINENT (2026-07-30, blocking decision)

Conor bought the relay VPS at Namecheap (card worked there) instead of Vultr: IP
`159.198.70.186`, Ubuntu 24.04, datacentre unknown to him. **Measured before any setup work:
avg 167.4 ms (min 167 / max 169, 20/20 replies) vs Vultr Warsaw 14.0 ms and Frankfurt 27.0 ms
the same hour.** Traceroute proves WHY, so this is not guesswork: hop 9 is 45 ms (still Europe),
hop 10 jumps to 115 ms — a transatlantic leg — so the machine is in North America. That is
Conor's own "over 100 ms — tell me now, not later" bucket: every mouse move would cross the
Atlantic twice (~334 ms round trip added to each interaction). **Recommendation given: do NOT
build on it; rebuild/relocate it to a European (ideally Warsaw/Amsterdam/Frankfurt) location, or
keep it and buy the small Warsaw VPS instead — his money, his call.** No server setup was
performed.

**Website check — NOT broken:** `flashdesk.org` → `64.187.97.203` (the cPanel shared IP, correct);
`www` → CNAME → same; `relay.flashdesk.org` does not exist yet (expected). Important DNS fact
found: the nameservers are `ns1/ns2.hostsilo.com`, i.e. **DNS is managed at the hosting side, so
the future `relay` A record goes in cPanel → Domains → Zone Editor, NOT in Namecheap's panel.**

**Root password: NOT used, and will not be.** Claude does not authenticate with passwords —
the key install and password-login shutdown must be run by Conor (or the key pasted at rebuild
time, which is the cleaner path). The password he pasted in chat must be treated as exposed and
rotated regardless of which server survives.

## Relay provider search — PAYMENT-FIRST (2026-08-03, awaiting Conor's pick)

Two failed attempts reframed the constraint: **payment is the blocker, not price or latency**
(Vultr rejected his card; Namecheap took it but has NO European datacentre — support first said
Amsterdam, then corrected itself; refund requested). Searched payment-first, verifying methods on
providers' OWN pages, and measured latency from `.223` the same way as before.

**Measured (6 pings each, plus traceroutes to prove the short ones are real and not a CDN edge):**
Хостинг Україна `185.39.224.104` **<1 ms** (6 hops, via UA-IX Kyiv `195.69.84.33` — genuine Kyiv
DC) · Lanet `194.60.69.123` **<1 ms** (6 hops, Kyiv) · Hetzner Falkenstein 27 ms · netcup 32 ms ·
Tucha 29 ms · nic.ua 36 ms (their site is on DigitalOcean, not their DC). DeltaHost/HyperHost/
GMhost/Contabo/Time4VPS all sit behind Cloudflare, so their 0 ms readings measure a Kyiv
Cloudflare edge, NOT their servers — recorded so nobody mistakes those for datacentre latency.

**A Kyiv relay is better than the original Warsaw plan**: ~1 ms instead of 14 ms, and BOTH legs of
every session stay in the city where Conor and his clients are. Honest counterweight given to
him: a Kyiv DC carries wartime power/infrastructure risk (generators are standard but blackouts
happen); mitigating facts — a citywide outage takes his clients' PCs down too, and relocating the
relay later is one A record plus a rebuild.

**Ranked recommendation given:** (1) **Хостинг Україна** ukraine.com.ua — Kyiv DC, ₴315/mo
(~$7.5) 2 GB VPS, pays via Privat24/LiqPay/Visa/MC/Google Pay/terminals (NO PayPal), SSH + VNC +
**browser WebSSH console** (which lets Conor install the SSH key without a terminal); (2)
**DeltaHost** — Kyiv DC available and the widest payment fallbacks anywhere (PayPal, Privat24,
LiqPay, WayForPay, UAPAY, Stripe, BitPay crypto, bank transfer) but Linux VPS starts at $15/mo;
(3) **Hetzner** — 27 ms Falkenstein, cheapest (~€4.5), most established, PayPal accepted but
MANUAL-only, and new accounts can hit identity-verification friction. Note for later: Hetzner
docs state alternative card-linked methods (Apple Pay etc.) are not accepted.

## RELAY SERVER CHOSEN AND MEASURED — DeltaHost Kyiv (2026-08-03)

Conor bought the relay at **DeltaHost, Kyiv**: `139.28.36.247`, Ubuntu 24.04 Cloudinit, 4 GB RAM,
50 GB NVMe, 10 TB traffic, $15/month, root over SSH port 22. **Measured BEFORE any setup work
(the Namecheap lesson): avg 0.1 ms (min 0 / max 1, 20/20 replies).** Traceroute: 5 hops, all
domestic (via a Kyiv IXP at `185.1.62.193`), no international transit — the VPS really is in
Kyiv, unlike the Namecheap machine whose website-based assumption was wrong. (Hop 4 reports
19 ms; that is an intermediate router de-prioritising ICMP, not a path problem — the destination
itself answers in <1 ms.)

**Final latency table:** DeltaHost Kyiv **0.1 ms** · Vultr Warsaw 14 ms · Vultr Frankfurt 27 ms ·
Hetzner Falkenstein 27 ms · Namecheap (US) 167 ms. The Kyiv relay beats the original Warsaw plan
by ~14 ms on every leg; both ends of a typical session now stay in the same city.

Open question "Relay provider/location/sizing" is now CLOSED: DeltaHost, Kyiv, 4 GB (well above
the 1 vCPU/1 GB minimum that was in doubt — sizing is no longer a risk at ~3 concurrent sessions).

**Access rule reaffirmed:** Claude never authenticates with a password. Conor installs the
ed25519 public key himself via the provider's VNC/KVM console with a single pasted line; after
that the session works over the key from `.223` (private key at `C:\Users\PC\.ssh\flashdesk_relay`,
never leaves the machine). The provider-emailed root password is to be rotated by Conor himself
with `passwd` in the console, so no password ever appears in the chat.

## Stage 3 step 1 DONE — server secured, updated, firewalled (2026-08-03)

Conor installed the public key via the KVM console and rotated the root password himself (no
password ever entered the chat). Getting in was painful: **the provider's generated password was
rejected by both the console and SSH until support reset it** — recorded in CLAUDE.md as a trap,
since key access is the only reliable route at DeltaHost.

Done over SSH from `.223` (key `C:\Users\PC\.ssh\flashdesk_relay`):
- **Key-only SSH.** Wrote `/etc/ssh/sshd_config.d/10-flashdesk.conf` (PasswordAuthentication no,
  KbdInteractive no, PermitRootLogin prohibit-password, PubkeyAuthentication yes). **Gotcha
  caught and fixed:** a `99-` file did nothing because sshd reads `sshd_config.d/` in NAME order
  and the FIRST value wins — Ubuntu's `60-cloudimg-settings.conf` was overriding it; that line is
  now commented out and our policy renamed to `10-`. `sshd -t` validated before every restart.
  **Proved by live test, not by reading config:** password attempt → "Permission denied
  (publickey)"; key attempt → logs in as root.
- **Updates:** 0 packages were pending (fresh image); `unattended-upgrades` installed and active
  so security patches land automatically. No reboot required.
- **Firewall (ufw):** default deny incoming / allow outgoing; 22, 80, 443 open (22 allowed BEFORE
  enabling, to avoid the classic self-lockout). **Verified from `.223`:** 22 reachable, 3306
  refused, 80/443 accepted by the firewall but nothing listening yet (as expected).
- Server facts: Ubuntu 24.04.4 LTS, 2 cores, 3.9 GB RAM, 42 GB free.

Next: step 2 Caddy, step 3 relay skeleton, step 4 TLS on relay.flashdesk.org (needs the cPanel
Zone Editor A record from Conor first), step 5 /health in a browser.

## Stage 3 steps 2-3 DONE — Caddy, TLS, and the relay skeleton are live (2026-08-03)

DNS: Conor added `relay` A → `139.28.36.247` in cPanel Zone Editor (flashdesk.org untouched).
Console fallback TESTED by him and works (`root@ubuntu:~#`, exited cleanly) — so losing `.223`
does not mean losing the server.

- **Caddy v2.11.4** installed from the official repo. Two real failures caught and fixed rather
  than worked around: (1) a `log { output file }` block made the service refuse to start — the
  packaged systemd unit sandboxes Caddy out of `/var/log`, so logging goes to the journal;
  (2) TLS could not be issued until DNS existed (`NXDOMAIN`), which is expected and self-healing.
- **TLS live:** Let's Encrypt certificate obtained for relay.flashdesk.org (valid to 1 Nov 2026,
  auto-renewing), HTTPS 200, `http://` → 308 → `https://`.
- **Relay skeleton** — new project `src/RemoteDesktop.Relay` (net8.0 web, references
  `RemoteDesktop.Shared`, which is the standing proof that Shared stayed platform-neutral —
  architecture rule 4). Listens on `127.0.0.1:5000` only; Caddy terminates TLS and forwards
  `/health`. Runs as unprivileged user `flashdesk` under systemd with a hardening sandbox.
  `/health` answers plain text (deliberately, so a non-technical person can read it): status,
  version, protocol version, start time, uptime.
- **Proved, not assumed:** `/health` answered over the internet from `.223`; then the process was
  `kill -9`ed and came back by itself in under 8 s (NRestarts 1) and answered again. Reboot
  recovery is enabled (`WantedBy=multi-user.target`) but NOT yet exercised — offered to Conor as
  a deliberate test while nothing depends on the server.
- ASP.NET Core runtime 8.0.29 installed from **Ubuntu's own repo**, so security patches arrive
  via unattended-upgrades; no third-party package source.
- Repo now carries `server/RUNBOOK.md` (rebuild in deployment order, with both traps recorded),
  plus the live `server/Caddyfile` and `server/flashdesk-relay.service` copies. Tests 16/16.

**Reboot test done (2026-08-03, Conor's call to do it while nothing depends on the server).
Measured downtime: 16 seconds.** That is the real cost of a reboot to a client mid-session — it
justifies the neutral "reconnecting…" state and the 60 s heartbeat expiry already specified.
Caught BEFORE rebooting: `systemctl is-enabled ssh.service` reports *disabled* on Ubuntu 24.04
because SSH is socket-activated — `ssh.socket` is the enabled unit; had that been assumed rather
than checked, the server could have come back without SSH. All four subsystems verified after the
reboot, each from outside rather than by reading status on the box: /health OK · TLS valid (cert
verify 0) · firewall active with identical rules (22/443 reachable, 3306 blocked) · password
login still refused (`Permission denied (publickey)`). Conor also confirmed /health from `.223`
AND from his phone on mobile data — so it works from a network that is not his own.

Next: step 4 was folded into step 3 (TLS landed with Caddy). Remaining Stage 3: the WebSocket
pairing endpoint + FlashDesk ID system, then relay-path test .223↔.222, then the cross-network
test, then consent/allow-list/session log, then adaptive quality.

## Stage 3 step 3a DONE — the window shows a 9-digit FlashDesk number (2026-08-03)

The ID system is live end to end. New code: `Shared/Identity/FlashDeskId.cs` (crypto-RNG
generation, first digit 1–9, validation, `418 205 793` formatting) and `RegistrationContract.cs`
(request/response defined once, so Windows client and Linux relay cannot drift);
`Relay/IdRegistry.cs` (persisted to `/var/lib/flashdesk-relay/registry.json`, stores **salt +
SHA-256 hash, never the secret**, fixed-time comparison); `Host/Identity/IdentityStore.cs`
(`%APPDATA%\FlashDesk\identity.json`, secret DPAPI-protected, write-then-move so a crash cannot
truncate it) and `Host/Identity/RelayRegistration.cs` (claims the ID, retries a collision up to 5
times, then a clear error — never a loop).

**Conor's four proofs, each tested rather than argued:**
1. *Same number returns.* Closed and reopened FlashDesk: `373883745` both times, and the relay
   still shows `1 registered` — a restart re-binds, it does not consume a second number.
2. *Two machines, two numbers.* Self-contained `FlashDesk.exe` (64.9 MB) published to
   `C:\Users\PC\Desktop\flashdesk-test\` for Conor to run on `.222`. Pending his run.
3. *Never shows an unaccepted number.* The window renders the number only in
   `IdentityState.Ready`; Copy is disabled otherwise. Verified live by stopping the relay.
4. *Relay down.* Screenshot captured: hero reads "No number yet" with "FlashDesk cannot reach the
   internet right now. Check this computer's connection, then close FlashDesk and open it again."

Relay API verified by curl: new ID accepted · same ID + same secret accepted (restart) · same ID +
different secret refused 409 · malformed ID refused 409. Registry on disk confirmed to hold only
salt+hash.

Two real defects found and fixed in this step, both recorded in the runbook: Caddy 404s any path
it has not been told to forward (`/api/*` was missing), and a 36 pt Hero string like "No number
yet" pushed Copy off the card — non-number states now use Display size, the read-aloud line hides
when there is no number, and the simple view is 40 px taller so a two-line explanation cannot be
clipped. Also caught: this project has its own `RemoteDesktop.Host.Encoding` namespace which
shadows `System.Text.Encoding` — aliased rather than renamed.

**Still IP-based for connecting.** Step 3a registers and displays the number; step 3b adds the
WebSocket pairing so the number actually carries a session. Until then the LAN path (typed IP,
now shown only in the technical view) is what works.

# ══════ SESSION HANDOVER 2026-08-03 — START HERE ══════

## Where the product actually is

**Two strangers can now connect to each other by number, through the Kyiv relay, with consent.**
The scope changed this session from Phase 1 (Conor's private tool) to Phase 2 (two strangers,
neither technical, Conor absent) — recorded at the top of CLAUDE.md with the assumptions it
invalidated.

Built and verified this session (each proven by running it, not by reading code):
- **One executable.** `RemoteDesktop.Viewer` became a library; `FlashDesk.exe` is the only exe.
  Whoever types a number sees the other screen. The 423 lines of hardest-won code (ScreenCanvas,
  RemoteScreen, InputCapture) moved without being rewritten.
- **Sessions through the relay** (`wss://relay.flashdesk.org/ws`). Proven: two instances with
  separate identities paired through Kyiv; caller's window read "connected to 373 883 745",
  the viewed side read "Someone is connected and can see this screen".
- **Nothing listens.** Port 7789 is gone; verified with `Get-NetTCPConnection` that FlashDesk has
  zero listening sockets and only outbound connections to `139.28.36.247:443`. **That is why the
  Windows Firewall prompt no longer appears** — outbound needs no permission.
- **Consent dialog** — one dialog, first-contact flag in amber, 3-second delay on first contact
  only, 30-second timeout, X/Escape/timeout all mean Reject (proven: after WM_CLOSE the host
  returned to Ready, the caller was told they were not accepted, the log recorded REFUSED).
- **KnownCallers + SessionLog** on the client's machine, never transmitted.
- **Auto-reconnect**: viewer redials for 75 s; host re-accepts the same caller for 90 s without a
  new dialog. Relay now verifies the CALLER's identity too, which is what makes keying that grace
  on the caller's number safe.
- **Rate limiting + `/usage`** on the relay: counts only, no numbers, no addresses.
- **Number entry forgives formatting** — `373883745`, `373 883 745` and ` 373-883-745 ` all
  reached the same machine; the box reformats as you type.

## ⚠️ THREE THINGS THAT ARE NOT WHAT THEY LOOK LIKE — verified live 2026-08-03

1. **🚫 THE SITE IS BLOCKED — flashdesk.org HTTPS is a self-signed certificate, and the fix is
   NOT in our hands.** `openssl s_client` returns `Verify return code: 18 (self-signed
   certificate)`; subject and issuer are both `CN=flashdesk.org`. Every visitor to
   `https://flashdesk.org` gets a full-page browser security warning before seeing anything.
   It went unnoticed because the page serves fine over `http://` (HTTP 200, and http is NOT
   redirected to https), so testing without typing the scheme looks perfect.
   **Status 2026-08-03: Conor checked cPanel and there is NO "Run AutoSSL" button on his account —
   only filters. He has opened a support ticket asking the host to issue a proper certificate and
   enable AutoSSL. We are waiting on the provider; there is nothing to build or configure from
   our side.**
   **HARD GATE: nothing goes to any tester until `https://flashdesk.org` shows a padlock with no
   warning.** Verify from `.223`, not by looking at a browser:
   `echo | openssl s_client -servername flashdesk.org -connect flashdesk.org:443 2>&1 | grep "Verify return code"`
   — it must say `0 (ok)`. Until then the whole two-tester plan below is on hold, because the
   first thing a stranger would meet is a browser telling them the site cannot be trusted, which
   is worse than any warning we have been trying to explain away.
2. **There is no `/download` URL.** It returns 404. Conor asked for the button to point straight
   at the file, so the live stable URL is `https://flashdesk.org/dl/FlashDesk.exe` and there is no
   redirect. It is stable the same way — replace the file, never the URL.
3. **Windows warns TWICE.** Conor's own live test found the download-time browser warning, which
   the page did not explain — and which a stranger would have stopped at, never reaching the
   run-time SmartScreen warning the page did explain. Both are now on the page, in order.

Confirmed working live: page content (HTTP 200, both warnings present, correct download link),
the file itself (`/dl/FlashDesk.exe`, 68,035,064 bytes, `application/x-msdownload`), and the
relay (`/health` OK, version 0.3.0-relay).

## The six things still unproven — adaptive quality is the biggest

The pairing test ran two copies on ONE machine with a config-dir override. That proves the relay
path, the protocol, consent, logging and reconnect. It does NOT prove:

1. ~~**⚠️ ADAPTIVE QUALITY IS NOT BUILT — largest risk, and the next piece of work.**~~ **BUILT
   2026-08-03 — see "Adaptive quality and frame rate" at the end of this file.** Measured on a
   simulated 0.6 MB/s link with an ordinary home router's 1 MB of buffering, a typical support
   screen: the picture used to settle **1.85 s behind** and now settles **0.32 s behind**. Still
   unproven on a REAL pair of home connections, which is what items 2 and 3 below are for.
2. **Two different routers/NATs.** Outbound 443 looks like ordinary HTTPS and should pass
   anywhere, but it has never crossed two real home networks.
3. **Real throughput between two homes.** The test was localhost → Kyiv → localhost; it never
   went through a narrow uplink.
4. **The download journey on a stranger's machine** — browser warning, SmartScreen, antivirus.
5. **Different screen sizes and DPI.** Coordinate mapping was verified at Stage 2 on matching
   screens; 4K↔1080p or 150% scaling across the relay is untested.
6. **Windows 11.** `.223` is Windows 10, `.222` is Server 2022. Never run on a real Win11 desktop.

## The test to run, and what to collect

**BLOCKED until the certificate is fixed by the hosting provider — see finding 1.** Then: two
people, each on their own computer and their own internet
connection, each opens `https://flashdesk.org`, downloads, runs it. One reads their 9-digit
number aloud; the other types it and presses Connect; the first presses Accept.

What to bring back — these are the inputs to the next decisions, so collect them deliberately:
- **Did either person stop at a warning, and which one?** (This is the number that decides whether
  €209/year for a certificate is worth it. Right now it is a guess.)
- **Did the picture stall when something moved?** (→ confirms the adaptive-quality priority.)
- **Did the mouse land where they clicked?** (→ DPI/coordinate mapping across different screens.)
- **Did a Windows Firewall box appear?** (It must not. If it does, something still listens.)
- **Did the connection drop, and did it come back by itself?** (→ auto-reconnect in the wild.)
- Their Windows version, screen resolution and scaling — especially if anything looked wrong.

Also worth reading after: `https://relay.flashdesk.org/usage` (sessions, traffic, rejections) and
`%APPDATA%\FlashDesk\sessions.txt` on each tester's machine.

## Session handover (2026-07-30) — read this and CLAUDE.md before doing anything

Everything below is durable because the conversation it came from is gone. Reasoning is included on
purpose: a conclusion without its argument gets reversed by a later session (that has already happened
here — the amber-vs-green indicator, and the square-vs-rounded corners).

- **`.223` is a VMware VM — 2 vCPU on a 24-core Intel Xeon Gold 6262 @ 1.9 GHz host, 16 GB RAM — and
  Conor reaches it over a REMOTE DESKTOP connection. It is not a physical PC and he is not sitting at
  it.** Why it matters: it changes how to read every `.223` measurement (capture over RDP is not the
  same as capture on a machine someone sits at), and it blocks some actions from his side (he could not
  change the refresh rate for the ACCESS_LOST test — RDP forbids display-settings changes).

- **Biggest performance lever, for later, do NOT act now:** `.223`'s 2 cores are a VMware *setting*,
  not fixed hardware (24 are available on the host), so raising the allocation is free. Combined with
  **parallelising the tile encoder** (single-threaded today; ~36–50 ms/frame full-motion encode is the
  ceiling), this is the largest performance lever the project has. It was ruled out earlier only on the
  assumption the hardware was fixed — which is now known to be wrong.

- **ACCESS_LOST recovery VERIFIED by hand: 3 interruptions survived, 0 failures** (screen lock + a UAC
  prompt, no viewer). Item closed. The resilience fix (recreate the DXGI duplication; fall back to GDI
  only if it can't recover in ~30 s; never crash the session) works.

- **Capture-health monitoring runs even with no viewer connected, on purpose.** The host keeps capture
  alive at ~2 fps (a low-rate health poll) whenever it is sharing, so a screen lock moves the counters
  without anyone connecting a viewer first. The window shows "monitoring: active / paused" because a
  zero that means "nothing broke" and a zero that means "nothing was watched" must never look the same.

- **Open: the `.223` GDI capture number is unresolved (16.1 ms vs ~47 ms).** Both were real readings;
  do not relabel one as an estimate. Two hypotheses (full detail in CLAUDE.md "Open — not yet
  decided"): (a) the app window was refreshing during the higher reading; (b) the RDP path changes
  capture behaviour. Every `.223` number came through RDP, so `.223` figures may not represent a client
  at a real machine. Resolve before Stage 4 quotes any performance figure.

- **GDI is capture-bound even on an idle screen.** GDI copies the whole screen every frame regardless
  of change (~47 ms on `.223`), which both caps the frame rate when nothing is happening (~20 fps on
  `.223`) and burns ~half a core continuously — a client on the GDI fallback gets a hot, fan-spinning
  machine. Stage 3's adaptive frame rate (drop to ~1–2 fps when static) is the fix, with that
  justification, not just as a feature name. (Most machines — even VMs — have DXGI, so GDI is rare.)

- **Working rule now in CLAUDE.md §1:** when reporting something as built, verify it the same turn
  (grep / run / show output) and state how in one line ("Confirmed by X"); if it can't be verified, say
  so rather than report it done. Prompted by a "20-tile warning" that had been reported as built but
  never written — Conor can't read code, so the gap must be closed by process.

**State:** Stages 0–2 done and audited; capture resilience + health verified; protocol tests green
(`dotnet test`, 16/16); host + viewer publish as ~65 MB single-file exes; 0 warnings. **Next: the
design pass** (rounded corners via GraphicsPath+AntiAlias with a DPI-scaled Theme radius and DWM corner
preference; app icon in both exes; button hover/pressed; vertical rhythm; deliberate window sizes;
address/code area as the hero), then show both windows, then the relay-location answer (nearest DC to
Kyiv + a way to measure latency before paying), then Stage 3.

# ══════ Adaptive quality and frame rate (built 2026-08-03) ══════

Built while the site's certificate was blocked at the hosting provider, because it was the largest
unproven risk before the first real test: quality was still the fixed LAN-era 95, full motion costs
~1.7–2.2 MB/s, and a typical home upload is 0.6–2.5 MB/s.

## What was built

- **`Host/Net/BandwidthGovernor.cs` (new)** — one ordered ladder of ten (fps, quality) operating
  points, walked one step at a time. Level 0 is exactly the old fixed behaviour, so nothing measured
  on the LAN regresses; level 9 is 2 fps at quality 60. Down fast (two steps when a single send has
  already blocked for most of a second), up slow (three consecutive quiet windows). Idle step-down
  on top: 8 fps after a second of nothing changing, 2 fps after four, back to full rate instantly on
  any change — and on the first INPUT event, which arrives before the screen changes and is what
  keeps the GDI path from feeling late.
- **`Host/Encoding/TileDiffer.cs`** — now remembers the quality each tile was last sent at and hands
  stale ones back a few at a time when there is room.
- **`Shared/Protocol/PingPayload.cs`** — the viewer's Ping now carries its last measured round trip
  (4 extra bytes, no extra traffic).
- **`Host/Diagnostics/LinkTest.cs` (new)** + `FlashDesk.exe --linktest <path> <seconds>` — the
  before/after measurement. Real encoder, real differ, real framing, real governor; only the screen
  and the link are simulated.
- **`Viewer/SessionWindow.cs`** — repaint requests are now coalesced instead of one BeginInvoke per
  frame.
- Technical view gained one adaptation line; the quality selector gained "Automatic" (the default)
  and can still pin a number for testing. **The simple view is unchanged and still number-free.**

## Measured, 40 s per run, Release, `.223`

The number is HOW OLD THE PICTURE IS on the other person's screen, settled (last quarter of the run):

| Link | Router buffer | Content | FIXED | ADAPTIVE |
|---|---|---|---|---|
| 0.6 MB/s | 1 MB (ordinary home router) | typical support screen | **1.85 s** | **0.32 s** |
| 0.6 MB/s | 256 KB | typical support screen | 0.57 s | 0.27 s |
| 0.6 MB/s | 1 MB | full motion | 2.47 s | 2.10 s |
| 0.6 MB/s | 256 KB | full motion | 1.19 s | 0.75 s |
| 2.4 MB/s | 256 KB | typical support screen | 0.06 s | 0.06 s |
| 2.4 MB/s | 256 KB | full motion | 0.27 s | 0.27 s |

The fast-link rows are the ones that had to come out UNCHANGED, and did: adaptation costs nothing
when there is nothing to fix.

## Three things that were tried and were wrong — kept so they are not retried

1. **Duty cycle as the congestion signal** (fraction of wall-clock time spent sending). Measured
   wrong: a healthy link near capacity legitimately spends most of its time sending, so it throttled
   a link with nothing wrong with it — 22 fps where 29 were available. Replaced by send time ÷
   intended frame interval.
2. **Measuring staleness at the instant a frame arrives.** Misses the gap BETWEEN frames, which is
   most of what a person feels. Replaced by the time-weighted age of whatever is on screen.
3. **Send timing alone, with no round-trip feedback.** Blind to an ordinary home router: while a
   deep buffer fills, every send returns instantly. This is the difference between 1.85 s and 0.32 s
   in the table above.

## Frames are still DROPPED, never QUEUED — re-verified, whole chain

Asked for explicitly, so it was checked end to end rather than assumed:

- **Host frame loop** — one frame at a time, capture → encode → send, no collection holding frames.
  Adaptation only makes it send LESS (fewer trips, fewer bytes); it adds no buffer.
- **`MessageChannel.SendAsync`** — writes straight to the stream under a semaphore. No buffer.
- **Relay `CopyAsync`** — strictly sequential receive-then-send on one 64 KB buffer. No queue.
- **Viewer receive loop** — decodes synchronously, so it is self-throttling.
- **Viewer repaint — this one was FIXED, not just verified.** It posted one `BeginInvoke` per frame,
  and `BeginInvoke` is a queue: a busy UI thread would have accumulated repaints of pixels that were
  already out of date. Now one repaint is outstanding at a time and frames arriving meanwhile merge
  into it, with a hard cap that falls back to repainting everything.

## Still unproven

Everything is measured against a SIMULATED link. Two real home connections, two different routers
and two different screen sizes remain untested — items 2, 3 and 5 of the handover's unproven list.
The certificate gate is unchanged: nothing goes to any tester until `https://flashdesk.org` verifies
`0 (ok)` from outside.

---

# 2026-08-03 — Measurement first, then the design system finished

Two commits, in this order on purpose.

## `67e094e` — measure where the frame time goes

Nothing in the project had ever measured the belief that the JPEG encoder was the ceiling.
`DiagnosticRunner` timed capture and encode but called `TileDiffer.Diff` from a `foreach` header
with the timer inside the loop body, so diff cost fell into an untimed gap; a live session timed
only the send. Four stages now: capture, diff, encode, send — a new `FrameTimings` (same one-second
window as `RateMeter`), one line in the host's TECHNICAL view only, and a diff column in the
diagnostics report.

`sendTimer` was READ, never re-timed: what it measures feeds the governor's congestion signal.

**First run validated the instrument** — the stages sum to the measured frame rate exactly
(capture 179.8 + diff 16.8 + encode 52.0 = 248.5 ms = 4.0 fps, measured 4.0).

**And it overturned the assumption.** Capture was ~72 % of the frame, encode ~21 %.
⚠️ **But DXGI was unavailable, because that run went through RDP** — so it measured the GDI
fallback, a path no ordinary client is on. This also contradicts a line in CLAUDE.md saying both
machines provide DXGI: true only from a console session, not over RDP. The honest conclusion is not
"capture is the ceiling"; it is that **nothing measured through RDP can rank these stages**.
Parallel tile encoding is therefore NOT justified yet and was deliberately not built.

One finding survives regardless: on an idle screen diff costs **19.4 ms against encode 1.8 ms** —
ten times — because `HashTile` walks 8.8 MB one byte at a time every frame. That is the GDI-path
"the client's fan runs all session" problem, now with a number.

## `61f1ea0` — themed inputs, and green back to the button

The palette discipline was already total (no `Color`/`Font` literal outside `Theme.cs`); the gap was
control COVERAGE. Added `RoundedTextBox`, `ThemedComboBox`, `ThemedCheckBox` following
`RoundedButton`'s pattern, plus `Theme.MakeTextBox/MakeComboBox/MakeCheckBox`, swapped in at all
four call sites. Checkbox state is carried by the checkmark GLYPH, never by the blue fill, so it
survives greyscale.

Site: green had leaked onto three elements, diluting "green is the thing you click". The scam
warning became a FILLED panel — louder than the outline it replaced — so the safety warning gained
prominence while green became exclusive to the download button. Also added link-preview metadata
(a preview with no title reads as a scam link when pasted to a stranger), a spacing scale, and
focus rings on every link.

## Left undone, deliberately

- **Re-measure with DXGI available** (a console session, not RDP) before choosing any optimisation.
- `TileDiffer.HashTile` vectorisation — justified by the data, not yet built.
- `SessionWindow`'s two checkboxes have not been seen on screen; that needs a live session.
- The outer window frame stays square on Windows 10 — `WindowCorners` is a Windows 11 feature and
  degrades silently, as intended. Rounded corners appear on cards, buttons, fields and checkboxes.
- The certificate gate is unchanged: nothing goes to any tester until `https://flashdesk.org`
  verifies `0 (ok)` from outside.

---

# 2026-08-05 — The download barrier removed for nothing, and the first OBSERVED capture fix

## The Chrome block was the HOST, not the file — measured, single variable

Chrome was not warning about the download from flashdesk.org, it was **blocking** it: transferring
the whole file, then withholding it as `Unconfirmed …crdownload` behind "Suspicious download
blocked". Behind a chevron: *"This file isn't commonly downloaded and it may be dangerous"*, with
**Delete from history** as the solid primary button and **Download suspicious file** as the pale
secondary — three clicks with the word *dangerous* in them, with the eye steered toward delete.

The same file — same bytes, same SHA-256 `72E45B4E…249B19`, unsigned, no reputation of its own —
was published as a GitHub release and downloaded in a fresh Chrome profile minutes later on the same
machine. **It came through cleanly, with no warning at all.** Only the host changed.

Two weaker tests preceded it and are recorded in CLAUDE.md so nobody repeats them believing they
settle anything: a *signed* file from GitHub differs in two ways at once; an unsigned but hugely
popular one (yt-dlp) carries years of its own file reputation.

**Consequence:** the code-signing certificate stopped being urgent. It had been the planned rescue
for a barrier that stopped everyone; that barrier is gone and it cost €0. The download button now
points at the GitHub release (`/releases/latest/download/` form), and `flashdesk.org/dl/FlashDesk.exe`
stays live and is named on the page as a fallback. **Verified that the page survives a GitHub
takedown:** `github.com` appears in `site/index.html` exactly once, in the button's href, with no
script, image or external fetch anywhere.

## ✅ FIRST OBSERVED — the capture fixes, watched rather than inferred (Conor, 2026-08-05)

Until now all four capture fixes were verified individually — the escaping-exception fix by direct
experiment, the new protocol message by round-trip tests — but **nobody had ever seen them work in a
live session.** That changed today.

Conor ran the whole journey on two machines: GitHub download on both, two distinct 9-digit numbers,
the consent dialog with its first-contact line, connected, screen visible, control working. Then he
**locked the shared machine mid-session**, and:

- the graphite band appeared over the operator's picture with exactly the intended sentence;
- **the session did not drop** — no "reconnecting", which is precisely the old bug;
- the client's own window kept its amber "Someone is connected and can see this screen" strip
  throughout.

That is the first time any of these fixes has been **observed** rather than inferred. Record kept
because the distinction is the whole discipline: the same gap between "built" and "seen working"
is what let a dead ComboBox chevron and a twelve-commit-stale download both ship.

## The exact Windows wording, finally captured

SmartScreen would not reproduce on the dev machine even with the file marked as internet-downloaded,
so this came off Conor's own screen. The important structural fact: **the first screen offers no way
forward at all** — its only button is *Don't run*, and the way through is a small grey text link.

    Windows protected your PC
    Microsoft Defender SmartScreen prevented an unrecognized app from starting.
    Running this app might put your PC at risk.
    More info
    [Don't run]

After clicking **More info** it grows to show `App: FlashDesk.exe`, `Publisher: Unknown publisher`,
and finally offers `[Run anyway]  [Don't run]`. The page now states all of this verbatim, including
"Unknown publisher", with the explanation that it means nobody has paid to attach a verified name —
not that the file is unsafe.

## Left undone

- The remaining barrier is SmartScreen, and unlike the Chrome block it is **not** solvable by moving
  the host: the warning names the file and its publisher, not where it came from.
- `sessions.txt` from the live test not yet reviewed.

## ✅✅ ALL FOUR CAPTURE FIXES — CONFIRMED IN A LIVE SESSION (2026-08-05)

Not "built and tested". **Observed, end to end, on two machines, from the file a stranger actually
downloads.** The session log holds both runs, so the before and after sit side by side in one file.

**Before the fixes (2026-08-04):** one CONNECTED line and **fourteen** report blocks in 38 seconds.
Bytes frozen at 0.7 MB while the duration climbed 4s → 23s, so KB/s fell 172 → 33 purely because the
divisor grew. `worst second 15` printed beside an average of 3.0 — arithmetically impossible, since
a minimum cannot exceed a mean.

**After the fixes (2026-08-05):** ONE block.

```
Lasted        : 2m 54s
Screen        : 1920 x 1080, captured with DXGI Desktop Duplication
Picture       : 12.6 fps average, worst second 0
Sent          : 2.1 MB total, 14.7 KB/s average
Screen hidden : 26s of that time the screen could not be seen at all
Interruptions : none
```

| Fix | Confirmed by |
|---|---|
| **1. Capture failures never end a session** | No churn at all. One session, 2m 54s, `Interruptions: none`. |
| **2. Silence + plain words instead of a frozen picture** | The `Screen hidden: 26s` line is the lock Conor performed. He **watched the graphite band appear over the operator's picture with the intended sentence, and clear again on unlock.** The session stayed up; the client kept its amber strip throughout. |
| **3. One report at the true end** | One block, not fourteen. |
| **4. The arithmetic holds** | `worst second 0` now sits BELOW the 12.6 average, and the 26s hidden stretch is excluded from the rates instead of dragging them down. |

Noted and deliberately not chased: DXGI this time rather than GDI, and a slowest single frame of
516 ms. Both are consistent with reaching the machine over Remote Desktop, and neither affected the
outcome. The way to tell a real recovery failure from an RDP artefact, using only what is visible:
a real failure leaves the band on screen after unlocking, or drops the session with a "reconnecting"
message. Neither happened.

**Why this entry exists in this shape:** the distinction between *inferred* and *observed* is the
discipline this project keeps relearning. A dead dropdown chevron and a twelve-commit-stale download
both shipped while every build passed. "Tests pass" is not "someone watched it work".

## The two files that carry the next test (current as of 2026-08-05)

Both are plain text in the repository root, written to be OPENED AND COPIED FROM rather than read in
a terminal — Conor works in one and cannot select text reliably there.

| File | For | Contains |
|---|---|---|
| **`tester-message.txt`** | Send to each tester | The whole journey in plain words: download from the button (the file comes from **github.com** — said explicitly so a careful person is not unsettled by seeing that address), the ONE Windows warning with its two-screen shape spelled out, a warning that they will see `Publisher: Unknown publisher`, the scam line placed at the moment somebody could be talking them through a security prompt by telephone, **what a working session looks like from their side** (the orange band, and their mouse moving on its own — someone not told that will think their computer has been taken over), and a request for `sessions.txt` so they never have to describe numbers. |
| **`watch-list.txt`** | Conor, while on the phone | Five things in the order they happen. Step 2 is the one that matters: at the Windows security screen, count the seconds of silence, whether they found **More info** unaided, and whether they got through without coaching. **That count IS the certificate trigger**, so the file explicitly warns against rescuing them early — doing so destroys the only measurement that decides the purchase. Ends by naming the most valuable thing to bring back, which is not a number: *where each person went quiet, and what they were looking at.* |

These supersede every earlier draft of a tester message in this file. Anything older describing
"open the downloads box and choose Keep" describes a flow that no longer exists.

## Files, part one — the client's side of it (built + tested 2026-08-06)

Three items from the handover's "Next, in order", in that order. **Nothing here is visible to a
stranger yet: there is no viewer panel, so none of this can be reached from a session.** It is the
half that runs on the client's machine, and it is finished and tested.

`dotnet test` **162 → 262**. Everything below was proved by running it, not by reading it.

### 1. A cut-off transfer no longer leaves litter — `Shared\Files\PartialFiles.cs`

A transfer writes to a temporary name and renames only at the last byte, which is what stops a half
file ever wearing the real name. The cost is that a hard drop leaves the temporary file behind in
the client's own folder and nothing ever comes back for it.

- Every partial is recorded in a ledger (`%APPDATA%\FlashDesk\unfinished-files.txt`) **before the
  file is created**. The other order looks tidier and loses exactly the case this exists for.
- The next start deletes what it recorded and did not finish, and writes one `CLEANED` line to
  `sessions.txt` — only when something was actually removed.
- **Deleting is the dangerous half, so it is guarded twice:** a path is removed only if its name
  carries BOTH our prefix and our suffix. A corrupted or hand-edited ledger cannot name
  `kernel32.dll` and have it deleted. Cleanup follows the **ledger**, never the folder.
- The partial lives in the **destination** folder, not a FlashDesk temp folder, because the
  finishing rename is only atomic inside one volume.
- The sweep runs off the UI thread: a sleeping drive must not delay the number appearing.

### 2. Chunks are sized from the link — `Shared\Files\FileChunkSize.cs`

**The bug this fixes was not slowness, it was the picture.** Every send is serialised behind one
lock, so while a chunk is written the latency ping waits behind it; the viewer measures that wait as
round-trip time, and the governor treats a round trip 250 ms above the session's best as a link
backing up — and drops the picture. A fixed 256 KB chunk takes ~430 ms to leave a 0.6 MB/s uplink,
so a download would have blurred the screen it was not competing with.

- The chunk is now sized to a **200 ms hold**, pinned deliberately under that 250 ms threshold. The
  coupling is written at both ends (`ProtocolConstants.MaxChunkSendHoldMs` and `QueueHeavyMs`).
- The measured rate **reads 2–3× high** (CLAUDE.md, against a known 600 KB/s link), so it is divided
  by 3 first. Believing it would give chunks three times too big — the original bug in new code.
- Unmeasured link → **64 KB**, not the ceiling: the first chunks are sent in ignorance and are
  exactly the ones that would stall the picture.
- `BandwidthGovernor.OnBulkSent` feeds chunk sends into the **rate estimate only, never the ladder**:
  during a download of a still screen the frames never block, so chunks are the only measurement
  available — and a link being full is not a link being broken.
- Tests assert the **hold time on real uplink speeds**, not the arithmetic against itself.

### 3. The host file service — `Host\Files\HostFileService.cs`

Look, list, copy off, and receive. No delete, rename, move or new folders.

- **Every request runs off the message loop.** That loop also answers the latency ping and injects
  the mouse; listing `WinSxS` on it would freeze the remote cursor and have the governor conclude
  the link had collapsed.
- **Two consent questions, both per connection, both discarded with the socket** — deliberately not
  covered by the 90-second reconnect grace, or an operator could drop the link on purpose and come
  back with file access unasked. Both dialogs block FlashDesk's own injected input while up.
- `LocalDrives` is the second half of the path rule: **a mapped network drive is another machine
  wearing a drive letter**, and no string check can see that. The `OpenedPath` handle re-check runs
  after the open, on the handle, for both reading and writing.
- Listings are paged, folders before files. Bounded work: one listing and one transfer at a time,
  anything more is answered `Busy`.
- Uploads: bare-name rules, containment on the resolved path, free-space check before a byte,
  sequential offsets only, more-than-promised refused, one exit for every failure that always
  deletes the partial and always sends a sentence.
- **A PROGRAM is asked about by name every time**, even after the general yes — see "one deviation"
  below. Logged with its own verb, so a person scanning `sessions.txt` sees software arrived.
- Handshake now actually advertises `FileBrowsing | FileUpload`. It advertised nothing before.

### One deviation from the approved plan, flagged rather than buried

The plan says the upload question is asked **once per connection**. Implemented as: once per
connection for files in general, **plus every time for a program, by name**. Otherwise the first
upload could be a text file and every `.exe` after it would arrive in silence — the exact step a
tech-support scam needs. Reversing it is one line in `HostFileService.AllowedToWriteAsync`.

### The test project moved to `net8.0-windows`

It now references Host. The file service's safety checks need `DriveInfo` and Win32 and cannot live
in Shared, which would have left the code with the worst failure mode as the only code with no
tests. **`RemoteDesktop.Shared` is unchanged and still platform-neutral** — the Linux relay
references it (architecture rule 4 intact).

### Still UNVERIFIED, and not to be described otherwise

- **None of this has been driven by a viewer**, because the viewer panel does not exist. The tests
  drive the host over a real loopback socket against real folders; that is not the same as two
  machines.
- **The mapped-network-drive refusal is read by eye, not asserted.** Proving it needs a real share
  and an administrator to map it. Named at the top of `LocalDrivesTests` so it is not forgotten.
- Nothing about the client's live indicator, the second band line or the direction arrow is built.
- The claim that a transfer slows the picture is still reasoned, never measured on a real link.

### Next, unchanged from the handover

4. The viewer file panel — must NOT take focus.
5. The client indicator — second band line, direction arrow, restore-without-focus.
6. Per-monitor DPI, as its own stage.

## Files, part two — the operator's side, and the self-test (built + tested 2026-08-06)

`dotnet test` **262 → 277**. A double-clickable build lives at
`C:\Users\PC\Desktop\FlashDesk-test-build\FlashDesk.exe` — **not** the public download, which is
still 0.3.1 at `b6734e0`-era and stays that way until this is finished.

### The program proves its own housekeeping — `Host\Diagnostics\SelfTest.cs`

Conor should not have to hand-craft a partial file and a ledger entry in PowerShell to find out
whether cleanup works. Five checks against a real folder on the machine it is actually running on,
answering PASS or FAIL. **Four of the five are rows where nothing should happen** — an ordinary file
named by the ledger, an unrecorded partial, a completed transfer, a partial still being written.
Three ways in, one piece of code so they cannot disagree: a **Run self-test button in the technical
view**, the top of `--linktest`, and `--selftest` (exit 0 or 1). Deliberately **not** automatic at
start-up: it writes files, and a stranger should not pay that cost every launch to prove something
to somebody who is not them.

### `Viewer\Files\ViewerFileClient.cs` — and the first test of both halves together

It owns no socket and no window; it is handed a `MessageChannel` and fed what arrives. That is what
makes `FileTransferEndToEndTests` possible: the real operator half driving the real client half over
a real socket, both message pumps running as in the product. **Those eight tests are worth more than
either side's own** — almost every way this feature can be wrong is a *disagreement* between the
halves, and neither side's tests can see that alone.

### `Viewer\Files\FilePanel.cs` — and two decisions worth keeping

**NO KEYBOARD SHORTCUT INTO THE PANEL. Click-only.** Conor's correction, and it generalises: any
combination FlashDesk keeps is a key the operator can never send to the person they are helping, and
the hole is invisible — you press it and nothing happens over there. Ctrl+L was the obvious
candidate and the worst one, because a browser is open on *their* machine too. **The rule this
settles: while remote control is active, FlashDesk swallows nothing.**

**The focus problem was never about typing.** Every useful control takes focus when clicked, and
remote control has *always* stopped at that moment. What was missing is that nobody was told — an
operator who types into a suspended session sees the letters go nowhere and concludes the other
machine has frozen. The panel therefore does not avoid focus, it **announces** it: a band across the
**picture**, not a discreet indicator. `InputCapture.Suspend` releases every held key **first**, then
stops forwarding; `Suspended` is deliberately separate from `Enabled` so a suspension cannot silently
turn the operator's own control choice off and leave it off.

The panel appears only if the host advertised `FileBrowsing`, and the person at the other end is
asked only when it is first opened — a session where the operator never opens it asks them nothing.
The "not sorted while there are more pages" sentence is on screen with the count, because a page
*looks* like a sorted folder and the operator would otherwise conclude a file is not there when it is
on page three.

### Still UNVERIFIED after part two

- **The release ORDER inside `Suspend`** — that held keys reach the remote machine before forwarding
  stops — needs a real focused window to reach from a test. Verified by reading, and stated as such
  at the top of `InputSuspendTests`. It is a two-machine check.
- **No human has used the panel.** It has never been clicked. The end-to-end tests drive the
  protocol, not the interface.
- Everything listed as unverified in part one still is, including the mapped-network-drive refusal.

## The adversarial read — six holes, all shut (2026-08-06, commit `98eb627`)

The finished file service was read by an agent that did not write it, briefed to **refute three
claims, not to review**. It refuted two, and found four more. **Every finding was real, and each was
confirmed independently before anything was changed** — including the load-bearing one, that
`Path.GetFullPath` expands an 8.3 short name, which was *measured* rather than believed. Nothing it
proposed would have undone a settled decision, so nothing had to be discarded.

`dotnet test` **277 → 293**.

1. **The overwrite dialog could name a file that did not exist.** `Path.GetFullPath` expands
   `IMPORT~1.DOC` to `important-document.docx`, so a request could name a file the person had never
   seen, pass every rule, and resolve onto one of their real documents. The dialog said *"They are
   sending IMPORT~1.DOC"*; they would read an unfamiliar name, conclude it was junk, press Replace,
   and lose their own file **under a name that appeared nowhere on their screen**. The log recorded
   the same fiction. Now refused: **the name checked must equal the name resolved.** This is the
   exact failure `RemotePath` already reasons about for a trailing dot — *"a name that changes
   between being checked and being used is the shape of a bypass"* — happening one screen later.
2. **The listing path never ran the handle re-check.** Only the read-a-file path did. A directory
   link is invisible in a string, so `C:\Projects` can BE `\\fileserver\finance` and every string
   check agrees it is on drive C. Contents were never reachable, but every file name, size and date
   on an employer's share was. `LocalDrives`' own comment claimed `OpenedPath` covered this case; on
   that path it was never called.
3. **The upload "yes" was not bound to the folder it named.** The dialog says *"into &lt;folder&gt;"*
   and *"Nothing else on this computer is changed"*, and the second sentence was untrue: one yes for
   a readme in Downloads licensed writes anywhere the account could reach. **A different folder is
   now a different question** — which is the question the person thought they were answering, not
   the repetition CLAUDE.md warns about.
4. **A consent-free oracle.** Folder-exists and free-space ran *before* the person was asked, so a
   refused operator could still tell `NotFound` from `RefusedByPerson` for any path, unlimited and
   invisibly — enumerating account names and installed software on a machine whose owner said no —
   and bisect the declared size to read free space. **Nothing is learned from the disk before the
   answer.**
5. **A name that reads backwards.** U+202E passed `IsSafeFileName`, so `Invoice<RLO>xcod.exe`
   displays as `Invoiceexe.docx` on the one line whose job is to say what is arriving. Four ranges
   of direction-control characters refused, with tests proving German, Georgian and Russian names
   still pass.
6. **Alternate data streams were readable.** The write side always refused a colon; the read side did
   not — and reading is where the secrecy matters, since an ADS appears in no listing at all.

**Claim 3 survived.** The reader could not make cleanup delete a file it did not create, nor leave a
half file wearing the real name. Its one fair criticism is now printed inside the self-test: **it
checks the ledger half only**, and would still say PASS if the rename or the failure exit were broken.

**A cost that comes with fix 2, recorded as a decision rather than discovered later:** a folder
reached *through* a junction can no longer be listed under that name. That is precisely what the
check is for, and the operator can navigate to the real location — but it is a real behaviour change
on machines that use junctions for redirected profile folders.

**The three attack tests were confirmed live on this machine** — the junction was created, the 8.3
alias `IMPORT~1.DOC` existed, the stream was written — so none of them silently skipped.

## The transfer-slows-the-picture claim is now measured, not reasoned (2026-08-26)

Before the first two-person, hands-on test of the file panel, one gap: `PROGRESS.md` had twice
recorded "the claim that a transfer slows the picture is still reasoned, never measured." Fixing
that needed to happen before the test, not after, so the run itself would answer the question
instead of leaving it for another round of reasoning.

`SessionRecorder` now keeps a plain history — one entry per completed second, frames and average
JPEG quality — for the whole session, cheap to keep because a session is at most a few thousand of
them. `HostFileService` tells it the instant a transfer actually starts moving bytes and the instant
it stops (done, refused, or cut off) — the same two points that already claim and release
`_transferBusy`, so "during" always means bytes were genuinely in flight, never the moment the
operator merely asked. The session report gains one new block per transfer: average fps and quality
for the five seconds before it started, for its whole duration, and for the five seconds after it
ended. `dotnet build` and `RecordFrame`'s new `quality` parameter (the real JPEG quality the governor
was using, not just the ladder step) are the only signature changes; everything else is additive.

`dotnet test` **293 → 293** (unchanged) — this is instrumentation, not behaviour, so no new test was
added for it; it will be exercised, and its output judged, by the two-person test itself. **Two
pre-existing tests were found flaky on this machine while checking that** —
`HostFileServiceTests.A_PROGRAM_is_asked_about_by_name_every_single_time` and
`FileTransferEndToEndTests.A_reply_for_a_request_the_operator_has_moved_on_from_is_dropped` — both
confirmed to fail at the same rate on the unmodified `dbc481c` checkout, so this is a pre-existing
timing issue, not something this change introduced. Left unfixed here as out of scope; flagged so it
is not mistaken for a new regression.

### ⚠ KNOWN FLAKY, PRE-EXISTING — do not chase these as new bugs

`HostFileServiceTests.A_PROGRAM_is_asked_about_by_name_every_single_time` and
`FileTransferEndToEndTests.A_reply_for_a_request_the_operator_has_moved_on_from_is_dropped`
intermittently fail (roughly every other `dotnet test` run, not every time). **Confirmed
2026-08-26 by running the unmodified `dbc481c` checkout four times in a row and seeing the same
two tests fail at the same rate** — this predates every change in this and the following section.
Left deliberately unfixed (Conor's instruction: leave them, just write it down). If `dotnet test`
shows one of these two failing, that is this known issue, not a new regression — check the test
name against this list before spending time on it.

## Second adversarial read (FilePanel/InputCapture): 4 claims survived, 2 real bugs fixed (2026-08-26)

Before the first hands-on, two-person test of the file panel, the same adversarial-refutation
brief that found the file service's six holes was pointed at `SessionWindow.cs`'s focus-suspend
wiring and `InputCapture.cs`. **First attempt returned a corrupted, worthless result — a forked
agent's "findings" were a verbatim copy of the parent session's own previous reply, with zero
tool calls made, meaning neither file had actually been opened.** Discarded rather than reported;
see CLAUDE.md §10, written directly because of this — an agent's result is a claim, not a fact,
and zero tool calls on a review task means no review happened. Relaunched as a fresh
(non-forked) agent, which returned a real result with file:line citations; the two most
consequential claims from it were independently re-verified against the actual source (not
re-read on faith) before being acted on.

**4 of 5 claims investigated SURVIVED** (the code already does what earlier sessions believed):
key-release-before-suspend ordering in `InputCapture.Suspend`; `Suspended` staying independent of
`Enabled` with no bad state possible at session end; no keyboard shortcut into the file panel
(and the actual mechanism is stronger than "no accelerator defined" — `ScreenCanvas.IsInputKey`
returns `true` unconditionally, so Tab/Escape/arrows never reach WinForms navigation at all while
the canvas holds focus, they go out as remote input); and the "paused" band being driven by the
exact same call (`SetControlPaused`) that actually suspends input, so the two can never disagree.

**2 NEW bugs found and fixed, both in the class this project treats as worst — something stops
and nobody is told:**

1. **The file panel silently outlives the connection it was built against.** `ToggleFilePanel`
   captures `_client.Files` into the panel once, the first time it is opened, and never again.
   `ReconnectAsync` swaps in a whole new `_client` and correctly rewires every event handler onto
   it — but never touched `_filePanel`, which stayed bound to the disposed old connection's file
   client for the rest of the session. Every list/download/upload after any reconnect would have
   run against dead state. **Fixed:** on reconnect, a `_filePanel` that exists is torn down and,
   if it was visible, rebuilt through the exact same path `ToggleFilePanel` already uses the first
   time it opens — which correctly asks the person again, matching the existing per-connection
   consent design (`HostFileService`: "discarded with the socket").
2. **A download in progress hung forever, with no error, if the connection dropped.**
   `ViewerFileClient.Dispose()` completed the download's chunk channel but never failed or
   cancelled `download.Finished`, the `TaskCompletionSource` `DownloadAsync` awaits next — and
   nothing else was ever going to complete it once the link was gone. `UploadAsync`'s equivalent
   waiter WAS one of the four `Dispose()` already failed, so upload correctly showed an error in
   the identical situation; download did not. **Fixed:** `Dispose()` now also fails
   `download.Finished` with the same "The connection ended." used everywhere else, which
   `DownloadAsync`'s existing catch-all turns into a proper `FileGetEnd` with an error status.

**Swept the rest of the Viewer project for the same shape of bug** (something captured from the
connection at one point in time, never refreshed after a reconnect) rather than treating the file
panel as the only instance, per Conor's instruction. Checked every use of `_client` in
`SessionWindow.cs` and every file in the Viewer project referencing `ViewerClient`: input
forwarding, the status line, and the FPS/latency readout all read the `_client` FIELD at call
time so they pick up a reconnect automatically; the four picture/connection events ARE correctly
rewired in `ReconnectAsync`; nothing outside `SessionWindow.cs` holds a `ViewerClient` reference
at all. The file panel's captured `files` was the only stale reference found. One minor,
lower-severity item noted but not changed: `_showFiles.Visible` is set once from
`_client.HostCapabilities` at construction and would not update if capabilities genuinely
differed after a reconnect — a cosmetic checkbox-visibility question, not a functional break, and
not touched without being asked.

**New regression test, proven both ways, not just added:** `FileTransferEndToEndTests
.Disposing_mid_download_fails_it_instead_of_hanging_forever`. First version disposed
`ViewerFileClient` alone and passed even against the un-fixed code — a false negative, because
the pump and socket were still alive and the real `FileGetEnd` from the host arrived and resolved
the download normally regardless of `Dispose()`, racing past the bug. Corrected to also cancel
the pump and close the socket, which actually simulates the connection being gone. Confirmed
**fails with `TimeoutException` at 10s against the pre-fix code, four consecutive times**, and
**passes in ~230ms with the fix** — both checked by hand before trusting the test.

`dotnet test`: 293 → 294 (the one new test). No change to the two known-flaky tests' behaviour.

Also this pass: added the missing `.gitignore` rules for RuFlo's own generated state
(`.claude/agents/`, `.claude/commands/`, `.claude/settings.json`, `.claude/proven-config*`,
`.claude/helpers/`, `.claude/skills/`, `.claude-flow/`, `.swarm/`, `.agents/`, `.mcp.json`,
`ruvector.db`) — none of it is this project's content. Deliberately left OUT:
`.claude/settings.local.json`, which turned out to be this project's own accumulated Bash/
PowerShell permission allowlist from real FlashDesk work, not RuFlo's — and it is already covered
by Conor's global git ignore for that filename anyway, confirmed with `git check-ignore -v`.
Deleted four confirmed-empty (0 bytes), untracked files at the repo root
(`0),+`, `126`, `SetControlPaused(false)`, `b.Length)`) whose names are fragments of shell/C#
text — almost certainly the accidental output of an unescaped multi-line command in an earlier
session, not project content.

## Two copies, one machine, safely — and two bugs the first real click found (2026-08-26)

**The manual two-VM setup (`.222`/`.223`) was defeating Conor** — not a developer, RDP-only, and
the setup itself had become the obstacle to the first hands-on test. His own catch: the 9-digit
number is read from `%APPDATA%\FlashDesk\identity.json` (see `IdentityStore`/`FlashDeskFolder`),
which is why two copies on one machine share a number — and `FLASHDESK_CONFIG_DIR` already existed
to override that folder (`FlashDeskFolder.cs`, comment: "so two copies can run side by side on one
machine ... which is how relay pairing is tested without a second computer") but nothing had ever
turned it into something a non-developer could actually run.

**Built `scripts/Start-Second-FlashDesk-Copy.cmd`** (also placed on the Desktop as
`Start SECOND FlashDesk copy (safe test).cmd`) — one double-click starts a second, independent
`FlashDesk.exe` with its own identity. Three environment variables, all new except the first:

- `FLASHDESK_CONFIG_DIR` (already existed) — its own data folder, its own 9-digit number.
- `FLASHDESK_DISABLE_CONTROL` (new — `FlashDeskTestMode.ControlDisabled`) — makes this copy
  physically unable to send OR act on a "control mouse and keyboard" request, in either direction.
  **Why both directions matter:** with two copies on the SAME real desktop, whichever one ends up
  playing HOST in the pairing would inject input into Conor's actual machine, not a separate one —
  and either copy could end up playing either role, since he chooses which window connects to
  which. Disabling only one direction on only one copy leaves the other direction open depending
  on which way he connects. The viewer-side checkbox is disabled and relabelled
  (`SessionWindow._control`); the host-side injection call is skipped even if a request somehow
  arrives anyway (`HostServer.InboundLoopAsync`, `MessageType.Input` case) — checked, not just
  trusted to the checkbox. **Consent is untouched**: the Accept dialog still requires a real
  click, on both copies, always.
- `FLASHDESK_FORCE_GDI` (new) — see below.

**Verified, not assumed** (sandbox disabled for this one check, since it needed a real GUI
process): launched a real second copy with these three variables and a real normal copy side by
side. Title bars read exactly `FlashDesk` and `FlashDesk — TEST COPY (remote control disabled)`;
`%APPDATA%\FlashDesk\identity.json` and `%LOCALAPPDATA%\FlashDesk-SecondCopy\identity.json` both
exist with **different** 9-digit numbers (`163035814` vs `175503444`). Both processes stopped
afterward so Conor starts from a clean state. The Accept-dialog and host-side injection paths were
checked by reading the code, not exercised through a live paired session — that is what the actual
test now proves.

### Bug: "Capture failures: 1" in red, GDI instead of DXGI — explained, not a regression

Traced to the real `FlashDesk-capture-log.txt` on the Desktop, not guessed:
```
07:09:06  INTERRUPTED   capture=Dxgi  reason=DXGI access lost (UAC prompt, lock, or resolution/monitor change)
07:09:37  DID NOT RECOVER  Dxgi gave up after 30538 ms; fell back to Gdi   (failures=1)
```
Windows will not let two processes both hold a DXGI Desktop Duplication session on the same
output at once. This never happened before because every previous test used two separate VMs
(`.222`/`.223`), each with its own adapter — Conor manually running two copies on one real machine
today, for the first time, is what surfaced it. `DxgiScreenCapture.GiveUpAfterMs` = 30 s matches
the 30538 ms in the log exactly. **Not a bug — DXGI genuinely cannot be shared** — but the new
launcher's `FLASHDESK_FORCE_GDI` flag (`HostServer.Start`, `ScreenCaptureFactory.Create`) skips
the second copy straight to GDI, so it reaches the same end state without 30 seconds of a red
failure counter along the way, every single time it is used.

### Bug: file panel rows nearly invisible — found because Conor clicked it himself

`FilePanel`'s `ListView` never had `BackColor` set, so it used WinForms' plain light default while
every row's `ForeColor` was set to `Theme.OperatorHeaderText` — a colour meant for the DARK
operator theme. Light text on a light background read as "the listing is empty" when it was not;
Conor saw a barely-visible `C:\` row and nearly missed it. Fixed by setting `_list.BackColor` /
`_list.ForeColor` to the exact pairing already used elsewhere in this same panel
(`Theme.OperatorHeader` / `Theme.OperatorHeaderText`) — no new colour introduced. Also removed a
`row.ForeColor = entry.IsDirectory ? Theme.OperatorHeaderText : Theme.OperatorHeaderText` — a
ternary whose two branches were identical, which reads as an intentional file/folder distinction
that was never actually there.

Two status messages improved to say **how**, not just what's required: the initial message no
longer says "choose a folder" (their drives actually appear automatically once access is granted,
nothing to choose) — it now describes what is actually happening; "This folder is empty." now
also says to type another path and press Go, or press Up.

### What was confirmed working, and left untouched

Conor's own words: the connection, the panel, and the pause band all worked — the band read
correctly and appeared the instant the panel was clicked. Nothing about the connection, panel
opening, or `SetControlPaused`/focus-announcement mechanism was touched this pass.

`dotnet build`: 0 warnings, 0 errors. `dotnet test`: 294 total, 293 passed — the one failure each
run is one of the two already-documented pre-existing flaky tests, confirmed by name each time.

## Clipboard copy-paste, and three smaller decisions cut from the same list (2026-08-26)

Conor's framing: fetching a file was find-it, select-it, press-a-button, choose-a-folder — three
decisions for something that should be one. Two directions: Ctrl+C on their screen, Ctrl+V on his
(their file arrives), and the reverse.

**Simple (eager fetch-to-temp-then-real-clipboard-file), not virtual (delay-rendered), decided
with numbers, before writing code, per Conor's own condition:** a real local file on the clipboard
(`CF_HDROP`) is read correctly by essentially every Windows application — it is the exact format
an ordinary Explorer copy already uses. A delay-rendered/virtual entry (`CFSTR_FILECONTENTS`) is
inconsistently supported across paste targets — many either fail outright or silently produce a
0-byte file — which is precisely the "silent wrong result" class of bug this project spends most
of its effort refusing to ship. It would also need to trigger network I/O, consent, and logging
from inside a COM `IDataObject.GetData` call arriving synchronously on some OTHER process's UI
thread (Explorer's, Outlook's, whatever the paste target is) — a well-known way to hang that
foreign application. The simple version instead reuses `DownloadAsync`/`UploadAsync` exactly as
written, on this process's own thread, so consent and `sessions.txt` logging are inherited for
free rather than re-implemented.

**Size threshold: 100 MB, Ctrl+C only.** Below it, an ordinary document, photo or spreadsheet
copies with no prompt at all — the actual point of the feature. Above it, on the slow home
uplink already measured elsewhere in this project (~0.6–2 MB/s), a silent fetch could run 50–170
seconds before the clipboard even has anything on it; that is exactly the moment worth one extra
click; a `MessageBox` asks first, Cancel defaulted. Double-click and the button are NOT gated by
this — a click on a specific row is a deliberate act, not the accidental-keypress risk Ctrl+C is.

**What was built:**
- `FilePanel.CopySelectedToClipboardAsync` (Ctrl+C, list focused) — downloads the selected file to
  a fresh `%TEMP%\FlashDesk-clipboard\<guid>\` folder (a new folder per copy, so copying the same
  name twice in a row can never collide with a leftover from the first copy) and calls
  `Clipboard.SetFileDropList`.
- `FilePanel.PasteFilesAsync` (Ctrl+V, list focused) — reads real local paths off the clipboard
  (`Clipboard.GetFileDropList`, filtered to `File.Exists` so a copied folder is silently skipped
  rather than erroring), uploads each one in turn through the SAME `UploadFileAsync` the "Send a
  file…" button now also calls — same per-file consent, same PROGRAM-by-name question, same log
  lines, because it is the same call.
- Both bound to `_list.KeyDown` specifically, not the panel generally, so Ctrl+V still pastes TEXT
  normally in the path textbox — this does not compete with that.
- **Clipboard TEXT is deliberately untouched.** Only `FileDropList` is read or written; nothing
  syncs the text clipboard in either direction — a different feature (people paste passwords into
  clipboards) with a different decision that was not made here.

**The three "either way" items, one cut and explained rather than silently dropped:**
- **Double-click a file now fetches it immediately, no dialog** (`OpenSelected`, extended — it
  already handled folders, files fell through to nothing before).
- **The last download folder is remembered** (`OperatorSettings`, new — same load/save shape as
  `KnownCallers`, one field). First download still asks once (there is nothing to remember yet);
  every one after uses the same folder silently. Applies to BOTH the button and double-click, since
  both call the same new `DownloadEntryAsync`. Deliberately DOWNLOAD-only — Decision E: an upload's
  destination is the remote folder just navigated to, which has no local-folder equivalent.
  Direct cost, stated rather than hidden: removing the per-download dialog also removes the
  per-download rename that `SaveFileDialog` used to offer.
- **Cut this pass: dragging a file out of the panel onto the real desktop.** Same simple-vs-virtual
  question as the clipboard (native OLE drag needs either the same eager-fetch-first approach or
  the same risky COM layer) — but Ctrl+C→Ctrl+V already delivers the same outcome with one
  keyboard gesture instead of a mouse gesture, so the incremental value is lower than the
  implementation risk this pass. Not forgotten — next, if wanted.

New tests: `OperatorSettingsTests` (4 tests — a fresh folder remembers nothing, what is set
survives a new instance exactly like a real restart, setting it again overwrites rather than
merges, a corrupted file is treated as nothing-remembered rather than a crash). `FilePanel`
itself stays outside the automated suite, same as before — no human has driven THIS part of it
either yet.

`dotnet build`: 0 warnings, 0 errors. `dotnet test`: 298 total (294 → 298, four new), 296–297
passed depending on the run — still only the same two pre-existing flaky tests, never a new name.

### Live two-copy test, launched by Conor's own request

Both copies started for the actual hands-on test (not just verification this time) and left
running: normal copy `163035814`, TEST COPY `175503444` (title-bar confirmed, same as the prior
verification — the identity is permanent per data folder, so the same two numbers reappear on
every launch, which is correct, not a stale cache). Walkthrough given: which number goes in
which window, panel contrast, the control checkbox disabled on the TEST COPY, one large download,
one ordinary upload, one `.exe` upload — `sessions.txt` to follow from Conor after.

## The first real two-machine test — Escape fixed and proven live, one belief corrected, the real cost of a transfer finally measured (2026-08-26)

Driven end to end on the actual `.222`/`.223` rig, Conor at the machine granting every consent,
Claude driving the session window via its own local UI (screenshots + UI Automation on `.223`'s
windows, never guessing at coordinates inside the remote picture except the few keyboard/click
exits this section proves). Each item run and verified alone, not chained — chaining is what made
the first `.exe` test upload the wrong file two sessions ago.

**1. Escape clears the pause band — confirmed on the real rig, not just in the test project.**
Screenshot before and after: band showing, Escape pressed, band gone, no other state change.

**2. Clicking the picture clears the band — confirmed.** A single click on a verified-empty
desktop point cleared it with no side effects.

**3. ⚠ CORRECTED BELIEF, not a bug: Enter in the path box does NOT clear the pause band, and
was never designed to.** Three people (Conor included) believed it did; it doesn't, and the code
was never written to make it. `FilePanel._path`'s Enter handler is a pure "go there" navigation
convenience — it calls `ShowFolderAsync`, nothing else — and was correctly documented as such at
the time it was written ("the one keyboard convenience that costs the remote machine nothing,
because forwarding has already stopped"). Confirmed live: typed a path, pressed Enter, the
folder changed and the band stayed exactly as it was. **Nothing needs fixing here** — recorded so
this does not resurface as a phantom regression in a future session that half-remembers today.

**4. The `.exe` upload — redone properly, and the log (not memory) answered the safety question.**
Uploaded `notepad.exe` into `.222`'s scratch folder (`C:\Users\Administrator\Desktop\test`,
created by Conor specifically so test files never land in a real folder again — the earlier round
had accidentally re-sent `ibdata1` into `C:\xampp\mysql\data`, a real database's data directory).
Claude lost track of the exact consent moment during the click sequence and could not itself
confirm whether a person clicked anything — so rather than guess, Conor sent `.222`'s own
`sessions.txt`, and the answer came from there, not from anyone's memory:
```
2026-08-26 10:24:35  PROGRAM  163 035 814  put the program "notepad.exe" (196 KB) into C:\Users\Administrator\Desktop\test
```
Traced against `HostFileService.AllowedToWriteAsync` (not just the log line alone): this was the
first upload of that connection, so the ask is unconditional regardless of `isProgram`
(`if (!_uploadAsked) { ... _uploadAllowed = await AskAsync(...) }`) — and even on a later upload
in the same folder, `isProgram` forces `AskAsync` again every time (`if (!isProgram) return true;`
— the negative case is the only early-return). There is no path from a received `PROGRAM` file to
that log line without `AskAsync` having returned true first. **The program-consent gate is intact
on the real rig, code-confirmed, not merely log-confirmed.** One asymmetry worth knowing, not a
bug: uploads log only the outcome ("put the program..."), unlike file *access*, which logs both
"asked" and "was allowed" as two separate lines — so an upload's own consent moment leaves no
separate "asked" line to point to, only its result.

**5. The stray second `notepad.exe`, explained from the log rather than guessed at.** 65 seconds
after the PROGRAM line above:
```
2026-08-26 10:25:40  SENT  163 035 814  copied "notepad.exe" (196 KB) from C:\Users\Administrator\Desktop\test
```
`SENT` is the download verb — the operator side asked for a copy of the file it had just
uploaded, 65 seconds later, matching Conor's own diagnosis exactly: the just-uploaded file
appeared in the refreshed listing, and Claude's own automation (most likely a stray Enter or a
mis-targeted click while checking status) triggered `DownloadEntryAsync` against it, landing it
in the remembered download folder on `.223`. Both stray copies removed — the one on `.223`
directly (`C:\Users\PC\Documents\notepad.exe`, deleted); the one inside `.222`'s own scratch
folder is left for Conor, since the file panel has no delete capability by design ("LOOK and COPY
OFF" only — see `HostFileService`'s own doc comment) and reaching it any other way means touching
the remote picture for a cleanup task that does not need that risk.

**6. The real number this whole test was for: 76 MB in under a second told us nothing, because
that rig is a LAN.** Turned on "Pretend the connection is slow (testing only)" → **600 Kbit/s**
(the slowest option), downloaded `ibtmp1` (12 MB) from `C:\xampp\mysql\data`, read the technical
panel's live numbers directly, before/during/after, on the real rig:

| | fps | latency | throughput | quality |
|---|---|---|---|---|
| Before | 17 | 3 ms | — | 95 |
| During (two samples, ~2 min apart, stable) | 2 | 353–354 ms | ~27 KB/s | 60 |
| After | 7 (climbing) | 3 ms | — | recovering |

Adaptive level hit 9/9 (its floor) during the transfer; send-stage time alone went from 0.1 ms to
**330–360 ms**; queueing built to 150–300 ms over budget. The transfer itself only achieved
**~27 KB/s of the nominal 75 KB/s link** — the video stream and the file are both drawing from the
same `LinkLimiter`-capped pipe with no priority between them, so neither wins: the picture
degrades to worse than useless (353 ms latency, 2 fps) AND the file still takes 7 minutes for
12 MB instead of the ~2.9 minutes the link could carry if it weren't sharing with video. Confirmed
on disk: the file landed at exactly 12,582,912 bytes in `C:\Users\PC\Documents`.

**Conor's read of this, recorded because it reframes the fix:** this is not "a transfer slows the
picture," which was already assumed. It is that **both lose at once** — remote control is already
unusable during a transfer (proven, not estimated), so there is nothing left to protect by
splitting bandwidth evenly, and doing so anyway costs the file real time for no benefit to an
operator who cannot use the screen either way.

**Proposed fix (numbers given to Conor, not yet built — his instruction: answer the log question
first, propose the fix with numbers, do not write code yet):** while a file transfer is active
(the same `_transferBusy`/`SessionRecorder.BeginTransfer` signal already wired for the
before/during/after measurement above), deliberately cut the video down to an occasional
keepalive rather than letting the adaptive ladder keep fighting for a fair share — reserve roughly
90% of the link for the file, ~10% for a still-alive frame every few seconds — and say so plainly
in the technical view: "Screen paused while the file transfers — about N minutes left," N
estimated from remaining bytes over the transfer's own just-measured rate. Projected from today's
numbers: ~90% of 75 KB/s ≈ 67 KB/s for the file, cutting the 12 MB / 7-minute transfer to roughly
**~3 minutes** — about **2.3× faster** — on the exact link just measured, for a picture that was
already unusable and is now honestly described instead of silently starved. Not yet built.

## The 2.3× prediction, investigated before being trusted — the algorithmic layer is NOT the cause (2026-08-26)

Conor's instruction: find where the missing ~48 KB/s (75 KB/s cap, file only got ~27) actually went
before promising a fix's size — "or you will ship a prediction instead of a fix." Three real,
throwaway tests (not kept — the finding is recorded here instead), each against the ACTUAL
production classes, nothing reimplemented:

1. **File chunk-sending alone** (`HostFileService`'s real loop logic, real `BandwidthGovernor`, real
   `LinkLimiter` at 600 Kbit/s, `Stream.Null` as the wire) — a 10-second run measured 124 KB/s,
   ABOVE the 75 KB/s cap. Explained, not ignored: `LinkLimiter`'s 512 KB buffer absorbs ~6.8 s as a
   free burst, and 10 s never leaves that burst. Extended to 60 s, measuring only the last 40 s
   (genuine steady state): **73.2 KB/s — 97.6% of the 75 KB/s cap.** The chunk-sizing/rate-estimate
   mechanism is not the problem in isolation.
2. **File + a simulated 2 fps video** contending for the same `MessageChannel` (same shared send
   lock the file chunks use) — steady state: **file 64.8 KB/s + video 8.6 KB/s = 73.4 KB/s
   combined**, file keeping the large majority of the link. Contention with video, modelled this
   way, is not the problem either.
3. **Same scenario over a REAL TCP loopback socket**, with a real receive loop draining the other
   end (ruling out "the OS socket buffer alone absorbed it") — steady state: **file 64.4 KB/s +
   video 8.6 KB/s = 73.0 KB/s combined**, bytes sent matching bytes received exactly. Same result.

**None of the three reproduce the real rig's ~27 KB/s.** This rules out the three most obvious
suspects — `FileChunkSize`'s rate-estimate correction, the shared send-lock serialising file
against video, and anything socket-buffer-specific — with real measurements, not by reading the
code and trusting it. What none of these three tests model is real DXGI capture cost: the actual
host loop spends real wall-clock time inside `AcquireNextFrame`'s blocking wait every cycle, which
these tests replace with a fixed 3000-byte dummy frame and nothing else. **This lines up with
Conor's own PART 3 finding below** — the logged "capture" stage reads 39–231 ms on the same
machine in one day, when the project measured the true figure at 0.16 ms long ago — which is
exactly the shape of a REAL, variable, thread-consuming wait, not simulated work. The most likely
remaining suspect is that this blocking wait, competing for a small thread pool with the file
loop's own send/read continuations, is where the missing capacity actually goes — untested here,
because reproducing real DXGI timing outside the live rig is not cheap, and the live rig is what
Conor is already about to remeasure.

**Consequence for the fix:** the DIRECTION (prioritise the file, cut video back hard during a
transfer, tell the operator plainly) is unaffected by any of this — it still directly targets the
one real, already-confirmed contention point (the shared channel), and cutting video's own share
of that contention can only help, whichever exact mechanism is eating the rest. But **the 2.3×
number is retracted as a promise.** It was arithmetic on an assumption ("90% of 75 KB/s") that
three real tests now show does not describe what's actually happening on the rig. The real number
will come from remeasuring after the fix, on the same 600 Kbit/s setting, before/during/after —
exactly as Conor asked, and not before.

## The "27 KB/s" reading was never the file — it was video, discovered by grep (2026-08-27)

Before trusting the 27 KB/s figure any further, Conor asked for the one number that was missing:
the TOTAL outgoing rate at the same moment, so the two possible worlds (pipe full, ~73 KB/s total,
video eating the rest — vs. pipe never full, ~35 KB/s total, bottleneck elsewhere) could be told
apart. Checking where that 27 KB/s figure actually came from, before remeasuring anything, found
the real problem: **it was never a file measurement at all.**

`HostServer.OutgoingMeter` — the only source of the technical view's "Outgoing: X KB/s" line — is
fed from exactly one call site, `OutgoingMeter.Record(1, bytes.Length)` inside `FrameLoopAsync`,
the VIDEO frame send loop. Grepped, not assumed: it appears nowhere else in the file. So every
"Outgoing" figure this project has ever reported during a file transfer, including the 27 KB/s
quoted to Conor, was the video rate — the file's own throughput has never been measured at any
point in this project. This is exactly the gap Conor's message identified before any remeasuring
was attempted.

**Fix (instrumentation only, no behavior change — commit `e1f558c`):** added `HostServer.FileMeter`,
a second `RateMeter` fed only from `HostFileService`'s two chunk loops (`StreamFileAsync`'s send
loop, `WriteChunksAsync`'s write loop, via a new `fileBytesTransferred` callback threaded through
the same optional-parameter pattern as the existing `transferStarted`/`transferEnded` callbacks).
Also added `HostServer.LastTransferStartedUtc`/`LastTransferEndedUtc`, stamped at the exact moment
those existing transfer callbacks fire — the same code-level boundary `SessionRecorder` already
uses — not from a UI click or a `FolderBrowserDialog` that can sit open and unnoticed for minutes
first (Conor's second doubt: the earlier 12 MB / 7-minute ≈ 27 KB/s arithmetic may also have dead
dialog time sitting inside its divisor; these are now real, code-level timestamps instead). The
technical view's file-transfer line now reads `file X KB/s · video Y KB/s · total Z KB/s`, live
while a transfer is active, plus the transfer's true start, end and duration once it completes.

Build: 0 warnings, 0 errors. Tests: 299/300 (`A_reply_for_a_request_the_operator_has_moved_on_from_is_dropped`
failed this run; `A_PROGRAM_is_asked_about_by_name_every_single_time` passed — matches the two
documented pre-existing flaky tests, only one tripped this time). Published build's embedded
version `e1f558cb8ef5fd2fb1e06c07c441723075863131` confirmed equal to `git rev-parse HEAD`.

**Not yet done:** the actual clean 600 Kbit/s measurement run Conor asked for. This instrumentation
makes that run possible; it does not replace it. No fix has been built yet — Conor said explicitly
not to build the bandwidth-priority fix until the measurement settles which of the two worlds this
is.

**Noted for later, not acted on now (Conor's instruction):** `LinkLimiter`'s 512 KB burst buffer
means any single file transfer under roughly half a megabyte is never throttled at all — it
completes entirely inside the free burst. Harmless, and probably a reasonable real-world default,
but it means small-file transfers will always look instant in testing and can never be used to
reproduce a slow-link problem; any future slow-link test must use a file safely larger than 512 KB.

## The clean 600 Kbit/s run — the pipe was full, the file was never starved to the extent believed (2026-08-27)

Driven end-to-end on the live rig (`.222` = Host, `.223` = Viewer, both consents already given),
using the new instrumentation from commit `e1f558c`. `.222`'s own "Pretend the connection is slow"
control was set to 600 Kbit/s, then `AnyDesk.exe` (4,021,320 bytes — comfortably over the 512 KB
burst floor) was downloaded from Host to Viewer with nothing else touching either machine during
the transfer.

**Numbers, read straight from `HostServer`'s new instrumentation, not the old video-only meter:**
- Code-level start/end: `03:10:42.760` to `03:11:55.022` — **72.262 s**, timestamped at
  `HostFileService`'s own transfer callbacks, not from any click or dialog.
- Sample at 17.4 s: **file 48.0 KB/s · video 13.8 KB/s · total 61.9 KB/s**.
- Sample at 61.8 s: **file 48.0 KB/s · video 37.6 KB/s · total 85.6 KB/s**.
- Full-transfer average file rate: 3,927 KB / 72.262 s ≈ **54.3 KB/s**.
- The 600 Kbit/s cap is ≈73–75 KB/s.

**Verdict — neither of the two worlds Conor posed, a real third case in between:** total (62–86
KB/s) straddles and briefly exceeds the ~73–75 KB/s cap, so **the pipe was full** — not the
"~35 KB/s, never full" alternative. But the file held a healthy majority of that full pipe (48 of
62–86 KB/s, roughly 56–77%) **the entire time, with no fix built yet** — it was never starved to
a small fraction the way the earlier (now-known-mismeasured) "27 KB/s" reading implied. Set
against the isolated no-video baseline from the investigation above (file alone: 73.2 KB/s), video
contention cost the file about a third of its throughput (73.2 → 48.0 KB/s) — a real, moderate
cost, not the dramatic one-sided starvation the original diagnosis assumed. **Consequence: "video
is starving the file" was overstated. A priority fix is not solving a problem that doesn't exist,
but the size of the win it would buy is far smaller than the retracted 2.3× framing suggested.**
Left for Conor to approve, redirect, or drop, per his own instruction.

**Caveats, named per charter rule 12 rather than left implicit:**
- `FileMeter` and `OutgoingMeter` are two independently-windowed 1-second rolling averages, not one
  atomic reading of the same wire — summing them can transiently read above the true instantaneous
  cap, which is the most likely explanation for the 85.6 KB/s sample.
- The "video" figure is not a clean idle baseline: `.222`'s own Host technical-view window had to
  stay open and visible for the whole run, since it was the only channel available to read this
  instrumentation at all (no RDP/credentialed access to `.222` existed at the time — see below).
  Its own live-updating labels repaint a small screen region every reading, and the post-transfer
  idle sample (video ≈25–33 KB/s with the file at 0) shows that alone is a non-trivial share of the
  "video" number. This is the same measurement method the project has used throughout (the
  standard procedure reads counters off the host's own open window) — not a new problem, but worth
  naming rather than presenting the video figure as if nothing but the transfer produced it.

**How this was driven, for the record:** no RDP session, saved credential, or WinRM trust to `.222`
existed at the time, so `.222`'s own "Pretend the connection is slow" control — deliberately
host-local, unreachable by protocol message from an operator by design (see `HostServer.cs`) — was
set using FlashDesk's own consented remote mouse/keyboard control (already granted for this
session), reading the result back through the video feed. Link throttle was reset to "No — send as
fast as it can" on `.222` afterward. A 3.8 MB test copy was left at `.223`'s
`C:\Users\PC\Documents\AnyDesk.exe`; deleting it was blocked by the session's permission classifier
and is left for Conor.

## The clipboard paste was RDP/VMware, not FlashDesk — no bridge exists in the code (2026-08-27)

Checked before trusting the observed "copy on `.222`, paste on `.223`" as evidence the clipboard
feature works, per Conor's explicit instruction not to assume it. Grepped the entire `src` tree:
**zero clipboard-related code in `Shared` (no wire-protocol message for it at all), and on the Host
side nothing but the unrelated "Copy" button that copies the operator's own FlashDesk ID text.**
The only clipboard code anywhere is `FilePanel.cs` (Viewer project) — `Ctrl+C`/`Ctrl+V` inside the
file panel, which downloads a selected remote file to a local temp folder and sets a
`FileDropList` on the **operator's own local OS clipboard**, and reads that same local clipboard
to start an upload. It never touches the host's clipboard, and there is no message type carrying
clipboard content across the wire in any direction.

**Conclusion: there is no clipboard bridge in FlashDesk today — not partially built, not one line
of it.** What Conor observed was the remote-access layer's own clipboard sharing between `.222` and
`.223` (RDP and/or VMware Tools, whichever actually connects them), one level below FlashDesk
entirely — exactly his own suspicion, confirmed rather than assumed. This rig cannot distinguish a
working bridge from that sharing for the simple reason that there is no bridge to distinguish yet.
**When a real bridge is eventually built,** proving it independent of the remote-access layer will
need either: disabling clipboard redirection on whatever channel reaches `.222` (RDP's own
clipboard-redirection setting, or VMware Tools' shared-clipboard setting) and re-testing, or testing
between two machines with no shared remote-console clipboard path between them at all.

**Also recorded so nobody re-derives it: the "two machines, two clipboards" instinct is wrong.**
Conor's own first assumption — that `.222` and `.223` each have their own separate clipboard, so a
paste succeeding on one must mean SOMETHING bridged it — was the mistake underneath the mistake.
RDP shares one clipboard across the session by default (and VMware Tools' guest-host sharing does
the same thing at a different layer), so two machines reached that way are not two clipboards to
begin with. Do not treat "it crossed two machines" as evidence of a bridge on its own; check what
remote-access layer connects them first.

## Chasing the pinned 48.0 KB/s — chunk sizer dismissed, LinkLimiter cleared, governor fix built (2026-08-27)

Before approving or dropping the bandwidth-priority fix, Conor asked for one more thing chased with
numbers: the file sat at EXACTLY 48.0 KB/s at two points in the clean 600 Kbit/s run above, 44
seconds apart, and "twice to one decimal is not coincidence." His first suspicion: `FileChunkSize`
sizes each chunk from `BandwidthGovernor._rateEstimate` divided by 3, and that estimate is a SHARED
EWMA fed by both `OnBulkSent` (file) and `OnFrameSent` (video) — so with video in the mix, the rate
it measures might not be the rate it thinks it is, sizing chunks wrong. Also asked, quickly: is
`LinkLimiter` actually enforcing 600 Kbit/s, or leaking?

Three throwaway tests (not kept — `ChunkSizeAndLimiterInvestigation.cs`, deleted once these findings
were captured), all against the real production classes:

1. **Chunk-sizer hypothesis: DISMISSED, by direct computation.** `FileChunkSize.ForMeasuredRate` is
   floor-clamped to the 16 KB minimum for the ENTIRE plausible range a 600 Kbit/s link (75,000 B/s
   true) could produce — confirmed by sweeping the estimate from 0.3× to 4× the true rate. The chunk
   size only starts growing past 16 KB above ≈245,760 B/s, more than 3.3× the true rate. So even a
   badly video-contaminated estimate cannot be what pinned the file — the chunk size literally cannot
   move at this link speed, contaminated or not.
2. **LinkLimiter: cleared, not leaking.** A first black-box 30-second run read 92,378 B/s against a
   75,000 B/s cap (23% over) — alarming on its own. Instrumenting the exact same leaky-bucket
   arithmetic in the open (not the class's black box) and isolating the POST-BURST region — i.e.
   excluding the documented, intentional 512 KB free-burst allowance (≈7 s worth at this rate) —
   gave **74,977 B/s against the 75,000 B/s cap: a ratio of 1.000.** The earlier "23% over" reading
   was entirely the burst diluting a short window's average, not a defect; the burst is designed
   behavior (see `LinkLimiter`'s own doc comment, "a stand-in for the buffer inside a home router"),
   and a longer window shows it enforcing essentially exactly.
3. **What the 48.0 KB/s pin most likely is, evidenced but not reproduced bit-for-bit:** with chunk
   size fixed at the 16 KB floor and the limiter enforcing the combined 600 Kbit/s cap almost
   exactly, a file loop sending fixed-size chunks back-to-back against a video loop attempting a send
   on a fixed ~500 ms cadence, both serialized through ONE send lock, is a fundamentally periodic
   system — it is expected to settle into a stable, repeatable division of the link, which two 1-second
   snapshots 44 seconds apart would read as identical to one decimal. This is offered as the
   best-supported explanation, not proven to the same standard as findings 1 and 2 above — reproducing
   the exact 48.0 figure in isolation would need a much longer contention simulation than was run here.

**Consequence for the earlier "pipe full, file majority share" verdict: unchanged.** Neither dismissal
threatens it — if anything, a confirmed-non-leaking limiter makes the "two independently-windowed
meters" explanation for the 85.6 KB/s sample (straddling the cap) more credible, not less.

## The same shape elsewhere — a survey, not a fix (2026-08-27)

Conor's framing: the governor's round-trip signal is queued behind the very traffic it reports on,
so it cannot arrive until a transfer ends — "that is not a file-transfer bug, it is a shape: any
large send blinds the thing watching it." Asked to look for the same shape elsewhere before fixing
anything else. Findings, not yet acted on:

**Confirmed instances of the shape (a decision arrives too late because the signal it needs is
queued behind the thing it is supposed to be judging):**
- `BandwidthGovernor.OnRoundTripReported` — the original finding. Now addressed by
  `OnBulkTransferStarted`/`OnBulkTransferEnded`.
- **Mouse and keyboard input, during an UPLOAD — newly found, not yet touched.** `ViewerClient`'s
  input-send loop and `ViewerFileClient`'s upload-chunk loop both call `SendAsync` on the exact same
  `MessageChannel` instance (confirmed by reading the constructor wiring, not assumed) — one
  semaphore, shared. So while the operator is sending a file TO the client, their own mouse and
  keyboard commands to the remote machine queue behind their own upload's chunks. This is the same
  root cause as the governor bug, on the opposite side of the connection, and arguably more
  user-visible than a status number: it would read as the remote machine lagging or ignoring input.
  Not fixed — Conor asked for the survey only.

**Adjacent, but not the same shape (real-time, not stale — flagged so it is not conflated with the
above):** `ViewerClient.LastLatencyMs`, shown in the operator's own status bar as "latency X ms".
This is a direct, live measurement (Stopwatch from Ping-sent to Pong-received), not a relayed
one-cycle-old value like the governor's round trip, so it is not "blinded until the cause ends" —
it updates as soon as some Pong gets through. But a spike during a transfer means "our own queue was
busy," not genuine network latency, and nothing on screen says so — the same family of mislabeling
already flagged for "capture ms" (PART 3, pending). A number, not a blindness.

**Checked and found NOT vulnerable — recorded so nobody re-checks them:**
- Relay-level liveness (`RelaySessions.cs`): deliberately socket-drop-based, not round-trip-timed —
  "There is no separate heartbeat to go stale... if a host's socket drops it leaves this dictionary
  immediately." The right pattern, already in use there.
- `ViewerClient.IsConnected`: set false only when the receive loop itself exits on a real socket
  failure, never inferred from a timeout or a missed round trip.
- The four consent-dialog countdowns (`ConsentDialog`, `FileConsentDialog`, `IncomingFileDialog`,
  `ReplaceFileDialog`): local wall-clock timers, do not depend on receiving anything over the wire.

**Open, not confirmed either way:** whether `OnFrameSent`'s own send-timing evidence can be
STARVED (not just delayed) if file chunks so dominate the shared lock that video frames rarely get
a turn to send at all during a transfer — a different variant of the same one-lock-no-fairness root
cause. Not measured.

**⚠ THE INPUT-QUEUE FINDING IS THE MOST SERIOUS THING FOUND IN THIS WHOLE INVESTIGATION (Conor,
2026-08-27).** Not a diagnostic curiosity: while the operator sends a file TO the client, the
operator's own mouse and keyboard commands to the client's machine queue behind the upload's own
chunks, silently, for the length of the transfer. Two follow-up questions, answered by reading the
code rather than assumed, before anything is fixed:

1. **Does it affect the CLIENT's own mouse and keyboard on their own machine? No.** Their physical
   input never touches FlashDesk's network code at all — it goes directly into Windows' own input
   stack on their machine, completely independent of any `MessageChannel`, any socket, any queue.
   The client can use their own computer throughout, uninterrupted. What is lost is only the
   OPERATOR's ability to reach them — the person on the other end is not locked out of their own
   machine, but the person meant to be helping them effectively is, without either side being told.
2. **Does the same queue carry consent replies? Only briefly, and not by the transfer it is
   itself about.** Read `HostFileService.ReceiveFileAsync` directly: every consent step
   (`AllowedToWriteAsync`, `_askReplace`) and its reply (`SendReplyAsync` → `FileSendReply`, over
   the same shared `_channel`) happens BEFORE the `Upload`/chunk channel is even created — a
   transfer's own bytes cannot delay the very question that decides whether it happens. But the
   reply still shares the one send lock with everything else on the connection, so if a VIDEO FRAME
   happens to be mid-send (blocked in `LinkLimiter`) at that exact instant, the reply waits behind
   it — bounded to roughly one frame's send time (measured up to ~300 ms on the 600 Kbit/s run
   above), not indefinite, and nothing like the full length of a transfer. A consent dialog would
   not read as ignored; a slow link could make it feel one beat behind.

**Two things found alongside this, not asked for but load-bearing:**
- **The problem is UPLOAD-specific, not download-specific — a real asymmetry.** Input travels
  viewer→host. During an UPLOAD, the viewer's own outbound queue carries both the operator's input
  AND the upload's own chunks — contention. During a DOWNLOAD, chunks travel host→viewer, the
  opposite direction from input, so a download does not delay the operator's own input at all.
  "Sending a file" is the dangerous direction; "getting one" is not.
- **Input is delayed, not dropped — and what queues eventually plays back onto a screen that has
  moved on.** `ViewerClient`'s `_inputQueue` is unbounded (`Channel.CreateUnbounded`), so nothing
  drops silently while a transfer runs. But every mouse move and click from that whole window is
  still queued and WILL eventually be sent, one at a time, once the shared lock frees up — a person
  moving a mouse for seven minutes queues thousands of stale events that then drain onto whatever
  is on screen by the time they arrive, not what was there when they were made. This is worse than
  "no response, then normal again" — it is "no response, then a burst of commands aimed at a screen
  that no longer matches them."
- **Related, found while re-checking `OnBulkTransferStarted`'s own timing — Conor called this a bug
  and it is FIXED (commit `d1b6b59`).** The proactive step-down fired from `_transferStarted`, which
  `HostFileService` raised the moment `_transferBusy` was claimed — BEFORE `IncomingFileDialog` /
  `ReplaceFileDialog` were even shown, let alone answered. On a slow link the picture dipped for as
  long as the client took to decide (`IncomingFileDialog.TimeoutSeconds = 30`), before a single byte
  had moved and before any real contention existed to justify it — punishing the client for taking
  their time. `Governor.OnBulkTransferStarted()` now fires from inside `HostFileService` itself, once,
  on the first chunk actually sent (`StreamFileAsync`) or received (`WriteChunksAsync`), guarded by a
  per-call flag; `HostServer`'s `transferStarted` callback no longer calls it. Same principle already
  applied to the Viewer's own ETA tracking: the waiting-for-an-answer phase is not transfer time.
  `_recorder.BeginTransfer()`/`LastTransferStartedUtc` were deliberately left at claim-time, unchanged
  — only the ladder timing was in scope for this fix; whether the session-log timestamp and the
  derived KB/s figures have the same "claim time vs first byte" gap is a separate, open question,
  not acted on.

**The input-queue discard/coalesce design itself is NOT fixed, per Conor's explicit instruction**
("do not fix it yet"). Recorded here so the shape, the scope (upload-only), and the severity
(delayed-then-stale-replay, not merely delayed) are not re-derived later. See "Stale input — design
proposal, not built" below for the approach put to Conor.

## Risk 2 decision — kept as is (Conor, 2026-08-27)

Conor's call, since the finding above left it to him: keep `OnBulkTransferStarted`'s current
behaviour. An unmeasured link being treated the same as a known-slow one, worst case landing on
level 2 (12 fps, quality 95) for a few seconds, is an acceptable price for not guessing — and
`LinkLimiter`'s 512 KB burst figure was correctly refused as a size filter, since it describes the
TEST harness's simulated router buffer, not any real-world guarantee. No code change from this.

## Stale input — design proposal, not built (2026-08-27)

Conor's correction: "input is queued, not lost" is worse, not better — thousands of stale clicks
and moves from a long upload will eventually fire onto a screen that has moved on, which is more
dangerous than input simply being delayed. Proposed approach, not built — Conor asked to see the
approach before anything is written:

- **Mouse MOVE events (no button change): latest-only, per Conor's own conclusion.** A position
  older than a moment is worthless once stale — only the newest matters. Replace the single
  unbounded FIFO the input loop currently drains with a coalescing slot for pure movement: a new
  move overwrites whatever move is still waiting, never queues behind it. Nothing here needs a
  judgment call; this is what "only the newest matters" already decides.
- **Clicks and keystrokes: NOT decided here — three options put to Conor, not chosen unilaterally,**
  because (his own reasoning) a keystroke may be part of something the person typed, and dropping it
  silently could corrupt that.
  1. *Discard past a threshold too* (e.g. ~2–3 s old) — simplest, safest against a stale click
     landing somewhere unintended, but risks losing part of something someone was mid-typing if the
     delay ran long.
  2. *Never drop, but warn afterward* — deliver everything queued, then tell the operator plainly
     once the backlog clears ("N actions were sent late — check what happened"), so a human notices
     rather than assumes silence meant nothing happened.
  3. *Stop ACCEPTING new input once the backlog passes a small bound, instead of ever building a
     large one* — bounds the problem at the source rather than cleaning up after it. Pairs naturally
     with the existing "Screen is slowed while the file transfers" message (already built): extend it
     to also say control is paused, the moment it actually is, so the operator is told in real time
     instead of clicking into what they will otherwise read as a frozen picture and only learning
     why afterward.
  Leaning toward a combination of 3 (bound the queue, tell the operator immediately) with 2 for
  whatever small amount still queues before the bound is hit — but this is a recommendation, not a
  decision made without Conor.

## The latency line joins the mislabelled-number list (Conor, 2026-08-27)

`ViewerClient.LastLatencyMs`, shown as "latency X ms" in the operator's own status bar, measures how
long OUR OWN queue was busy before a Ping got out and a Pong came back — not genuine network
latency to a person reading the label. Filed alongside "capture ms" (PART 3, pending) as the same
class of defect charter rule 12 exists to catch: a real, reproducible number, answering a different
question than the one its label asks. Not fixed now, per instruction — recorded so it is fixed
together with "capture ms" rather than separately, when that work is picked up.

## Answering the two risks in the governor fix, before testing (2026-08-27)

**Q1 — what raises the ladder back up, and how fast?** Originally: nothing new — only the
pre-existing `OnFrameSent` quiet-window climb (3 consecutive 500 ms windows with low send-overrun
AND low queue delay), which still partly depends on the same round-trip signal for its
"queue delay is clear" half. Real risk, as Conor suspected: the FIRST round trip reported right
after a transfer ends can still be the stale, transfer-inflated kind, holding recovery hostage for
one extra cycle. **Fixed, not just explained: `BandwidthGovernor.OnBulkTransferEnded()` (commit
`0b97b9c`)** clears `_queueDelayMs` and `_quietWindows` the moment a transfer ends, so the very next
real round trip (due within about one Ping interval, ~1 s) is judged on its own evidence instead of
the transfer's leftover reading. It deliberately does NOT step the ladder up itself — recovery still
has to earn it through real quiet evidence, the same "up slow" principle the class already uses
everywhere else. Expected recovery order of magnitude: roughly one Ping interval plus up to three
500 ms decision windows ≈ 2.5–4 s after a transfer ends, assuming video is sending something to
accumulate evidence from — not yet measured live.

**Q2 — what counts as "fast", and is there a flicker risk on a fast link with a short transfer?**
Honest gap, not yet closed: `KnownFastBytesPerSecond = 3 MB/s`, and the check is
`_rateEstimate > 0 && _rateEstimate >= 3 MB/s`. This correctly SKIPS the proactive step-down when the
link is already MEASURED fast. It does NOT skip it when the rate is simply unknown (`_rateEstimate`
still 0 — e.g. the very first transfer of a session before video has measured anything) — an unmeasured
fast LAN and an unmeasured slow link are treated identically today. So a first, tiny, sub-second
transfer on a fast but not-yet-measured link WILL trigger the proactive step-down. Bounded, though:
`OnBulkTransferStarted` steps down by 2 LEVELS, not to the floor — from level 0 that lands on level 2
(12 fps, quality 95, per the ladder table above), which is a mild dip, not a collapse, for at most the
~2.5–4 s recovery window above. Considered and deliberately NOT built without Conor's steer: gating
on file SIZE using LinkLimiter's 512 KB burst figure would be wrong — that number describes the TEST
harness's simulated router buffer, not any real-world guarantee, and a real slow uplink has no free
allowance at all. A principled size/duration gate is possible but needs a threshold based on
real-world slow-link figures (CLAUDE.md's ~40–600 KB/s home-upload range), not reused from the test
instrument. Left for Conor to decide whether the bounded worst case (one mild, few-second dip on an
unmeasured-but-actually-fast link) is acceptable as is.

**Built, since Conor approved these as cheap and worth doing once the bandwidth split was dropped**
(commit `239e5e3`):
- **The honest transfer message.** `FilePanel` now tracks each transfer's own average rate and time
  remaining (starting on the first real progress sample, so an upload's "waiting for them to answer"
  phase before consent is never counted as transfer time). `SessionWindow`'s status line shows
  "Screen is slowed while the file transfers — about N minutes left" in place of the fps/latency
  line while a transfer is active. Deliberately not "paused" — the picture keeps updating, just
  slowly, and saying otherwise would be a lie the operator could catch by looking at it.
- **The governor no longer waits for a signal that cannot arrive in time.** Investigating why an
  earlier real transfer left quality unchanged for its entire length and only stepped down once the
  transfer was already over: `OnRoundTripReported`'s round trip is carried in the viewer's Ping as a
  ONE-CYCLE-OLD measurement, and the Host's own Pong reply is queued behind the very file chunks it
  is trying to report on, through the same send lock — so the signal that is supposed to catch this
  structurally cannot arrive before the file transfer that caused it has mostly or fully finished.
  `BandwidthGovernor.OnBulkTransferStarted()` now steps the ladder down the moment a transfer begins
  (skipped when the link is already known fast, so a session that never needed it is never touched),
  wired from the same `transferStarted` callback `HostServer` already uses for the new instrumentation
  above. Not yet re-verified live against a real transfer — the next live test should check the
  technical view no longer shows quality 95 while a slow-link transfer is in progress.
- **The session log now carries the file/video split, not just the screen** (commit `0b97b9c`),
  per "record all of it into the session log... so the next measurement does not depend on someone
  watching a window that pollutes it": `SessionRecorder` buckets file and video bytes separately per
  second (fed from the same point `HostServer.FileMeter` is), and each transfer's before/during/after
  line in the client's own log now states file KB/s, video KB/s, and total, the precise UTC start/end
  timestamps, and a computed "recovered after Ns" line — all read from history already recorded,
  the same principle `AppendTransfers` already used for fps/quality.

## Live run on `ffe7b73` — the step-down fix confirmed, and a real gap in the test tooling found (2026-08-27)

Driven end-to-end on `.222`↔`.223`, both on `ffe7b73401e7e95acacdbafab8e4855ea9ed16a7`. `.223`
confirmed exact (the running process's own file, matched to `git rev-parse HEAD` directly). **`.222`
could not be confirmed to the same standard — recorded honestly, not glossed over:** the file panel
has no byte-exact size, hash, or version string anywhere in its UI, only a rounded-to-one-decimal MB
figure and a date. What was visible (`68.5 MB`, `2026-08-27`) is consistent with the correct build —
the local build rounds to the identical figure — but this is corroboration, not proof. Proceeded on
that plus Conor's own just-completed copy, per his call once told plainly. A real download+hash
compare would be the only way to close this gap completely; not done, would have cost the run's own
bandwidth budget for a check that circumstantial evidence already answered well enough.

**Quality drops DURING the transfer, not after — confirmed directly, this is the whole point of the
fix.** Downloaded `AnyDesk.exe` (3.8 MB) at 600 Kbit/s, hands-off. At 38.1 s into the transfer, the
technical view read `Adaptive: level 9/9 - 2 fps cap, quality 60` while `File transfer: ACTIVE` —
the floor, reached DURING, not after. Transfer ran `05:18:59.373` to `05:20:07.313` (67.9 s per the
now-first-byte-timed log line).

**Recovery: reached level 5/9 quickly, then stalled there — not full, and the likely cause is
already on record.** Sampled after transfer end (`05:20:07.313`): level 5/9 at +34.5 s, level 4/9 at
+56.5 s, level 5/9 again at +91.6 s, level 5/9 still at +137.3 s. Climbed from the floor to roughly
half the ladder within well under a minute, then never reached level 0 across the ~137 s observed —
consistent with, and most likely caused by, the same self-referential technical-view repaint cost
already documented (2026-08-27, "measurement hygiene" note): the window had to stay open to be read
at all, and its own live-updating labels are enough ongoing "activity" to keep tripping the
overrun/quiet-window check just short of full recovery. Not a new defect in `OnBulkTransferEnded`
itself — that fix does what it says (clears the stale reading so recovery isn't blocked by the
transfer's own tail) — this is the ceiling the OBSERVATION method itself imposes, the same limit
named honestly in the first live run.

**The message: appears, reads exactly as written, ETA order-of-magnitude right from one early
sample — not fully validated.** The viewer's status bar showed
`Screen is slowed while the file transfers — less than a minute left` within ~3 s of the transfer
starting. The transfer's real total was 67.9 s — a little over a minute, so that specific early
estimate was a mild underestimate at the moment it was shown. Only one ETA sample was taken, early;
whether the estimate corrected itself as more of the transfer's own average rate accumulated (the
design already accounts for this — see `FilePanel.Progress`) was not observed. Not wildly wrong, not
proven right either — said plainly rather than rounded up to a clean "confirmed."

**Session log: the wiring is confirmed by reading the code, the actual file was not read.**
`SessionRecorder.Report()` → `HostServer.SessionSummaryReady` → `MainForm` calls
`_sessionLog.Detail(report)` → appended to `sessions.txt` — but only once a session actually ends
(disconnects). The session stayed connected for the rest of the run at Conor's own direction, so
`sessions.txt` was not opened. What is claimed here is that the code path exists and was not touched
beyond the fields added this session — not that the file was read and checked.

### The input-queue test — the finding Conor cares most about, and a real testing gap found alongside it

**Major, unplanned discovery: `LinkLimiter` does not throttle uploads AT ALL.** Its own doc comment
says exactly this — "Reads pass straight through — only sends are held back" — and an upload is the
HOST *receiving*, not sending. "Pretend the connection is slow" only ever throttles the Host's own
outgoing traffic (video, Pong, downloads). Proven, not just read: a 200 MB upload finished in
21.8 s (~9.4 MB/s) and a 1 GB upload in 116.8 s (~9.4 MB/s) with 600 Kbit/s selected on `.222` the
entire time — no throttling effect whatsoever. **Consequence: there is currently no way to simulate
a slow uplink to test upload-side input contention.** Conor's worst-case scenario — a real client's
genuinely slow home upload, contended for minutes — could not be reproduced or measured today. What
was measured is real, but it is a lower bound, not the worst case.

**What WAS measured, at full unthrottled speed (~9.4 MB/s):** five distinct clicks sent in rapid
succession (`05:33:03.624`–`05:33:04.726`, all landing while a 1 GB upload was actively sending,
which ran until `05:33:30.952`) were confirmed delivered — cursor visibly at the last click's target
position — by `05:33:14.768`, roughly 10 s after the last of the five was sent, and well before the
upload itself finished. Real, non-instant delay, confirmed directly rather than only read in code —
but recovered in single-digit-to-low-double-digit seconds at this speed, nowhere near "stuck for the
length of a seven-minute transfer." That scenario needs a genuinely slow, contended channel to
reproduce, which today's tooling cannot provide for uploads.

**"Do the queued actions replay?" — confirmed by code, not by a captured frame sequence.**
`ViewerClient._inputQueue` is an unbounded `Channel`, drained strictly in order, nothing in the path
drops or reorders an event — so every one of the five clicks WOULD have been delivered in the order
sent. The final observation (cursor at the fifth/last click's position after all five had time to
arrive) is consistent with that. A second attempt at denser sampling (40 rapid moves) was mistimed —
sent 23 s after that particular upload had already finished — so it measured ordinary unthrottled
responsiveness, not in-transfer contention, and is not used as evidence here.

**One more thing found along the way, unrelated to the input-queue question:** the upload status
text (`"Waiting for them to answer — the question is on their screen now."`) never updates once the
transfer is actually moving bytes — it sits there, unchanged, for the whole upload, even though the
technical view's own `File transfer: ACTIVE` line confirms real progress underneath it. Minor, and a
different defect from the input-queue finding — an unattended status label, not a control-delivery
problem — but worth fixing whenever this screen is next touched.

**What this means for the stale-input design already agreed:** none of it changes the design Conor
already decided (mouse-move newest-only; clicks/keys pause-and-warn at a 1 s threshold). It does mean
that decision was made, correctly, on the STRUCTURAL evidence (the shared `MessageChannel` lock,
confirmed by reading the constructor wiring) rather than on a measured worst case — because the
worst case cannot be measured yet. If a real client's slow-uplink severity ever needs to be
quantified precisely, `LinkLimiter` (or a viewer-side equivalent) would need to start throttling
sends on the VIEWER's own outbound side, which does not exist today.

## Three fixes from the live-run debrief, before the stale-input work (2026-08-27)

Conor's read on the run: the governor fix is confirmed and that was the goal, but two things were
open, and one of them — uploads never being throttled — undoes the ability to test the thing he
cares about most. Ordered: fix the tool, then prove recovery without the polluting instrument, then
re-run the slow upload for real, then design stale-input against what is actually seen. Stale-input
still not started, per instruction.

**1. `LinkLimiter` now throttles reads too (commit `79fa3ea`).** The class's own doc comment used to
say "reads pass straight through — only sends are held back, which is the direction a home upload
actually limits" — backwards for the case that matters: an UPLOAD is the client's machine
*receiving*, i.e. reading on this side, and reads were never touched at all. Fixed with a second,
independent leaky bucket (`Bucket`, one per direction, sharing only the Stopwatch) — reads throttled
after the fact, since a read's byte count isn't known until it returns, unlike a write's. **Verified
with a throwaway test before trusting it** (`LinkLimiterReadThrottleInvestigation.cs`, deleted after
— per charter rule 11, a fix must be proven to work, not just proven to compile): a source stream
with zero delay of its own, read through a 600 Kbit/s `LinkLimiter`, landed at a post-burst rate of
75,589 B/s against a 75,000 B/s cap — **ratio 1.008**, matching the write side's own already-verified
figure (1.000) almost exactly. The FAIL side of rule 11 is the live run itself, already on record:
the unfixed code moved a 1 GB upload in 116.8 s (~9.4 MB/s) with 600 Kbit/s selected — real evidence
the old code did not throttle reads at all, not a hypothetical.

**2. The frozen upload status text is fixed (commit `79fa3ea`).** Conor's correction on calling this
minor: "It says the client has not answered when the client has — the same class of lie as
everything else we found." `FilePanel.Progress()` now takes an active-transfer status string and
sets it the moment real bytes are confirmed moving — `"Sending…"` for uploads (replacing the frozen
`"Waiting for them to answer…"`), `"Copying…"` for downloads (which previously showed nothing at all
during the transfer, not even a wrong answer).

**3. The build version is now readable, not inferred (commit `79fa3ea`).** The file panel exposes no
hash or version anywhere — the live run above had to proceed on a rounded-MB-and-date match, which
Conor called indirect and asked to be fixed properly. `MainForm`'s technical view now shows
`Version: <the same string PowerShell's VersionInfo.ProductVersion reads>`, read once at startup via
`AssemblyInformationalVersionAttribute`. Either side of a live test can now be confirmed by reading a
line on screen instead of comparing a rounded file size.

**Not yet done, next in the stated order:** prove the stalled recovery from the first live run is
real or is the technical-view-repaint artifact suspected — without the technical view open during
the observation, reading from the session log or the viewer's own side instead. Then re-run the
upload test with the NOW-real throttling in place and actually observe the input-blocking severity
on a genuinely slow channel, which the first run could not do. Stale-input design work does not start
until after both.

## Recovery diagnosis, live run on `cfe3eea` (2026-08-27) — two findings, one still open

A live run produced four file transfers in one session: #1 recovered to level 0 in 54s, #2 in 4s,
#3 and #4 never recovered — read from the technical view as "stuck at level 6/9" for 985s and 744s
respectively before the session ended. Conor's instruction: name the state that differs between #2
and #3 by reading the code, not by running more transfers.

**Finding 1 — recovery is all-or-nothing, with no partial credit.** `BandwidthGovernor.OnFrameSent`
requires **three consecutive** clean 500 ms windows (`QuietWindowsToRecover`) before giving one level
back. But `StepDown()` — called on a single over-budget frame, a single panic-slow send, or a single
round trip over `QueueHeavyMs`/`QueueSevereMs` — unconditionally zeroes `_quietWindows`
(`BandwidthGovernor.cs:410`). There is no decay, no partial credit: one bad half-second anywhere in
the count discards all progress and the three-window count restarts from zero. Going DOWN needs one
event; coming UP needs perfect silence for 1.5 straight seconds, repeated for every level owed. A
steady trickle on the channel — a blinking cursor, a clock tile redrawing, the technical view's own
repaint — never *slows* recovery under this design. It *stops* it, for as long as the trickle
continues. This is a real design asymmetry, confirmed by reading `OnFrameSent`/`StepDown` directly,
independent of what caused #3 and #4's specific stall.

**Finding 2 — the host's own rate estimate is upload-blind and never reset between transfers.**
`BandwidthGovernor._rateEstimate` (the EWMA behind the "send burst" reading and the over-budget
safety check) is fed from two places only: video frames (`OnFrameSent`) and DOWNLOAD chunks
(`OnBulkSent`, called from `HostFileService.StreamFileAsync`). It is **never** fed from an UPLOAD —
`WriteChunksAsync` (the host receiving a file) has no equivalent call anywhere in it. And it is never
reset between transfers, only at `Reset()` when a session begins. So if an earlier transfer or a
burst of video traffic pushes it high, and a later transfer in the same session is an upload, the
host has no way to learn from that upload that the link is actually slower than it believes — the
belief can only be corrected by traffic it structurally never observes. Recorded per Conor's
instruction regardless of whether it explains today's stall: a stale "the link is fast" reading that
only self-corrects via evidence it never receives is a bug waiting for its day.

**Still open — which world #3 was in.** Reading the log to settle "trickle vs. genuine silence"
during the stuck window turned up a THIRD finding first: closing the host window after the run left
**`sessions.txt` byte-for-byte unchanged** — same length (4202), same last-write time (2026-08-26
06:50:28) as before the run, even though the host process (`FlashDesk.exe`, PID 4580, running since
06:36:04 that morning) had by then exited with no Windows Application-log crash event recorded
against it. Traced the write path: `MainForm`'s `FormClosing` calls `HostServer.Dispose()` →
`Stop()` → `FinishSessionReport()` → `SessionSummaryReady` → `SessionLog.Detail()`, which should
have appended the full "how it went" block including the four transfers' before/during/after and
`RecoveryLine` verdicts. It did not fire, or fired and failed silently (`SessionLog.Append`'s
`catch { }` swallows any write failure on purpose, per its own comment, "a log that cannot be
written must never take the session down" — which also means a failure here leaves no trace
anywhere to explain itself). Both are real possibilities and neither is confirmed: whether the host
was closed by a path that skips `FormClosing` (a task-kill, a crash with no logged event, closing a
window that was not actually this `FormClosing`'s owner) versus `FormClosing` running but the append
failing quietly. **Needs one fact from Conor: exactly how the app was closed**, before this can be
narrowed further. Until then, the trickle-vs-silence question for #3 is unanswered — not "genuinely
silent and stuck" and not "confirmed trickle," but "the instrument meant to answer this did not
produce a reading at all." No code changed while chasing this.

## The session log's silent catch, fixed — a three-hour session cannot vanish again (2026-08-27)

Conor's own close (the X button — "nothing unusual, no Task Manager, no crash") lost a three-hour,
four-transfer session with zero trace. Since `FormClosing` definitely runs on that path, the cause is
either a broken link in the write chain or a swallowed exception in `SessionLog.Append`'s `catch { }`
— and since the failure left no trace anywhere, which one it actually was is now unrecoverable
forensic evidence. Built to Conor's exact order, so it cannot happen invisibly again:

1. **Write as you go.** `HostServer.SnapshotReport()`/`EmitCheckpoint()` produce the same "how it
   went" block `FinishSessionReport` writes at the true end, without ending anything —
   `SessionRecorder.Report` only reads and rolls its history forward, so this is safe to call
   repeatedly. `SessionLog.Checkpoint()` appends it as a clearly-marked, non-final block. Wired to
   fire (a) after **every file transfer ends** — the before/during figures and the recovery reading
   become worth having on disk the moment they exist — and (b) every **3 minutes** from the existing
   UI timer, as a plain dead-man's switch for a session with no transfers at all. Each transfer's
   history remains queryable this way at any point, not only at a clean close.
2. **The silent catch is gone.** `SessionLog.Append` now sets `LastWriteError`/`LastWriteErrorAtUtc`
   and raises `WriteFailed` on any exception, instead of doing nothing. The session still never stops
   over a log write failing — that part of the original reasoning was right — but the failure is now
   filed into `CaptureHealthLog` (the Desktop capture log, a second, independent file, not gated
   behind `FLASHDESK_CONFIG_DIR`) and shown live in the technical view (`Session log: writing
   normally` / `FAILING since HH:mm:ss — <message>`, self-correcting the moment writes succeed again).
3. **Coverage by close path, stated honestly rather than promised:**
   - **X button / Stop sharing** (both route through `HostServer.Stop()` → `FinishSessionReport()`):
     fully covered — the final block still writes, and checkpoints throughout the session are now a
     second safety net under it.
   - **Task Manager "End Task", a crash, a plain kill:** `FormClosing` does not reliably run on these.
     The final "how it went" block and the true `RecoveryLine` verdict are lost, same as before — but
     everything checkpointed before the kill is already on disk, because checkpoints are ordinary
     appends, not a buffer flushed at exit. Bounded loss: at most the last 3 minutes, or since the
     last transfer ended, whichever is more recent.
   - **The machine sleeping:** not actually a close — the process is merely suspended and resumes
     where it left off on wake. Nothing lost to sleep itself; only a genuine close/kill loses anything.
   - **What is NOT fixed:** the gap between the last checkpoint and an abrupt kill. This is a real,
     stated limit, not a hidden one — narrowing it further trades against turning sessions.txt into
     the "fourteen near-identical blocks" noise problem the original design was written to avoid.
4. **`dotnet build -c Release`: 0 errors, 0 warnings.** `dotnet test -c Release`: 298/300 passing.
   The 2 failures (`HostFileServiceTests.A_PROGRAM_is_asked_about_by_name_every_single_time`,
   `FileTransferEndToEndTests.A_reply_for_a_request_the_operator_has_moved_on_from_is_dropped`) were
   confirmed, by running the identical suite against the unmodified `cfe3eea` tree, to be **pre-existing
   and unrelated to this change** — both fail identically with no code touched, and the first passes
   reliably in isolation (5/5), which is the signature of test-parallelism/isolation flakiness in the
   suite itself, not a defect in this fix. Not investigated further; out of scope for what was asked.

Recovery (the three-consecutive-quiet-windows finding) is untouched and waits, per instruction.

## Close-chain trace, checkpoint readability, and a live proof (2026-08-27, same day)

Conor's follow-up: fixing the silent catch answers "did the write fail" but not "did every link in
the chain even get reached" — a break upstream of `Append` would look identical from outside. Asked
for both a trace through the chain and a live X-close demonstration, in one pass, kept small.

**Trace added, one `Health.Note` line per link, all seven:** `MainForm.FormClosing` (1) →
`HostServer.Dispose` (2) → `Stop` (3) → `FinishSessionReport` (4, and explicitly distinguishes "no
recorder — nothing to report" from a break, since those look identical without the note) →
`SessionSummaryReady` reaching MainForm's own subscriber (5, logged from INSIDE the subscriber, not
the publisher — invoking a null-checked event proves nothing about whether anyone was listening) →
`SessionLog.Detail` being called (6) → `Append` actually succeeding (7, read from `LastWriteError`
immediately after, since it is set synchronously inside `Append`).

**Live proof, run for real, today:** built Release, launched `FlashDesk.exe`, waited 4s (no viewer —
none was needed for this half of the question), closed via `CloseMainWindow()` (the exact WM_CLOSE a
person's X click sends), confirmed the process exited within 8s. The capture log shows:
```
CLOSE-TRACE  1/7  MainForm.FormClosing reached
CLOSE-TRACE  2/7  Dispose() reached
CLOSE-TRACE  3/7  Stop() reached
CLOSE-TRACE  4/7  FinishSessionReport() reached
CLOSE-TRACE  4/7  no recorder — nothing to report, stopping here on purpose
```
All four links that fire on EVERY close (session or not) are proven reached, live, today — this was
previously untested territory; nothing before this touched the close path at all. `sessions.txt` was
correctly untouched (nothing to report), confirmed by its unchanged timestamp. Links 5–7 need an
actual connected session to exercise for real, which needs a human to answer the consent dialog —
covered instead by `SessionLogTests` (below), which drives the exact same
`SessionSummaryReady → Detail → Append` sequence MainForm wires, deterministically, without a peer.
One false alarm on the way: the capture log briefly appeared to mangle its own em dash
(`nothing to report, stopping here on purpose` read as `â€”` in a PowerShell console) — checked with
Read against the raw bytes, the file itself is correct UTF-8; the mangling was only `Get-Content`'s
own console rendering. Recorded so nobody chases a phantom encoding bug here later.

**`SessionLogTests.cs` — new, 3 tests, all real failures (not mocks):**
- A NUL byte in the folder path forces `Append` down its catch branch — confirms `WriteFailed` fires
  and `LastWriteError` is set, on a genuinely broken path, not a simulated one.
- A file sitting where `Directory.CreateDirectory` needs a folder forces the same failure on one
  `SessionLog` instance, then the obstruction is removed and the SAME instance writes again —
  confirms `LastWriteError` self-clears on the next success, which is what the technical view's live
  line depends on.
- An ordinary session (`Started`/`Checkpoint`/`TransferCheckpoint`/`Ended`) never raises `WriteFailed`
  at all, and the checkpoint text actually lands on disk.

Rule 11 note: these test NEW capability that did not exist before today (`WriteFailed`), so there is
no "unfixed" build to run them against in the literal sense the rule describes — the closest
equivalent already happened this session, unplanned: the real vanished-session failure, checked
against the OLD code by `git stash` comparison, produced zero trace anywhere, which is what the old
code being "wrong" actually looks like. `dotnet build -c Release`: 0/0. `dotnet test -c Release`:
301/303 — the same 2 pre-existing, unrelated failures as before, now alongside 3 new passing tests.

**Checkpoint readability, per Conor's own instinct — only the final block stays the full report:**
`SessionRecorder` gained `ShortStatusLine()` (one line: elapsed, avg fps, avg KB/s, current and worst
ladder level, transfer count) and `LastTransferLine()` (just the ONE transfer that just finished,
factored out of `AppendTransfers` via a shared `AppendOneTransfer` helper so the full report and the
checkpoint can never drift apart in format). `HostServer.EmitPeriodicCheckpoint` (every 3 minutes)
now writes the short line; `EmitTransferCheckpoint` (after each transfer) writes just that transfer's
own few lines — not the whole cumulative report repeated, which is what the first version of this did
and would have compounded every later transfer with every earlier one's text, reproducing the
"fourteen near-identical blocks" problem this whole feature exists to avoid, just slower. A two-hour
session with occasional transfers now costs roughly one short line every 3 minutes plus a few lines
per transfer, not forty near-duplicate multi-paragraph blocks.

**Rule 12 addendum — drafted, not yet added to CLAUDE.md.** Conor asked for the wording and to see it
before it is committed; see the reply for the exact text proposed.

**Recovery — proposal only, not built, per instruction.** See the reply for the one-line fix proposed
(decay `_quietWindows` in `OnFrameSent`'s dead-zone branch instead of zeroing it) and the reasoning
for why it does not reopen the oscillation the 3-window design was built to prevent.
