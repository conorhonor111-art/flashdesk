# CLAUDE.md — project context

You read this file automatically at the start of every session. Everything in it applies to
every task in this project unless I explicitly override it in a message.

**How this file is numbered.** Two numbering schemes live here on purpose:
- **Charter sections §1–§8** below are the original, authoritative policy. When a stage
  prompt says "read section 4", it means **charter §4 (Hard rules)** — the client-safety
  invariants.
- **Build stages, Stage 0–Stage 7**, near the end, are the build plan. They are aligned so
  that **Stage 4 ("Safe to hand to a client") = charter §4**, so "section 4" lands on the
  client-safety content under either reading.

Where the original charter and my reconstructed notes disagreed, the charter wins on rules,
policy, scope and communication; my notes win on facts about the actual machines and setup.

---

## 1. Who I am

I am not a developer. I direct this project; you write the code.

This governs how you communicate, never how deeply you think:

- Define every technical term the first time it appears, in one plain sentence.
- Never give me code without saying, in plain language, what it does, which file it goes
  in, and whether it replaces something or is added.
- For any action I have to take, give the literal path: which program, which menu, which
  button, what appears on screen, what happens after the click.
- If a step needs an account, a payment, a download, or an administrator password, say so
  **before** the step.
- After anything you build, tell me how to test that it works and what a failure looks
  like. I cannot infer this.
- Never say a step is "simple", "just", or "straightforward". Say what it involves.
- Reply to me in Georgian. Write all code, comments, identifiers, file names, commit
  messages and console output in English.
- Never reduce depth, drop a caveat, or skip a warning to make an explanation shorter.
  Simplify the tooling, never the thinking.
- **Never make me the measuring instrument.** When you need a number (fps, KB/s, latency) or a health
  signal (did capture recover?), build it into the program so I read a figure or a counter — do not
  ask me to watch a moving window, arrange screens, or check indicators through a lock and a UAC
  prompt. When you genuinely need my eyes, ask ONE yes/no about ONE specific thing, not a table to
  fill in. (This principle produced the diagnostics tool, the END-TO-END measurement, and the
  capture-health counters.)
- **When you report something as built, verify it in the SAME turn** — grep for it, run it, show the
  output — and say in one line how ("Confirmed by X"). If you cannot verify it, say so instead of
  reporting it as done. A report of work done is all Conor has (he cannot read code), so the gap
  between "intended" and "done" must be closed by process, not good intentions. This is a working
  rule, not a reaction to any one miss.

## 2. What we are building

A remote-support application for Windows. Two people, each on a Windows PC, anywhere on the
internet, behind ordinary routers. My client runs a small program and reads me a short code.
I type that code into my own copy, they press Accept, and I then see their desktop as live
video and can move their mouse, type, open programs and change settings exactly as if I were
sitting at that machine.

Reference product: AnyDesk (anydesk.com).

**The product is named FlashDesk (2026-07-30).** Conor has bought the domain (the exact domain
string is not recorded here yet — ask for it when Stage 3 needs it for TLS and the download link,
and record it then). FlashDesk appears everywhere a person can see; internals deliberately keep
the RemoteDesktop name — see "Naming — FlashDesk" under Project facts.

### Phase 1 — what we are doing now

A working private tool for me and my own clients. Perhaps 5–30 client machines. It has to be
reliable and it has to be usable by a non-technical person on the other end, but it does not
have to scale, does not need an installer for me, and is not being sold.

### Phase 2 — later, do not build for it now

Possibly a real distributed product. Design so that Phase 2 is not blocked, but **do not add
abstraction, plugin systems, configuration layers or multi-tenant structure "for later".**
If a Phase 1 decision would be genuinely expensive to reverse in Phase 2, say so in one
sentence at the time and let me decide. Otherwise build the simple thing.

## 3. Definition of done for Phase 1

I can email a client one `.exe` file. They double-click it — no installation, no
administrator password, no account. It shows them a 6-digit code. They read it to me on the
phone. I type it in, they press Accept, and I fix the problem on their machine, including
steps that require an administrator prompt.

Nothing is finished until that full sequence works on a real client machine.

*(2026-07-30 update: the "6-digit code" became the permanent 9-digit **FlashDesk ID** — a
researched spec from Conor replaces the ephemeral-code idea. See "The FlashDesk ID system" under
Stage 3.)*

## 4. Hard rules — do not design around these

1. **The client must accept every session by hand.** An incoming connection shows a dialog
   with my ID and name, and Accept / Reject buttons. Timeout means Reject.
2. **A live session is always visible on their screen** — a persistent on-screen strip or
   border with a Disconnect button, which I cannot hide or suppress from my side.
3. **No hidden, silent or invisible mode ever.** Visible window, visible tray icon, and the
   program stops completely when they close it.
4. **Every session is logged on their machine** — time, duration, my ID, my IP — in a plain
   text file they can open.

These are not decoration. They are the technical difference between remote-support software
and malware. Windows Defender, SmartScreen and every antivirus engine classify an unsigned
remote-control binary with no visible consent flow as a trojan, and they are right to. Break
any of the four and the program becomes undeployable no matter how good the code is.

## 5. Stack — decided, do not re-litigate without a concrete reason

| Layer | Choice |
|---|---|
| Language | C# on .NET 8 |
| My side (viewer) UI | WinForms |
| Client side UI | WinForms, single self-contained `.exe`, no install |
| Screen capture | DXGI Desktop Duplication via the `Vortice.Windows` package, GDI `BitBlt` fallback |
| Video for Phase 1 | Tile-based JPEG diff (see §6) — **not** H.264 |
| Input injection | Win32 `SendInput` via P/Invoke |
| Network | Relay-only over WebSocket on TCP 443 (see §6) — **no** NAT hole punching in Phase 1 |
| Relay server | ASP.NET Core minimal API, Linux VPS, behind Caddy or nginx for TLS |
| Encryption | TLS to the relay first; end-to-end added at Stage 5 |

Reasoning I have already accepted, so that you do not re-derive it: C# because it reaches
every Windows API directly, has the largest body of examples, and is the most readable
language for someone who cannot write it himself. If you find a genuine blocker in any of
these choices, raise it before writing code — do not silently substitute.

**Note on package names (my discovery, keep it):** on NuGet there is no single package
literally named `Vortice.Windows` — that is the name of the project. The actual packages to
reference are `Vortice.Direct3D11` and `Vortice.DXGI`. Same library the charter means.

**UI framework — WinForms, not WPF (decided 2026-07-29; the original charter line said WPF,
which is overridden here, with the reason, so no later session reopens it).** The UI in this
project is almost nothing: one window showing an ID and status, one window showing a video
frame. All the real work is capture, encode, network and input injection. WPF would add XAML —
a second language for Conor to read — for no benefit at this scale, and the projects were
already scaffolded as WinForms in Stage 0, so converting would cost time for nothing.

**One WinForms constraint that must not be got wrong — the viewer's video display.** Do NOT
use a `PictureBox` with a fresh `Bitmap` per frame: at 10–15 fps that allocates 10–15 bitmaps
a second and the garbage collector dominates the profile. Instead use a **double-buffered
custom control**, hold **one** `Bitmap` for the lifetime of the session, write changed tiles
into it with **`LockBits`** (direct pixel access, no per-tile GDI+ allocation), and
**invalidate only the changed rectangles**, not the whole control.

