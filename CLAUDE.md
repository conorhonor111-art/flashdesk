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
| My side (viewer) UI | WPF |
| Client side UI | WPF, single self-contained `.exe`, no install |
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

2. **The HOST is the machine my hands are on.** During input tests (Stage 2), `.222` drives
   `.223`'s mouse and keyboard while I am physically sitting at `.223`. I need a kill switch
   that works **from `.223` itself and depends on neither the network nor the app**:
   - **Guaranteed kill switch: press `Ctrl+Alt+Delete` on `.223`'s own keyboard.** Windows
     handles this itself (the Secure Attention Sequence), on a secure desktop that a normal
     user-mode program cannot touch and `SendInput` cannot reach or suppress until Stage 6. It
     is immune to both the network and the app. From that screen choose **Task Manager →**
     find **`RemoteDesktop.Host` → End task**, which stops all capture and injection instantly.
   - A convenience in-app panic hotkey may be added later, but it depends on the app still
     working, so it is **not** the guaranteed switch. `Ctrl+Alt+Del` is.
   - **State this to me before I run the first input test, every time.**

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
- **DXGI Desktop Duplication may not work on the VM** — virtual display adapters often cannot
  provide it, so on the VM the GDI fallback is expected, not a bug. This is the reason the
  HOST role is pinned to the physical PC (see above).
- Conor sometimes pastes example values literally and sometimes runs a command on the wrong
  machine. Give commands fully filled in with real values, and always name the machine.

## Environment and commands

- .NET SDK **8.0.423**, git **2.55** — both on the **dev PC only**. The test machine needs
  nothing installed (from Stage 4 it receives a self-contained `.exe`).
- Project root: `C:\Dev\RemoteDesktop`. **This is not necessarily the folder a Claude Code
  session opens in** — see "Where this file must live" below. Use absolute paths or
  `git -C C:\Dev\RemoteDesktop`.

```
dotnet build                                      # build everything
dotnet run --project src\RemoteDesktop.Host       # runs on the HOST machine
dotnet run --project src\RemoteDesktop.Viewer     # runs on the VIEWER machine
```

Solution `RemoteDesktop.sln` contains three projects under `src\`:

| Project | Role | Framework |
|---|---|---|
| `RemoteDesktop.Host` | Screen capture + serving (runs on the controlled machine) | `net8.0-windows`, WinForms in Stage 1; charter §5 moves UI to WPF |
| `RemoteDesktop.Viewer` | Display + control (runs where I sit) | `net8.0-windows`, WinForms in Stage 1; WPF per charter §5 |
| `RemoteDesktop.Shared` | Wire protocol both sides speak | `net8.0`, no UI, no Windows-only APIs |

Note: charter §5 specifies WPF for both UIs. Stage 1 was planned in WinForms because the
projects were scaffolded that way in Stage 0. This is a real discrepancy to resolve with
Conor before Stage 1 UI work — do not silently pick one.

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

- TCP port **7789** · tile size **128×128** · JPEG quality **70** — defined once in
  `Shared/Protocol/ProtocolConstants.cs`, never hard-coded elsewhere.
- Every message is length-prefixed with a type byte, so two frames can never run into each
  other on the wire.
- **Latency is measured by round-trip ping, never by comparing timestamps.** The two
  machines' clocks are not synchronised; subtracting them yields meaningless (sometimes
  negative) numbers.

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
Both programs switch to **outbound** connections. Full first-time-Linux deployment writeup:
provider/size with real monthly cost, domain question, every command with what it does, TLS
from scratch, auto-restart on crash/reboot, how to check status and read logs. Honest
bandwidth cost at 10 and 50 clients.

## Stage 4 — Safe to hand to a client  (= charter §4)

**Re-read charter §4 in full when this stage begins.** Everything the person on the other
end needs:

- Incoming connection → dialog showing who is connecting, **Accept / Reject**, **times out
  to Reject after 30 seconds**. Silence is never yes.
- Checkboxes putting the client in charge: allow keyboard/mouse, allow clipboard, allow file
  transfer. **The risky ones default to off.**
- While a session is live, a strip stays on top of everything on the client's screen with a
  **Disconnect** button. The operator must have **no way to hide, move or suppress it** —
  the enforcing code must be pointed out explicitly, not merely claimed.
- Plain-text session log: start, end, operator ID, operator IP. A button opens it in Notepad.
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
