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

## 2. What we are building

A remote-support application for Windows. Two people, each on a Windows PC, anywhere on the
internet, behind ordinary routers. My client runs a small program and reads me a short code.
I type that code into my own copy, they press Accept, and I then see their desktop as live
video and can move their mouse, type, open programs and change settings exactly as if I were
sitting at that machine.

Reference product: AnyDesk (anydesk.com).

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

## Machines and their test roles

| Test role | Address | Machine | Prompt |
|---|---|---|---|
| **HOST** — screen is captured and controlled; runs `RemoteDesktop.Host` | `192.168.1.223` | Physical dev PC. Also where all code, building and git live. | `C:\Users\PC>` |
| **VIEWER** — where I sit to watch and control; runs `RemoteDesktop.Viewer` | `192.168.1.222` | Windows Server 2022 21H2 (build 20348), VMware, user `Administrator`, network profile Private. | `C:\Users\Administrator>` |

**Why the physical PC is the HOST, not the VM — do not reverse this.** DXGI Desktop
Duplication is the main capture path and it needs a real GPU. The VM has only a virtual
display adapter, so on the VM capture falls back to GDI. If I only ever captured the VM, I
would only ever exercise the GDI fallback — while my real clients' machines all have real
GPUs and will run the DXGI path. That path would then ship completely untested. Capturing the
physical PC exercises the path that actually matters in production. (The VM's weakness at
DXGI is exactly why it is the viewer.)

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
       `RemoteDesktop.Host`; or suspend the VM.
     - *Original roles (`.223` host — if I ever run input that way):* press **`Ctrl+Alt+Delete` on
       `.223`'s own physical keyboard.** Windows handles it on a secure desktop the app and the
       remote side cannot touch (until Stage 6) → **Task Manager → `RemoteDesktop.Host` → End
       task**. This is the guaranteed one, because `.223` is my real machine.
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
- **`.223` is a slow 2-core machine — the floor, not typical.** `.222` is faster.
- **GDI roughly halves the frame rate vs DXGI on the same hardware** (`.223`: ~7 vs ~13 fps), because
  GDI copies the whole screen every frame (~48–76 ms on `.223`) while DXGI wakes only on change.
- **Quality 95 is nearly free on REAL content** (~7 % more bytes than q70, END-TO-END) though expensive
  on synthetic noise (~41 % on PATTERN). Real desktop content compresses well at any quality, so the
  quality-95 LAN default is well justified — but over the internet it still needs adaptive quality.
- **Full-motion over the internet is impossible** on a typical home upload (~1–2 MB/s). Stage 3 must
  handle it with adaptive quality/frame-rate, not discover it.

## Design system (decided 2026-07-29 — palette, type, spacing, states, and the reasoning)

UI framework re-examined once the design started to matter: **WinForms stays**, with a central
design system in a shared library `RemoteDesktop.UI` (`Theme.cs`). Reasons, not inertia: the design
is deliberately **flat** (no gradients/shadows/glow/rounded/animation — see "what not to do"), which
is WinForms' comfort zone; the only genuinely custom control (the video canvas) already works and
porting it to WPF is the project's highest-risk change for zero visual gain; custom drawing is barely
needed here; and staying on WinForms keeps one language for Conor (§1). Full analysis was given at the
Stage-2/3 boundary.

### Palette — one colour, one meaning, used nowhere else

| Meaning | Colour | Hex |
|---|---|---|
| Structure / no state | Neutral grey | window `#F5F6F8`, card `#FFFFFF`, border `#C6CCD4`, text `#1B1F24`, secondary text `#5A626C` |
| Ready · running · nobody connected | **Green** | `#1E7E34` |
| A decision being asked right now | **Blue** | `#1A73E8` |
| A session is LIVE, someone is watching | **Amber** | text/dot `#B26A00`, fill `#F4B400` |
| Disconnect · reject · revoke (destructive/ending only) | **Red** | `#C5221F` |
| Operator (viewer) side chrome | Graphite | header `#242931`, text `#ECEFF3` |

**THE rule that matters most — the client's live-session indicator is AMBER, never green.** Green in
interface convention means "everything is fine, you may ignore this" — which is exactly wrong for an
indicator whose entire job is to make sure the client never forgets someone is on their machine. It
must stay noticeable for the whole session. Green would be comfortable and would quietly defeat hard
rule 2 in §4. Green is correct for the *opposite* state: running, and **nobody** connected. A later
session that has the hex values but not this reasoning will "harmonise" the indicator to green — do
not; it is a safety control, not decoration.

### Accessibility — non-negotiable

- **Never signal state by colour alone.** Every state also carries a distinct icon/shape **and a
  word**. Roughly one man in twelve cannot separate red from green.
- Body text ≥ **4.5:1** contrast on its background; large text ≥ **3:1**.
- **Test the whole palette in greyscale.** If two states become indistinguishable, the design has
  failed regardless of how it looks in colour.

### Typography and layout

- **One typeface — the Windows system font (Segoe UI).** No downloaded fonts. Four sizes only:
  Display 24, Heading 12 (semibold), Body 10, Small 9 (pt). Two weights: regular + semibold — note
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

1. **READY** — nobody connected. Green dot + lock + "Nobody is connected". The **address is the
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

### What not to do

- No gradients, drop shadows, or glow. Flat surfaces, real (square) borders.
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

Replace the typed IP with a **6-digit code**, working across different networks. Relay:
ASP.NET Core, WebSocket over TCP 443, Linux VPS. Host registers and gets a code; codes
expire and are safely reused; collisions and guessing prevented; rate-limit code attempts.
Both programs switch to **outbound** connections. **Consent is built here, moved up from Stage 4**
(decided 2026-07-29): an incoming connection raises an **Accept / Reject** dialog on the client (with
the operator's ID/IP) that **times out to Reject after 30 s** — so the moment the program is reachable
over the internet, nothing connects without a human deciding. The indicator strip, session log and
packaging stay in Stage 4. **Add adaptive quality AND adaptive frame rate**,
driven by measured bandwidth and by whether the screen is changing. Two measured justifications:
(1) full-motion costs ~1–2 MB/s — impossible on a home upload — so quality must fall under bandwidth
pressure (the LAN-only quality-95 default is revisited here); (2) on the **GDI** path, polling at
30 fps while nothing changes burns a large fraction of a CPU core continuously (GDI copies the whole
screen every frame — ~16 ms on a fast client, ~48–76 ms on a slow one), so the client's fan runs the
whole session and they say the tool slowed their computer. The frame rate must drop toward ~1–2 fps on
a static screen and ramp up on change. Full first-time-Linux deployment writeup: provider/size with real monthly cost, domain question, every
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
  seconds; closing the window ends everything; target under **40 MB** (report the real size
  and what drives it). Document how to send it to a non-technical person, the exact
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
control is most needed. Capture and input move into a **Windows service running as LOCAL
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
client in person, no remote path to enabling it) · in-session chat · quality selector ·
H.264 with hardware encoding (only once JPEG tiles are **measured** too slow).

---

## Phase 2 — not now

Deliberately out of scope until the tool has served real clients for months: code-signing
certificate, antivirus whitelisting, installer, auto-update, NAT hole punching, licensing.
Different project, different shape (charter §2).