## 6. Architecture for Phase 1 — the two simplifications that matter

These are deliberate. Do not "improve" them back into the complicated version. The reasoning
is written out in full precisely so that a future session does not "optimise" one of them
away and cost a week.

**Relay-only networking.** Both programs open an outbound WebSocket connection to my server
on port 443 and the server pipes bytes between the two. Real products do NAT hole punching to
get a direct peer-to-peer connection; that is complex, and it still fails on roughly a fifth
of networks. Outbound 443 works on every home network, every office network and every hotel
Wi-Fi, with no router configuration on either end. The cost is bandwidth on my server, which
at my scale is a few gigabytes a month. Hole punching becomes a later optimisation, not a
prerequisite.

**Tile-based JPEG instead of H.264 video.** Split the screen into a grid of tiles (start at
128×128 pixels). Each capture, compare each tile against the last one sent and transmit only
the changed tiles, as JPEG. A support session is a mostly-still screen with short bursts of
movement, so this sends almost nothing while I read the client's screen, and it is a fraction
of the work of a hardware-accelerated H.264 pipeline. Target 10–15 frames per second. If the
result is too slow to work with once we can measure it on a real connection, we upgrade the
codec then, with real numbers in hand.

## 7. How we work

- **One stage at a time.** I will give you a stage prompt. Build only that stage. Do not
  start the next one, and do not scaffold for it.
- **Before writing code in a new stage**, tell me: what you will build, which files you will
  create or change, and anything you need from me. Wait for my go-ahead.
- **After each stage**, give me the exact steps to run and test it, and stop.
- **When something does not work**, ask me for the exact error text or a screenshot. Do not
  guess at three fixes at once — diagnose one thing, tell me how to check it, then fix.
- **Keep a `PROGRESS.md`** in the project root. After each stage, append what was built,
  what was decided and why, and anything left unfinished. Keep it short.
- **Commit to git after each working stage** with a clear message. If git is not set up yet,
  set it up in Stage 0 and tell me what git is and why I want it.

## 8. Correct me

If anything in this file is technically wrong, out of date, or contradicts itself, say so
directly. If I ask for something that will not work, tell me it will not work and why. I
would much rather be corrected than agreed with. Agreeing with a bad instruction costs me
weeks that I cannot see coming.

---

# Project facts (discovered during setup — keep these; I cannot recover them otherwise)

## Open — not yet decided (do NOT treat these as settled; written-as-decided is how a future session goes wrong)

- **Relay provider — NOT chosen.** An earlier DigitalOcean lean was **retracted**; the provider is
  deferred until the datacentre-region recommendation, because location decides it.
- **Datacentre location — NOT chosen.** Must be the option nearest **Kyiv**; Conor needs a way to
  measure real latency to a candidate **before** paying.
- **Domain purchase — DONE 2026-07-30.** Conor bought the domain and named the product
  **FlashDesk**. The exact domain string is not recorded here yet — ask for it when Stage 3 (relay
  TLS + download link) needs it, and record it then. (The earlier where-to-buy question is moot.)
- **Relay sizing — unanswered.** Whether 1 vCPU / 1 GB is genuinely enough at ~3 simultaneous sessions,
  or just cheap.
- **ACCESS_LOST recovery — VERIFIED by hand 2026-07-30: 3 interruptions survived, 0 failures** (a screen
  lock + a UAC prompt, no viewer connected). The refresh-rate step could not be run — Conor is on RDP
  and display settings are blocked over RDP — and 3-survived / 0-failed is enough. **Item closed;** left
  here only so the resolution and its RDP caveat are not lost. (Runnable ALONE because the host keeps
  capture alive for health tracking even with no viewer connected.)
- **The `.223` GDI capture number is UNRESOLVED: 16.1 ms vs ~47 ms.** Both were real readings given to
  Conor — do **not** settle this by relabelling one as a mere estimate (that was wrong). Two live
  hypotheses, both worth resolving **before Stage 4 quotes any performance figure to anyone**:
  (a) **the window** — 16.1 ms came from a headless command-line run; ~47 ms from a run launched with
  the app window open and refreshing. The idle-contamination fix (stopping the window's refresh timer
  during diagnostics) concedes the window was a variable, and it may move capture timing, not just idle
  tile counts. (b) **the remote-desktop path** — Conor reaches `.223` over RDP, and screen capture over
  RDP behaves differently from capture on a machine someone sits at, and differs again by whether the
  RDP session is attached or detached. **Every `.223` number this project has produced came through
  RDP**, so `.223` figures may not represent a client at a real machine. Do not spend diagnostics runs
  on this mid-handover; resolve it deliberately before quoting numbers.
- **Biggest performance lever — option for later, do NOT act now.** `.223` is allocated **2 of the
  host's 24 cores**; that is a VMware setting, not hardware, so raising it costs nothing. Combined with
  **parallelising tile encoding across cores** (ruled out earlier only because we assumed the hardware
  was fixed), this is the largest performance lever the project has — the single-threaded ~36–50 ms/frame
  encode is the current ceiling. Revisit once the tool works end-to-end.

*(Resolved 2026-07-29, no longer open: `.223` has **2** logical processors — confirmed by OS query, not
a guess — so "encoding is the ceiling" holds. See the performance-ceilings note.)*

## Naming — FlashDesk (2026-07-30)

The product is **FlashDesk** everywhere a person can see: window titles, headings, the
executables (`FlashDesk.exe` for the host, `FlashDeskViewer.exe` for the viewer — set via
`<AssemblyName>`, so Task Manager shows the same names), the capture log
(`FlashDesk-capture-log.txt` on the Desktop), the diagnostics report
(`FlashDesk-diagnostics-<machine>-<timestamp>.txt`), and the icons (`assets\FlashDesk.ico` idle +
`assets\FlashDesk-live.ico` live — see "The icon is an instrument").

**Deliberately NOT renamed (Conor's call): C# namespaces, project names and folders
(`src\RemoteDesktop.*`), the solution file, and the git repo** — mechanical churn with real
breakage risk that buys nothing visible. That rename is a recorded LATER task, to be done as its
own deliberate pass with nothing else mixed in. Until then `dotnet run --project
src\RemoteDesktop.Host` etc. keep their current paths.

## Machines and their test roles

| Test role | Address | Machine | Prompt |
|---|---|---|---|
| **HOST** — screen is captured and controlled; runs `RemoteDesktop.Host` | `192.168.1.223` | **VMware VM** (Task Manager: Virtual machine = Yes), **2 vCPU** on a host with a **24-core Intel Xeon Gold 6262 @ 1.9 GHz**, 16 GB RAM. Where all code, building and git live — the dev machine. | `C:\Users\PC>` |
| **VIEWER** — where I sit to watch and control; runs `RemoteDesktop.Viewer` | `192.168.1.222` | Windows Server 2022 21H2 (build 20348), VMware, user `Administrator`, network profile Private. | `C:\Users\Administrator>` |

**Conor reaches `.223` over a REMOTE DESKTOP connection — he is NOT sitting at it.** This affects the
interpretation of *every* measurement taken on `.223` (screen capture over RDP does not behave like
capture on a machine someone sits at — see the open capture-timing question), and it blocks some
actions from his side: he could not change the refresh rate for the ACCESS_LOST test because RDP does
not allow display-settings changes. Keep this in mind whenever reading a `.223` figure.

**Why `.223` is the HOST for video/capture work — do not reverse it.** `.223` is the dev machine
(code, build, git) and the screen Conor actually sits at, so capturing it is the realistic test.
**Correction to an earlier belief:** `.223` and `.222` are *both* VMware VMs, and — contrary to the
original assumption that a VM forces the GDI fallback — **both provide DXGI Desktop Duplication**, so
the DXGI path real clients use is exercised on `.223` by default. (`.222` is the viewer for video, and
the roles **swap for input testing** — see consequence 2. Neither machine is a physical PC; the
earlier "physical dev PC" wording was wrong.)

### Three consequences of this arrangement — handle each in code, warn me at test time

1. **Feedback loop / infinite mirror.** I sit at the physical HOST (`.223`) and open the VM
   (`.222`) in a VMware window on that same screen. The viewer inside the VM displays `.223`'s
   desktop — which contains the VMware window — which contains `.223`'s desktop — an infinite
   mirror. It is not fatal, but it makes the changed-tile count explode and makes any FPS or
   KB/s figure meaningless. Mitigations, best first:
   - **Second physical monitor on `.223`:** the HOST captures the *primary* monitor, so put
     the VMware/viewer window on the *second* monitor. Primary stays a normal desktop, no
     mirror, and the FPS/bandwidth numbers are trustworthy. This is the clean solution.
     ⚠️ Whichever monitor is set as **primary** in Windows is the one captured — check this.
   - **Minimise the VMware window** while reading the numbers: no mirror, but you cannot watch
     the live picture at the same time. Fine for *measuring*, useless for *watching*.
   - **Point the HOST at a static screen** (e.g. an open Notepad) when measuring, so even a
     mild mirror contributes little churn.
   - **Single monitor, honest limitation:** with only one screen you cannot both watch the
     live viewer and avoid the mirror at the same time. There is no clever region-exclude that
     survives the window moving. Watch *or* measure, not both — and treat single-monitor
     FPS/bandwidth as inflated. A second monitor is the only real fix; say so, do not pretend.

2. **Input has its OWN feedback loop — so input testing swaps the roles.** If `.223` stays the
   host during an input test, the viewer inside the VM (on `.223`'s own screen) sends a mouse move
   to `.223`, whose real cursor then lands back inside the VM window and reads as *more* movement —
   a runaway loop, and it would look exactly like a coordinate-mapping bug when it is not. This is
   the mouse version of the video mirror in consequence 1.
   - **For input/control testing (Stage 2+), swap the roles: `.222` is the HOST (the machine being
     driven), `.223` is the VIEWER (where my hands are).** The driven cursor then lives inside the
     VM window and never touches the machine I am sitting at. DXGI is irrelevant here — input
     injection is identical on a VM.
   - **This swap is for input/control testing ONLY. All video and capture work keeps `.223` as the
     HOST** — that is the whole reason the host is pinned to the physical PC (see above). Never let
     the swap leak into capture measurements.
   - **Kill switch — know it before the first input test, both directions:**
     - *Swapped roles (`.222` host, `.223` viewer):* my hands are on `.223`, which is NOT being
       driven, so the soft stop is trivial — click any window on `.223` outside the viewer, or press
       the viewer's Disconnect; the viewer stops sending input the instant its picture loses focus.
       Hard stop on the VM: VMware menu **VM → Send Ctrl+Alt+Del** → Task Manager → End
       `FlashDesk` (the host process name since the 2026-07-30 rename); or suspend the VM.
     - *Original roles (`.223` host — if I ever run input that way):* press **`Ctrl+Alt+Delete` on
       `.223`'s own physical keyboard.** Windows handles it on a secure desktop the app and the
       remote side cannot touch (until Stage 6) → **Task Manager → `FlashDesk` → End
       task** (the host process is named FlashDesk since the 2026-07-30 rename). This is the
       guaranteed one, because `.223` is my real machine.
   - **State the relevant kill switch to me before the first input test, every time.**

3. **Stuck modifiers.** If the connection drops while a modifier is held, `.223` is left with
   Ctrl/Shift/Alt/Win down and is unusable until reboot. The code must **release all modifier
   keys on disconnect, on timeout, and on host shutdown** — viewer side sends key-up for every
   key it is tracking; host side synthesises key-up for every key it injected and still holds.

### Traps that already cost time — keep them written down

- **Both machines ignore pings** (default Windows firewall). A `Request timed out` from
  `ping` does **not** mean they cannot reach each other. Verify reachability with `arp -a`
  instead, which lists machines this PC has actually exchanged traffic with.
- The VIEWER VM runs Windows **Server** 2022, not desktop Windows. Fine for development, but
  anything that behaves oddly only there should get a final check on a real Windows 10/11
  desktop before it ships. (The HOST dev PC is Windows 10 Pro — genuine desktop Windows.)
- **DXGI on the VM: this one actually works.** Contrary to the earlier assumption, the `.222` VMware
  VM's adapter DOES provide DXGI Desktop Duplication, so it captures on the fast path by default. To
  exercise the GDI fallback for comparison, **force it** — the "Run diagnostics (force GDI)" button on
  the host window, or `--diagnostics <path> <idleSeconds> gdi`. (Other VMs/adapters may still lack DXGI
  and fall back automatically — that remains expected, not a bug.)
- Conor sometimes pastes example values literally and sometimes runs a command on the wrong
  machine. Give commands fully filled in with real values, and always name the machine.
- **A brand-new host machine silently blocks the port — no firewall dialog appears** (hit on the
  Server 2022 VM at Stage 2: the first inbound connection to TCP 7789 was just dropped, with no
  prompt at all). Fix it once, from an **Administrator PowerShell on the host machine**, with this
  exact line — nothing to decide:
  `New-NetFirewallRule -DisplayName "RemoteDesktop 7789" -Direction Inbound -Protocol TCP -LocalPort 7789 -Action Allow`
  This recurs on every new test machine, and on every client machine at Stage 4 — the client
  packaging must add this rule automatically there.

## Environment and commands

- .NET SDK **8.0.423**, git **2.55** — both on the **dev PC only**. The test machine needs
  nothing installed (from Stage 4 it receives a self-contained `.exe`).
- Project root: `C:\Dev\RemoteDesktop`. **This is not necessarily the folder a Claude Code
  session opens in** — see "Where this file must live" below. Use absolute paths or
  `git -C C:\Dev\RemoteDesktop`.

```
dotnet build -c Release                                    # build everything (Release)
dotnet test  -c Release                                    # run the protocol tests (after EVERY stage)
dotnet run -c Release --project src\RemoteDesktop.Host      # HOST machine
dotnet run -c Release --project src\RemoteDesktop.Viewer    # VIEWER machine
```

**Run `dotnet test` after every stage.** `RemoteDesktop.Tests` covers the wire protocol only —
round-trip encode/decode, truncated message, an oversized length prefix, a malformed handshake — and
holds the hardening in place so a later tidy-up cannot silently reintroduce a bug (e.g. the length
prefix that once would have allocated 32 GB). Protocol only, by design; do not grow it into a general
suite.

**Always `-c Release` for anything Conor runs to test or measure — no exceptions.** A Debug build
runs the JPEG encoder several times slower, and the encoder is essentially the whole cost of this
program, so every FPS / KB-per-second number taken from a Debug host is wrong with no visible sign
that it is. Whenever I report a measured number, I state which configuration it came from (Release
unless explicitly noted).

Solution `RemoteDesktop.sln` contains three projects under `src\`:

| Project | Role | Framework |
|---|---|---|
| `RemoteDesktop.Host` | Screen capture + serving (runs on the controlled machine) | `net8.0-windows`, WinForms |
| `RemoteDesktop.Viewer` | Display + control (runs where I sit) | `net8.0-windows`, WinForms |
| `RemoteDesktop.Shared` | Wire protocol both sides speak | `net8.0`, no UI, no Windows-only APIs |

Note: UI framework is **WinForms** for both (decided 2026-07-29, see §5). The earlier charter
line saying WPF was arbitrary and is overridden — do not reopen it.

Git: branch `main`, local only, no remote. Commit at the end of each stage with a message
naming the stage. Never commit `bin`/`obj` (already handled by `.gitignore`).

## Architecture rules (mine — they keep later stages as swaps, not rewrites)

1. **The window is a shell, not the program.** Capture, encoding and networking must never
   depend on the UI layer. Stage 6 moves that machinery into a Windows service with no
   window at all; entanglement with the UI turns Stage 6 into a rewrite.
2. **All socket setup lives in one file per side** (`Net/HostServer.cs`,
   `Net/ViewerClient.cs`). Stage 3 replaces "listen for incoming" with "dial out to the
   relay"; that must touch one file, not twenty.
3. **Anything crossing the wire is defined once, in `Shared`.** Never define a message
   format separately on each side; the two copies will drift.
4. **`Shared` stays platform-neutral** — no `System.Drawing`, no Win32. The Stage 3 relay
   runs on Linux and references this same protocol code.
5. **Capture and input sit behind interfaces** (`IScreenCapture`), so implementations can be
   swapped: DXGI ↔ GDI now, session-aware versions in Stage 6.

## Protocol facts

- TCP port **7789** · tile size **128×128** · default JPEG quality **95** (operator-adjustable
  60–95, live) — all defined once in `Shared/Protocol/ProtocolConstants.cs`, never hard-coded
  elsewhere. **Quality 95 is a deliberate LAN-only default** (on a local network the extra bytes are
  free); **Stage 3 must replace it with adaptive quality driven by measured bandwidth**, because
  there the bytes are the server's bill and the client's home connection.
- Every message is length-prefixed with a type byte, so two frames can never run into each
  other on the wire.
- **Latency is measured by round-trip ping, never by comparing timestamps.** The two
  machines' clocks are not synchronised; subtracting them yields meaningless (sometimes
  negative) numbers.

## Measurement procedure (use the same method every stage so numbers stay comparable)

**Every measurement run is a `-c Release` run** (see Environment and commands) — Debug numbers are
meaningless because the encoder dominates the cost. State the configuration with any number reported.

Because the operator sits at the HOST and opens the VIEWER VM window on the same screen, read
performance numbers **off the HOST window on `.223`**, not off the viewer:

1. On `.223` run the HOST; on `.222` run the VIEWER and connect.
2. **Minimise the VMware/viewer window entirely.** This removes the mirror. The HOST keeps
   capturing and sending and its own FPS / KB/s counters keep updating, because the viewer's
   receive loop runs on a **background thread** and keeps reading and acknowledging frames even
   while its window is minimised. If a future change makes the viewer stop pulling frames when
   minimised, this measurement silently breaks and the numbers go stale without warning —
   guard against it, and sanity-check that the HOST counters still move with the viewer down.
3. Record **two** numbers every time, so stages stay comparable:
   - **IDLE** — static desktop, nothing moving on `.223`.
   - **BUSY** — a video playing fullscreen on `.223`.
   Report each as FPS and KB/s read from the HOST window.

## Measured performance ceilings (built-in diagnostics, 2026-07-29 — they constrain later decisions)

Two figures per machine matter and they differ:
- **END-TO-END** (real capture + real encode + real moving screen) is the number to quote for anything
  real, including Stage 3 bandwidth planning.
- **PATTERN** (synthetic full-screen noise, encode only) is a **~40 % overstated upper bound**, useful
  only for comparing machines: real desktops have large flat areas that compress far better than
  noise, so PATTERN runs ~40 % above END-TO-END at the same tile count. Never quote PATTERN as a real
  figure.

**Full-motion END-TO-END baselines (Release, quality 95):**

| Machine | Capture | fps | bytes/s |
|---|---|---|---|
| `.222` VM | DXGI | ~17 | ~2.2 MB/s |
| `.223` (2 cores) | DXGI | ~13 | ~1.7 MB/s |
| `.223` (2 cores) | GDI (forced) | ~7 | ~1.0 MB/s |

- **Encode is the ceiling; a fully-changing screen never reaches the 30 fps cap.** Real support
  screens change little and do hit 30 fps — full motion is the worst case, not the normal case.
- **`.223` is a slow 2-core machine — the floor, not typical.** `.222` is faster. (Logical processor
  count **CONFIRMED = 2** via OS query 2026-07-29 — an Intel Xeon Gold 6262 @ 1.90 GHz presented as two
  single-core sockets, i.e. a slow virtualised allocation. Because the tile encoder is single-threaded,
  the ~36–50 ms/frame full-motion encode is the ceiling regardless; more cores would only help if tile
  encoding were parallelised later. **CONFIRMED by Task Manager 2026-07-30: `.223` is a VMware VM**
  (Virtual machine = Yes, Sockets 2, Virtual processors 2), on a 24-core Xeon Gold 6262 host; the
  RTX 3060 Ti and 180 Hz monitor belong to that host, not to `.223`. So `.223` sees 2 vCPU, no GPU
  passthrough beyond the virtual adapter, and DXGI still works.)
- **GDI roughly halves the frame rate vs DXGI on the same hardware** (`.223`: ~7 vs ~13 fps), because
  GDI copies the whole screen every frame (~48–76 ms on `.223`) while DXGI wakes only on change.
- **Quality 95 is nearly free on REAL content** (~7 % more bytes than q70, END-TO-END) though expensive
  on synthetic noise (~41 % on PATTERN). Real desktop content compresses well at any quality, so the
  quality-95 LAN default is well justified — but over the internet it still needs adaptive quality.
- **Full-motion over the internet is impossible** on a typical home upload (~1–2 MB/s). Stage 3 must
  handle it with adaptive quality/frame-rate, not discover it.

## Design system (decided 2026-07-29 — palette, type, spacing, states, and the reasoning)

**DESIGN IS CLOSED (Conor, 2026-07-30).** No more icon rounds, no more window rounds. What exists
is approved and good enough, and an icon or a window can be changed in five minutes at any point
in this project's life. Do not reopen any visual decision without Conor explicitly asking — the
product's actual problem is that it has only ever worked on one LAN, and Stage 3 is the work.

UI framework re-examined once the design started to matter: **WinForms stays**, with a central
design system in a shared library `RemoteDesktop.UI` (`Theme.cs`). Reasons, not inertia: the design
is deliberately **flat** (no gradients, shadows, glow, or animation — though **rounded corners** were
added 2026-07-29, see "Rounded corners and the polish pass" below), which
is WinForms' comfort zone; the only genuinely custom control (the video canvas) already works and
porting it to WPF is the project's highest-risk change for zero visual gain; custom drawing is barely
needed here; and staying on WinForms keeps one language for Conor (§1). Full analysis was given at the
Stage-2/3 boundary.

### Palette — one colour, one meaning, used nowhere else

| Meaning | Colour | Hex |
|---|---|---|
| Structure / no state | Neutral grey | window `#F5F6F8`, card `#FFFFFF`, border `#C6CCD4`, text `#1B1F24`, secondary text `#606266` (nudged hue-neutral 2026-07-30 from `#5A626C`, which read greenish at Small sizes through ClearType fringing) |
| Ready · running · nobody connected | **Neutral grey** (dot ● + word; was green until 2026-07-30 — idle should not attract the eye, and freeing green gave it to the brand) | dot/text `#606266` |
| The brand — the mark ONLY, never a state | **Vivid green on a near-black tile** (decided 2026-07-30) | green `#2BD16B`, tile `#17191E` |
| A decision being asked right now | **Blue** | `#1A73E8` |
| A session is LIVE, someone is watching | **Amber** | text/dot `#B26A00`, fill `#F4B400` |
| Disconnect · reject · revoke (destructive/ending only) | **Red** | `#C5221F` |
| Operator (viewer) side chrome | Graphite | header `#242931`, text `#ECEFF3` |

**THE rule that matters most — the client's live-session indicator is AMBER, never green and never
any "everything is fine" colour.** Green in interface convention means "fine, you may ignore this" —
exactly wrong for an indicator whose entire job is to make sure the client never forgets someone is
on their machine. It must stay noticeable for the whole session. A later session that has the hex
values but not this reasoning will "harmonise" the indicator to something calmer — do not; it is a
safety control, not decoration. *(Updated 2026-07-30: idle is now a neutral GREY dot + word — idle
should not attract the eye at all — which gives amber an even calmer field to shout against. Ready
● and Stopped ■ stay apart by glyph + word, never by colour alone. Green left the semantic set
entirely and belongs to the brand.)*

**Brand colour rule (2026-07-30, revised same day):** the FlashDesk brand/mark colour must NOT be
any semantic state colour. One colour one meaning: a colour cannot be both "the brand" and a state
(AnyDesk made this mistake with red and now cannot use red cleanly for danger). The semantic set
is now **amber / blue / red**; green was REMOVED from that set precisely so the brand could take
it — Conor wanted the green, so the system changed instead of the mark: idle became neutral grey,
and **brand green (`#2BD16B` on tile `#17191E`) lives ONLY in the mark**, never as a signal.

### Accessibility — non-negotiable

- **Never signal state by colour alone.** Every state also carries a distinct icon/shape **and a
  word**. Roughly one man in twelve cannot separate red from green.
- Body text ≥ **4.5:1** contrast on its background; large text ≥ **3:1**.
- **Test the whole palette in greyscale.** If two states become indistinguishable, the design has
  failed regardless of how it looks in colour.

### Typography and layout

- **One typeface — the Windows system font (Segoe UI).** No downloaded fonts. Four sizes:
  Display 24, Heading 12 (semibold), Body 10, Small 9 (pt) — plus ONE sanctioned exception added
  2026-07-30: **Hero 30 (semibold), reserved exclusively for the connection code/address hero.**
  The code is the product's most-seen artifact — a number a stressed person reads aloud over a
  phone — so it gets the one size outside the scale; using Hero for anything else is a defect.
  Two weights: regular + semibold — note
  WinForms/Segoe has no true "medium" (500) weight, so semibold (600) is the emphasis weight (still
  lighter than bold, which is not used). *Exception reserved:* if Segoe UI digits prove ambiguous
  when a stressed client reads a code aloud, the sanctioned fallback for the address hero only is
  Consolas (a built-in Windows monospace, not a download).
- **Sentence case everywhere.** No ALL CAPS, no Title Case on buttons.
- **Spacing scale** `4 · 8 · 16 · 24 · 32` (px at 100%), used everywhere — uneven padding is what
  makes software look amateur.
- Every window must keep working when resized and at **125% and 150%** display scaling
  (`AutoScaleMode.Font`, `TableLayoutPanel`/`Dock`/`Anchor`, no hard-coded pixel positions).

### The three states of the client-facing window (Stage 4 builds them fully; recognisable across a room)

1. **READY** — nobody connected. Neutral grey dot + lock + "Nobody is connected" (grey since
   2026-07-30 — idle must not attract the eye). The **address is the
   hero**: large, grouped in threes, high contrast, one-click copy, digits set generously for reading
   aloud over a phone.
2. **INCOMING REQUEST** — a decision, not a status. Blue accent; shows who is asking (name + address);
   **Accept and Reject equally weighted** — Reject is the safe default and must never be smaller,
   greyer, or harder to hit.
3. **LIVE SESSION** — amber. Plain words: who is connected, what they may do, how long. **Disconnect
   always visible, always one click. No animation on the indicator** (a pulse becomes wallpaper and
   can be mistaken for activity).

The **operator (viewer) side is deliberately different** (graphite header) so the two sides can never
be confused in a screenshot.

### Rounded corners and the polish pass (decided 2026-07-29 — this REVERSES the earlier square-corner rule)

Conor withdrew the square-corner instruction: he wants rounded corners and a genuinely refined look.
This does **not** reopen WinForms — rounded corners are more work in WinForms, not impossible, and
that work sits in the **chrome** (a custom-painted rounded panel/button) where a mistake is harmless;
converting to WPF would mean rewriting the video control, the hardest-won code in the project (its
coordinate mapping was verified at all four corners). Rewriting working internals for prettier buttons
is the worst trade available.

How, specifically, so a later session gets it right:
- **`GraphicsPath` + `SmoothingMode.AntiAlias`, never `Control.Region`** — Region gives jagged aliased
  edges.
- **The radius is a `Theme` value and scales with DPI.** Set to **12** (2026-07-30, raised from
  8 — a timid radius reads as an accident) and there is exactly ONE radius, used everywhere; two
  radii in one window look like a mistake.
- **Outer window frame:** try `DwmSetWindowAttribute` with `DWMWA_WINDOW_CORNER_PREFERENCE` (Windows 11
  rounds it at the OS level for free); **degrade cleanly on Windows 10.**

The rest of the pending "design pass": a simple embedded **app icon in both executables** (the default
.NET icon reads as "unfinished" to a client, on the taskbar and on the emailed file); **hover and
pressed states on every button** (currently dead surfaces); **real vertical rhythm** (everything on the
spacing scale, nothing eyeballed); **deliberate default window sizes**; and the **address/code area
treated as the hero** of the client window. Every value still goes through `Theme` (radius included).

### The icon is an instrument, not a logo (decided + built 2026-07-30)

Six exploration rounds ended in this conclusion: **originality does not exist at 16 px** — every
depiction lands on a software cliché (a bolt), on this category's own cliché (a monitor — Windows
RDC, TeamViewer and VNC all use one), or on a competitor's shape. Chrome, Explorer and PowerShell
are memorable through colour and repetition, not invention. The mark (final, 2026-07-30 — it
replaced the earlier K1 `#473D8C` disc, which had been chosen by elimination; Conor found a shape
he actually responds to, and the evidence strip confirmed the new mark beats the disc on
findability in all three rows): **a bold two-stroke angular form in vivid brand green `#2BD16B`
on a near-black tile `#17191E`, proportion P3 — the DESCENDING stroke is the long one.** That
proportion is load-bearing: a checkmark is short-down/long-up, and the inverse is what stops the
mark reading as a check ("done / verified" is the wrong message on a remote-access tool). Flat,
no gloss, sharp miter corner. The icon's real job is safety:

- **IDLE** — the green mark on the tile. Quiet, no state meaning (idle status in the window is a
  grey dot + word).
- **LIVE** — **the whole mark turns AMBER `#F4B400` and a badge bump appears top-right**
  (bottom-right is owned by the long stroke), breaking the silhouette — colour change AND shape
  change, never colour alone — shown the whole time a viewer is connected. This extends hard
  rule 2 (§4) into the operating system: a minimised window still shows, on the taskbar, that
  someone is watching.

Mechanics (verified on Windows 10, 2026-07-30, with a real loopback viewer handshake):
- Swapping `Form.Icon` at runtime updates the title bar and taskbar button instantly
  (`WM_SETICON`); Windows neither animates nor caches across the swap.
- **`ShowIcon = false` KILLS the live badge** (verified by test, same day): hiding the title-bar
  icon makes the taskbar fall back to the exe's static icon, so the runtime swap never reaches
  the taskbar. The title-bar icon is therefore mandatory — it is the handle that feeds the
  taskbar, not decoration. Consequence for branding: the brand appears exactly ONCE, in the
  title bar (icon + "FlashDesk"); the in-window lockup was removed to avoid duplication, and the
  designed lockup belongs on surfaces that don't carry the safety icon (Stage 3/4 dialogs, the
  viewer header, the download page).
- The **exe-file icon stays IDLE always** (Explorer, pinned shortcuts, the emailed file) — right,
  because a closed program cannot be live.
- **The tray must never be the safety surface**: Windows 10/11 hide new tray icons in the
  overflow by default.
- **ITaskbarList3 overlay badges must never be the mechanism**: Windows 10 does not draw them
  when "use small taskbar buttons" is on. Reinforcement at best, later.

Files: `assets\FlashDesk.ico` (idle, also the `ApplicationIcon`) and `assets\FlashDesk-live.ico`,
generated by `assets\make-icon.ps1`, embedded as `FlashDesk.AppIcon` / `FlashDesk.AppIconLive`.
The identity does NOT live in the icon — it lives in the code hero (`Theme.Hero`), the lockup
(mark + "FlashDesk" in Segoe UI Semibold at Heading size, K1 on the mark only), and the craft of
the window itself. Replacing the icon takes five minutes at any point; do not spend rounds on it.

### The host window has TWO VIEWS — simple and technical (decided + BUILT 2026-07-30)

The host window serves two audiences and must never show both the same surface. **Simple view —
the default, what the client sees:** the FlashDesk mark and name, ONE plain-language status line,
the connection code as the hero (large — read aloud over a phone by a stressed person), Copy, ONE
action (Stop sharing), and one small quiet "Technical details" link. **Not one number.**
**Technical view — behind that link, for Conor:** capture method, fps, KB/s, the health counters,
the quality selector, both diagnostics buttons, the log button. Why the split is load-bearing: the
client is non-technical and already anxious that someone is connecting to their machine; developer
readouts turn the window into a debugging console, and a line like "Capture failures: 0" is
reassuring to a developer and frightening to a client — they read the word "failures". A later
session will be tempted to "helpfully" surface a status number in the simple view. Do not — the
simple view stays number-free.

**The live state must read at a glance, from across a room, without reading a word (Conor,
2026-07-30):** a full-width status band at the top of the window blends into the window surface
when idle (neutral grey dot + word — idle does not attract the eye; brand green appears only in
the mark) and turns SOLID AMBER fill with dark icon + words while a viewer is connected. No animation; never colour alone. The one action
changes weight with state: **quiet neutral "Stop sharing" while idle** (red shouting when nothing
is wrong drains red), **full-weight red while live**, blue "Start sharing" when stopped. The
action row is anchored to the BOTTOM of the window in both views, so the primary action never
moves when the technical panel opens.

### What not to do

- No gradients, drop shadows, or glow. Flat surfaces with **rounded corners** (see above), real borders.
- No animation anywhere a person needs to read or decide.
- Nothing that makes Disconnect or Reject harder to find than Accept.
- No icon without a word beside it in any state the client sees.

## Where this file must live (session working directory)

Claude Code auto-loads `CLAUDE.md` from the directory the session opens in (and its
parents), not from wherever the code happens to be. As of Stage 0 the session was opening in
`C:\Users\PC\Desktop\sadsad`, which does **not** contain the project. For this file to load
automatically, **start each session from the project folder** `C:\Dev\RemoteDesktop`, so the
working directory and the code are the same place and this file travels with the repo. If a
session ever starts elsewhere and this file is not loaded, that is the cause.

---

# Build stages (Stage 0–Stage 7) — the build plan

Stage 4 below = charter §4. "Read section 4" means the client-safety invariants.

## Stage 0 — Set up the workshop ✅ complete

.NET 8 SDK, git, project folder, solution with three projects, first commit, test machine
confirmed reachable and set to a Private network profile.

## Stage 1 — See the other screen

Two machines on the same LAN, connecting by typed IP. No relay, no codes, no encryption, no
input control.

- Host captures the primary monitor with **DXGI Desktop Duplication** (Vortice packages —
  see §5 note), **automatic fallback to GDI BitBlt**, active method shown in the window.
- Frames split into 128×128 tiles; only changed tiles are JPEG-encoded and sent.
- Host window: local IP, viewer connected or not, FPS, KB/s out. One viewer at a time.
- Viewer: IP field + Connect, assembled picture scaled to fit, status bar with FPS, latency
  and bandwidth.

## Stage 2 — Take control

Mouse and keyboard captured on the viewer while the video window has focus, injected on the
host with **`SendInput`** (not the deprecated `mouse_event`/`keybd_event`). Explicitly
handle and explain: exact coordinate mapping (viewer window at any size/zoom → real remote
pixels); differing DPI/scaling; keyboard sent by **scan code, not character** (so a Georgian
layout does not produce garbage on a differently-configured machine); modifier keys
force-released if the connection drops mid-press; wheel, right/middle click, drag, double
click. Input must arrive reliably and in order even while video frames are dropped.

## Stage 3 — Work over the internet

Replace the typed IP with the **FlashDesk ID** — a permanent 9-digit per-installation address
(Conor's researched spec, 2026-07-30, replaces the earlier ephemeral 6-digit-code idea; full spec
in "The FlashDesk ID system" below). Relay: ASP.NET Core, WebSocket over TCP 443, Linux VPS.
Rate-limit connection attempts per ID and per source.
Both programs switch to **outbound** connections. **Consent is built here, moved up from Stage 4**
(decided 2026-07-29): an incoming connection raises an **Accept / Reject** dialog on the client (with
the operator's ID/IP) that **times out to Reject after 30 s** — so the moment the program is reachable
over the internet, nothing connects without a human deciding. **The allow-list gates this dialog**
(2026-07-30, see the ID system): a request from an operator ID not on the client's trusted list is
refused before any dialog appears; an empty list falls back to the dialog. The indicator strip, session log and
packaging stay in Stage 4. **Add adaptive quality AND adaptive frame rate**,
driven by measured bandwidth and by whether the screen is changing. Two measured justifications:
(1) full-motion costs ~1–2 MB/s — impossible on a home upload — so quality must fall under bandwidth
pressure (the LAN-only quality-95 default is revisited here); (2) on the **GDI** path, polling at
30 fps while nothing changes both **burns a large fraction of a CPU core continuously** (GDI copies the
whole screen every frame) **and is capture-bound even on a still screen** — the full-screen copy takes
longer than a 30 fps interval (~47 ms measured on `.223` → a ~20 fps ceiling with nothing happening),
so the client's fan runs the whole session and they say the tool slowed their computer. (The `.223`
GDI capture figure itself is disputed — see "Open — not yet decided".) The frame rate must drop toward
~1–2 fps on a static screen and ramp up on change. Full first-time-Linux deployment writeup: provider/size with real monthly cost, domain question, every
command with what it does — each as **one literal copy-paste line** (the format that works for
Conor, like the firewall rule), TLS from scratch, auto-restart on crash/reboot, how to check status
and read logs. Honest bandwidth cost at 10 and 50 clients.

**Stage 3 also carries these (2026-07-29 audit + decisions), each a required line item:**
- **Consent dialog + session log** move here from Stage 4 (see the consent note above). The session
  log is a line appended to a plain-text file on the client — who connected, when, operator ID/IP; it
  is the record of what the consent dialog decided, so the two ship together. (The button that opens
  the log in Notepad can stay in Stage 4.)
- **Handshake read timeout:** a peer that connects then sends nothing is dropped after a few seconds,
  so a stalled or hostile connection cannot hold a slot. Theoretical on the LAN; not on the internet.
- **Message cap → 16 MB** (from 64 MB) in `MessageChannel`: a frame is normally under 2 MB, so a
  tighter cap shrinks what an abusive peer can force us to allocate.
- **Serve the client download** as a static file from the same web server that fronts the relay
  (`https://<domain>/download`) — a normal link with my own name on it, same URL across updates, one
  static file, no extra cost.

### The FlashDesk ID system (Stage 3 spec — researched by Conor 2026-07-30)

**THE PRINCIPLE — the ID is an ADDRESS, not a credential.** Nine digits is a billion
combinations, which sounds like a lot and is not: an internet-facing service WILL be scanned, so
knowing an ID must never, on its own, get anyone anywhere. What actually protects the machine, in
order: **(1) the allow-list, when set; (2) the consent dialog the client must actively accept;
(3) the unattended-access password, if the client ever sets one** — and that last one, when built
in a later stage, gets rate limiting, exponential backoff, lockout, a visible record of failed
attempts on the client's own machine, and never a short numeric PIN (reported AnyDesk incidents
include unattended access that appeared brute-forced despite a complex password). This principle
governs every later access decision.

- **Permanent per-installation ID: 9 digits, three groups of three (`418 205 793`), digits
  only.** Nine, not 10-11: the number exists to be read aloud down a phone by a flustered
  non-technical person, and nine in threes is the edge of reliable; letters would need a phone
  alphabet clients don't have. First digit 1–9 (a leading zero gets dropped in dictation).
- **Born on FIRST RUN on the client's machine — never baked into the download.** One exe for
  everyone; an ID inside the file would give every downloader the same number.
- **Generated with a cryptographic RNG; NEVER derived from MAC, hostname or any hardware value**
  — derived IDs leak hardware information and are predictable (RustDesk's MAC-derived path is
  the cautionary tale).
- **Public ID + private SECRET** (RustDesk's UUID-mismatch mechanism, done properly): first run
  also generates a long random secret, stored locally, never displayed, never read aloud, never
  in the UI. The relay binds ID → secret (server side stores a salted HASH of the secret, so a
  relay DB leak exposes nothing) and refuses any claim on an ID with the wrong secret. ID taken
  at first registration → generate a new one, capped retries (~5), then a clear error — never a
  loop.
- **Clones — CERTAIN, not hypothetical** (Conor works on VMs; clients' IT clone machines too).
  **Correction to the naive design: a clone carries the same ID AND the same secret, so
  wrong-secret refusal does NOT catch it.** The relay must also refuse a second LIVE registration
  of an ID that is already online — with heartbeat/liveness so a crashed session's stale binding
  expires instead of locking the real machine out. The refused copy auto-generates a fresh
  ID + secret and says plainly in the window that its number changed and why. **The window never
  shows an ID the relay has not accepted** — a valid-looking unreachable number is worse than an
  error. Known limit (AnyDesk has it too): with identical secrets the relay cannot know which
  copy is "real"; whichever connects second gets the new number. Deliberate Stage 3 test: clone
  `.222`, run both, show Conor what happens.
- **Storage:** `%APPDATA%\FlashDesk\` — user-writable, needs NO administrator rights, and never
  lives next to the exe. So the ID **survives** program updates, deleting and re-downloading the
  exe, and emptying the Downloads folder. The secret is DPAPI-protected (CurrentUser). The ID
  does **not** survive: deleting that folder, a wiped Windows profile, or a different Windows
  user account on the same machine (per-user config — each account gets its own ID; fine at
  Phase 1 scale, recorded so it never surprises).
- **The allow-list — the deliberate advantage over AnyDesk.** The client's copy holds a short
  list of trusted operator IDs. A request from an ID not on the list is refused BEFORE the
  consent dialog — the client is never asked, so they cannot be talked into yes. This targets
  the category's primary abuse pattern (scam calls that walk a person into accepting; in early
  2025 attackers in the region impersonated CERT-UA with fake "security audit" connection
  requests). Adding an entry requires action on the client's machine; an empty list falls back
  to the normal consent dialog so the tool works before setup. **Honest limits, recorded so
  nobody oversells it:** (a) it guards the FlashDesk door only — it cannot stop a scammer from
  talking the client into installing a DIFFERENT remote tool; (b) it is only as strong as
  operator AUTHENTICATION — the relay must verify the connecting operator owns the claimed ID
  via its secret, and at Stage 5 the allow-list entry pins the operator's PUBLIC KEY with the
  number as its label, because a bare number can be claimed by anyone who learns it; (c) the
  empty-list fallback is a persistence vector — one socially-engineered Accept would let an
  attacker add themselves to the list — so the add-operator confirmation must IGNORE INJECTED
  INPUT (the host knows which input it injected itself; the remote hand cannot sign its own
  permission slip); (d) refusals are logged visibly on the client, so a legitimate new operator
  ID (e.g., after a clone regeneration) can be confirmed over the phone and added deliberately.
- **Display — the most-seen element of the whole product:** three groups generously spaced,
  Hero-sized (larger than feels comfortable), digits unambiguous in the typeface, Copy beside it
  with a VISIBLE copied-confirmation, findable within two seconds of the window opening, and one
  quiet sentence beneath: **"Only give this number to someone you contacted yourself."** Not a
  warning box — one line a person actually reads. The AnyDesk-style Alias (name@namespace) stays
  OUT of Phase 1.
- **Stage 6 migration is a REQUIREMENT, designed now (Conor, 2026-07-30):** at Stage 6 capture
  moves into a service running as SYSTEM, and SYSTEM cannot read a user's `%APPDATA%` — so the ID
  must move to a machine-wide store (e.g. `%ProgramData%\FlashDesk`). **On its first run the
  service ADOPTS the existing per-user ID and secret rather than generating new ones** —
  otherwise every client's number changes on update day, exactly when Conor has them written
  down against client names. Cheap now, expensive then. **Which user's ID, when the machine has
  several accounts each holding one (rule, 2026-07-30):** the account that performs the
  install/elevation; if that account has none but others do, the most recently used (latest
  last-run timestamp in each profile's config); the not-adopted IDs are retired, and those
  accounts' windows say so plainly on their next open — never a silent change.
- **Clones are ALSO detected locally, not only at the server — best-effort, with the server as
  the backstop.** Alongside the ID, store a machine fingerprint and compare it on startup.
  **NOT the Windows `MachineGuid`** — it lives on the disk, a clone copies the disk, so that
  check would never fire (Conor's catch 2026-07-30; sysprep exists precisely because of such
  values). The real candidates are what the hypervisor reassigns when a copy is acknowledged:
  the **SMBIOS UUID and the MAC address** — and they move together on VMware, where the
  auto-MAC's last three bytes ARE the UUID tail (read directly off `.223`: UUID `…3FD9AEFB`,
  MAC `00-0C-29-D9-AE-FB`). **The final pick comes from the Stage 3 clone A/B test** (read the
  values on `.222`, clone it, read both, choose from what actually differed), not from what
  sounds like an identifier. Honest limit: an operator answering "I moved it" (or an
  identity-preserving cloning tool) changes nothing, and local detection stays silent — the
  relay's second-live-registration refusal is the backstop that always fires. **False-alarm
  guard:** a changed fingerprint does NOT regenerate the ID by itself; the client first asks the
  relay whether this ID is live elsewhere — yes → regenerate, no → keep the ID and adopt the new
  fingerprint (a swapped network card or a docked laptop must never cost a client their number).
  The no-hardware-derivation rule stays intact: the ID is random; the fingerprint is a separate
  value used only for change detection.
- **Heartbeat numbers (decided 2026-07-30): client heartbeats every 20 s; the relay expires a
  registration after 60 s (three misses); the client retries from ~5 s with backoff to ~30 s.**
  Why: consumer connections routinely drop for 10–30 s, and 60 s tolerates that without a client
  on flaky Wi-Fi losing its ID to itself; a genuinely relocated machine waits at most ~60 s. What
  the client sees during the gap: a neutral "Connection lost — reconnecting…" status (not an
  error, not a changed ID); the number changes only if re-registration is actually refused.

**Relay location + provider + domain (location FIRST, price second):** every byte of every session
crosses the relay, so its physical location sets the whole product's latency (LAN was 1 ms; a
badly-placed relay makes it ~200 ms and the mouse drags through mud, ruining the tool however good the
rest is). **DECIDED:** Conor and his clients are in **Kyiv**, so the datacentre must be the nearest
option, chosen *before* price; and the domain will be a **bought `.com`** (not a free DuckDNS subdomain
— a tool with real clients should not depend on a free volunteer DNS service). **NOT yet decided (see
"Open — not yet decided" below):** the provider (an earlier DigitalOcean lean was **retracted**,
deferred until the region is recommended — location decides it), the specific datacentre, the actual
`.com` purchase (where to buy / what to avoid), and whether 1 vCPU / 1 GB holds ~3 simultaneous sessions.

## Stage 4 — Safe to hand to a client  (= charter §4)

**Re-read charter §4 in full when this stage begins.** Everything the person on the other
end needs:

- **Accept / Reject dialog — moved to Stage 3** (built there, so nothing is ever internet-reachable
  without a human deciding). It shows who is connecting and **times out to Reject after 30 seconds**;
  silence is never yes. The remaining bullets stay in Stage 4.
- Checkboxes putting the client in charge: allow keyboard/mouse, allow clipboard, allow file
  transfer. **The risky ones default to off.**
- While a session is live, a strip stays on top of everything on the client's screen with a
  **Disconnect** button. The operator must have **no way to hide, move or suppress it** —
  the enforcing code must be pointed out explicitly, not merely claimed.
- **Session log — moved to Stage 3** (it records what the consent dialog decided, so it ships with
  consent). Stage 4 keeps only the convenience button that opens that log in Notepad.
- Package the client side as a **single self-contained `.exe`**: no installer, no admin, no
  .NET runtime on the client machine; double-click to a visible code within a couple of
  seconds; closing the window ends everything. **Size reality (measured): ~65 MB is the floor** for a
  no-install self-contained WinForms exe and it **cannot be trimmed** — the SDK blocks it
  (`NETSDK1175`: Windows Forms + trimming unsupported; ReadyToRun only makes it bigger; compression is
  already on). So delivery is the **hosted download link** (served from the relay host, see Stage 3),
  not an email attachment — 65 MB exceeds Gmail's 25 MB limit anyway. Document how to send that link to
  a non-technical person, the exact
  SmartScreen warning an unsigned program shows, and what to tell them when they phone.

## Stage 5 — Encrypt it properly

End-to-end encryption so the relay cannot read the session. **X25519** key pair per machine,
generated on first run, private key never leaves the machine; session key agreed directly
between the two machines; payloads encrypted with **AES-256-GCM**; the relay forwards
ciphertext only, and this must be **demonstrable by Conor himself**. On first connection to a
new machine, both screens show a short **fingerprint** to read aloud over the phone, to
defeat a man-in-the-middle. Explain the whole scheme in plain language before writing code.

## Stage 6 — Survive the administrator prompt

The stage that decides whether the tool is genuinely useful. Today, when Windows shows the
UAC "allow this app to make changes?" prompt, the operator's screen goes black — exactly when
control is most needed.

**Requirement carried from the ID system (see Stage 3): the service must ADOPT the existing
per-user FlashDesk ID on first run** — SYSTEM cannot read `%APPDATA%`, so the ID moves to a
machine-wide store, and regenerating instead of migrating would change every client's number on
update day. The migration is designed into the Stage 3 storage code, not bolted on here. Capture and input move into a **Windows service running as LOCAL
SYSTEM**; a small **agent in the logged-in user's session** provides dialog, strip and tray
icon, launched via `WTSQueryUserToken` + `CreateProcessAsUser`; they talk over a **named
pipe**; the service detects desktop switches, calls `OpenInputDesktop` / `SetThreadDesktop`
and keeps working on Winlogon (covering UAC, lock screen, login screen); a **Send
Ctrl+Alt+Del** button on the operator side. Before code: explain the consequences of the
client now needing an administrator install, and whether portable and installed modes can
coexist. Write an **automated test for the secure-desktop path**.

## Stage 7 — Everything that makes it pleasant

Only after Stage 6 works, **one feature at a time**: multiple monitors · clipboard sync ·
file transfer · auto-reconnect · saved client list · unattended access (password set by the
client in person, no remote path to enabling it — and hardened per the ID principle: rate
limiting, exponential backoff, lockout, a visible failed-attempt record on the client's machine,
never a short numeric PIN) · in-session chat · quality selector ·
H.264 with hardware encoding (only once JPEG tiles are **measured** too slow).

---

## Phase 2 — not now

Deliberately out of scope until the tool has served real clients for months: code-signing
certificate, antivirus whitelisting, installer, auto-update, NAT hole punching, licensing.
Different project, different shape (charter §2).
