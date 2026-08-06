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

**The product is named FlashDesk (2026-07-30). The domain is `flashdesk.org` (bought,
confirmed by Conor same day).** FlashDesk appears everywhere a person can see; internals
deliberately keep the RemoteDesktop name — see "Naming — FlashDesk" under Project facts.

### ⚠ SCOPE CHANGED 2026-08-03 — Conor moved the goal from Phase 1 to Phase 2

**The product is no longer "a private tool for me and my clients". It is now: two strangers,
neither technical, neither having seen it before, both download from flashdesk.org and connect
to each other. Conor is not present and cannot talk anyone through anything.** He made this
decision knowingly; it is recorded here because it invalidates several Phase-1 assumptions that
are written elsewhere in this file:

- **The allow-list stops being the primary defence** (it assumed exactly one operator). The
  consent dialog becomes the only thing between a user and a social engineer — see "The consent
  dialog is now the only defence" below.
- **Auto-reconnect moves from Stage 7 to essential.** Two strangers on a phone call cannot
  repeat the code exchange because a link blinked (the measured reboot gap alone is 16 s).
- **Code signing moves from "Phase 2, not now" to a real, priced decision** — see the SmartScreen
  section below.
- **Relay bandwidth and abuse exposure are now other people's sessions, not Conor's own.** At
  some volume this brings cost, abuse reports and takedown requests. Not yet actioned; recorded
  so it is not a surprise.
- Everything about *how we work* (one stage at a time, verify what you report, plain language)
  is unchanged.

### 🚦 THE STRANGER RULE (Conor, 2026-08-03) — in force until the first real two-person test

**Until two real people who have never seen FlashDesk connect to each other across two real home
connections, the only work that counts is work that changes what a STRANGER experiences.** If a
change is invisible to someone who downloads the file and connects once, it waits — no matter how
correct, how cheap, or how satisfying it is to fix.

What that includes, and what it does not:
- **Counts:** the download page and everything on it; the SmartScreen and download warnings; the
  simple view of the host window — the number, Copy, the peer-number field, Connect, Stop sharing;
  the consent dialog; the live indicator; anything that decides whether a session connects at all,
  stays connected, or is watchable over a real home upload.
- **Does NOT count:** the technical view; the operator's own session window; internals with no
  visible effect; documentation tidy-ups; anything on a screen a first-time stranger never opens.

**Why this rule exists, in Conor's words:** the first thing two strangers experience must not be the
thing we have not fixed, while the thing we just polished sits on a screen neither of them will
open. This rule was written after exactly that happened — a dropdown chevron in the technical view
and checkboxes on the operator's own window were refined while stranger-facing risks were still
open.

**Design is closed, and this is what that means now:** the only visual work still permitted is on a
surface a first-time stranger sees on their own screen. Everything else is finished until the first
two-person test says otherwise.

### Phase 1 — the original scope, kept for the reasoning behind existing decisions

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

1. **The client must accept every session by hand.** An incoming connection shows a dialog with
   **the caller's verified 9-digit number** and Accept / Reject buttons. Timeout means Reject.

   ⚠️ **THE DIALOG SHOWS THE NUMBER AND NOTHING ELSE. NEVER A NAME.** This rule said "my ID and
   name" until 2026-08-06, and that was a hole waiting to be dug: **a name is whatever the caller
   types, so a scammer would put "Microsoft Support" in it** — and the one window standing between
   a stranger and their machine would be helping the lie. The 9-digit number cannot be chosen; the
   relay binds it to a secret and refuses anyone claiming an ID that is not theirs, so it is the
   only identifier on that screen that has been *verified* rather than *asserted*.
   Checked 2026-08-06: no peer-supplied name exists anywhere today — not on the wire, not in the
   relay handshake, not in the dialog. The danger was never a bug in the code; it was **this
   sentence**, which invited a future session to add the field in good faith. It is fixed here so
   that cannot happen. The same rule governs every later consent surface, including the file-access
   and overwrite prompts: **identify the caller by the verified number, never by anything they
   chose for themselves.**
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
- **Domain purchase — DONE 2026-07-30: `flashdesk.org`.** (The earlier where-to-buy question is
  moot; registrar details to be collected when the DNS record is added.)
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
- **The relay VPS is KEY-ONLY, and that is not optional at this provider.** DeltaHost's
  generated root passwords proved unreliable (2026-08-03: the emailed password was rejected by
  both the KVM console and SSH; support had to reset it manually). Key access via
  `C:\Users\PC\.ssh\flashdesk_relay` is the only sane route — password login is switched off on
  the server. Also note for anyone editing SSH config there: files in `/etc/ssh/sshd_config.d/`
  are read in NAME order and the FIRST value wins, so Ubuntu's `60-cloudimg-settings.conf`
  silently overrode a `99-` file; our policy lives in `10-flashdesk.conf` and the cloud-image
  line is commented out. Always run `sshd -t` before restarting, or a typo locks everyone out.
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

**The publish command that produces the file people download — ONE literal line.** It was missing
from this repo until 2026-08-04, so rebuilding depended on someone's shell history:

```
dotnet publish src\RemoteDesktop.Host -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o C:\Users\PC\Desktop\flashdesk-upload
```

⚠️ **CHECK WHICH BUILD IS ACTUALLY ON THE SERVER BEFORE ANY TEST.** The exe carries the git commit
it was built from, so this is one command and it is not optional:
`(Get-Item FlashDesk.exe).VersionInfo.ProductVersion` → e.g. `0.3.0+84c3bab`, then
`git log --oneline <that hash>..HEAD` lists exactly what the downloaded file is MISSING.
This caught a real one on 2026-08-04: the file on flashdesk.org was built at `b6734e0`, **twelve
commits behind, and therefore had no adaptive quality at all** — the feature the whole
slow-connection story depends on. The site looked perfect and served a build without it.

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

- TCP port **7789** · tile size **128×128** · JPEG quality **starts at 95** and is then moved by the
  bandwidth governor (60–95) — all defined once in `Shared/Protocol/ProtocolConstants.cs`, never
  hard-coded elsewhere. **Adaptive quality and frame rate BUILT 2026-08-03** — quality 95 was a
  LAN-only default and is now only a starting point; see "Adaptive quality — the three findings that
  shaped it" below before changing any of it.
- Every message is length-prefixed with a type byte, so two frames can never run into each
  other on the wire.
- **Latency is measured by round-trip ping, never by comparing timestamps.** The two
  machines' clocks are not synchronised; subtracting them yields meaningless (sometimes
  negative) numbers.

## Adaptive quality — the three findings that shaped it (BUILT 2026-08-03)

`Host/Net/BandwidthGovernor.cs` decides frame rate and quality from the measured link. Everything
below was **measured while building it**, and each item is here because a plausible-sounding
alternative was tried first and was wrong. `FlashDesk.exe --linktest <path> <secondsPerRun>`
reproduces all of it (simulated link, real encoder, real governor, no network — safe to run over
RDP, unlike throttling the machine's real network).

1. **It is ONE ladder of (fps, quality) pairs, not two independent controls, and frame rate is spent
   before quality.** Two controllers need an ordering rule that has no stable answer and is where
   oscillation comes from. The order inside the ladder is forced by an existing measurement in this
   file: quality 95 costs only ~7 % more bytes than quality 70 on REAL content, so quality 95→60
   saves about a tenth of the bytes while 30→8 fps saves nearly three quarters. Frame rate is the
   lever; quality is a trim. This also keeps quality at 95 through the first five steps, protecting
   text legibility, which is the primary quality metric. **Level 0 of the ladder is exactly the old
   fixed behaviour, so nothing measured on the LAN regresses.**

2. **The congestion signal is send time ÷ intended frame interval — NOT the fraction of time spent
   sending.** The duty-cycle version was built first and measured wrong: a healthy link running near
   capacity legitimately spends most of its time sending, so it kept backing off a link with nothing
   wrong with it (22 fps where 29 were available). Being busy is not the same as being late. The
   threshold is deliberately **below 1** (0.85): filling a link completely means every frame waits
   behind the last one's bytes, so the picture is permanently a frame behind. This is a remote
   CONTROL tool — latency beats throughput, and roughly a third of the link is left unused to buy
   the delay back.

3. **⚠️ THE ROUND-TRIP FEEDBACK IS LOAD-BEARING — do not remove it as "extra protocol".** The viewer
   reports its last measured round trip inside its next Ping (4 bytes, no extra traffic —
   `PingPayload`). Without it the sending side is **blind to an ordinary home router**: while a deep
   buffer fills, every send still returns instantly, so all local signals read healthy while the
   picture slides seconds behind. Measured, 0.6 MB/s link with 1 MB of router buffering, typical
   support screen: with send timing alone the picture settled **1.85 s behind and never recovered**;
   with the round-trip signal it settles at **0.32 s**. The comparison is against the session's OWN
   best round trip, never an absolute — a machine 2000 km away starts at 60 ms with nothing wrong.

**Two supporting pieces that are easy to mistake for optional:**

- **`TileDiffer` remembers the quality each tile was last sent at, and re-sends stale ones a few at a
  time when there is room.** Without this, adaptive quality PERMANENTLY damages the picture: a tile
  is only re-sent when its pixels change, so text that was softened during a burst of motion and
  then stopped moving stays soft for the rest of the session. The refresh is capped (6 tiles/frame,
  only while under 8 tiles changed) so the screen settles rather than flashes.
- **`IScreenCapture.TryCapture` must leave the buffer untouched when it returns false.** The refresh
  pass depends on it — a still screen is exactly when there are no fresh pixels to work from. Both
  implementations satisfy it today; the contract is now written in the interface.

**Honest limits, so nobody oversells this:** full-screen motion on a 0.6 MB/s uplink is still not
possible — adaptation takes the settled delay from ~2.5 s to ~2.1 s there and no further, because the
link genuinely cannot carry it. What adaptation fixes is the SUPPORT session (a mostly-still screen
with something moving in it), which is the actual product. And all of it is still measured against a
simulated link; two real home connections remain unproven.

**Client-invisible by rule (Conor, 2026-08-03).** Adaptation never appears in the simple view: no
flicker, no bandwidth message, no number. The client cannot act on it, and a "your connection is
slow" line during a support call reads as "this is broken". Every readout lives in the technical
view, on one line.

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

- **⚠ THE APP still uses only the Windows system font. THE DOWNLOAD PAGE does not, since
  2026-08-06** — Conor lifted the no-downloaded-fonts rule for the site alone, because typeface is
  most of the difference between a page that looks typed and a page that looks made. One
  self-hosted face (Inter, latin + punctuation, **24.2 KB measured**, `font-display: swap`, with a
  metric-matched `size-adjust: 108.6%` fallback so nothing reflows when it arrives). **The quoted
  Windows and Chrome dialogs on that page stay in the SYSTEM font**, because the page's whole
  method is that a frightened person matches those words against the real box on their screen, and
  the real box is drawn in Segoe UI. Two traps found while doing it: subsetting by "characters
  currently on the page" gives 16.3 KB but silently drops the ellipsis, en dash and `&nbsp;`, so
  subset BY UNICODE RANGE; and Inter is 8.6 % wider than Segoe UI, so without the size-adjust the
  page visibly reflows on load.
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

**⚠ THE MARK WAS REFITTED 2026-08-06, AND ITS GEOMETRY NOW LIVES IN FIVE PLACES THAT MUST CHANGE
TOGETHER.** The shape was never reopened — the seven rounds were about what the mark IS, and every
angle, the P3 proportion and the colours are untouched. What was wrong was that **it did not fit
its own tile**: rendered and measured, the stroked outline spanned y 2.219–15.844 against a tile
interior of 1.000–15.000, so the miter tip hung **0.841 grid units below the tile** and touched the
canvas edge. It was also off-centre — 2.656 units of clear tile on the left against 1.437 on the
right. On a LIGHT taskbar the protruding tip read as one stray green pixel; on a dark one it was
invisible, because there the tile itself is invisible (`#17191E` on a dark panel = 1.08:1).
The fix scales the PATH by 0.8807 and re-centres it, and **deliberately does NOT scale the stroke**:
scaling it too gave 2.290 and dropped the 16 px mark from 37 solid-core pixels to 29 with no solid
pixel left at the bottom terminal — trading a defect visible at 32 px and above for a worse one at
16 px, on the taskbar, which is the surface the icon exists for. New points
**(4.631, 2.512) (9.387, 11.495) (11.325, 7.620)**, stroke **2.6** unchanged; the mark now sits with
2.500 units of clear tile left and right, 0.906 above and 0.656 below.
**The five places:** `assets\make-icon.ps1`, the favicon data URI in `site\index.html`, the header
SVG in `site\index.html`, `site\holding.html` (BOTH of its copies — this one is easy to miss), and
the two regenerated `.ico` binaries. A site and an app wearing different marks is worse than the
defect that was fixed. `scratchpad\fit-mark.ps1` and `stroke-test.ps1` reproduce the measurements.

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

### The 2026-08-06 design pass — what changed, and the four traps it found

Everything below was found by RENDERING the three surfaces and measuring them, not by reading code.
Three of the four had survived every previous review precisely because nobody had looked at a
picture.

1. **Amber was being used to mean "this caller is new"** in the consent dialog, and amber has
   exactly one meaning in this product: *a session is LIVE, someone is watching*. So the first
   amber a person ever saw was on a window where nothing was live — which is how a safety colour
   stops meaning anything. It ALSO failed contrast: `#B26A00` on `#F5F6F8` at 12 px measures
   **3.92:1** against a 4.5:1 floor. Now primary ink at 15.31:1, with the two cases kept apart by
   their words, which is what the no-colour-alone rule actually asks for.
2. **Focus landed in the peer-number field on startup**, so the field hid its own "their number"
   placeholder and drew a 2 px blue focus ring — blue being the colour reserved for *a decision is
   being asked of you*. A frightened stranger's first sight was an empty blue-ringed box demanding
   input, directly under their own number. Fixed by clearing `ActiveControl` on `Shown`.
3. **The mark did not fit its own tile** — the miter tip hung **0.841 grid units** below it and the
   side margins were 2.656 vs 1.437. Fixed as a scale + translate; see `assets\make-icon.ps1`.
4. **`Theme.Caption` hard-sets Small AND TextSecondary**, so the two most safety-critical sentences
   in the product were automatically the smallest, palest text on their own surface. `Theme.Note`
   now exists for a sentence that must be read. Do not "simplify" it back into Caption.

**Two new rules that came out of it:**
- **`Theme.Small` (9 pt = 12 px) is the floor, and nothing a stranger must ACT on may use it.**
  It is for labels, never for instructions or warnings.
- **A disabled control must stay legible.** WCAG exempts disabled components from contrast, so this
  is not a rule fix — but the consent dialog's Accept is deliberately disabled for three seconds
  *so that the person reads*, and at 2.76:1 inside a 1.30:1 border it read as broken instead. A
  person who jabs at a dead button clicks the instant it lights, which is a MORE reflexive Accept
  than no delay at all.

**One thing measured and deliberately NOT changed:** the app's card borders are `#C6CCD4` at 1.50:1
on the window and 1.62:1 on a card, below the 3.0:1 used for UI boundaries. Left alone because the
cards group content rather than being interactive controls, and changing `Theme.Border` would repaint
every surface in the app including several out of scope. Recorded so it is a decision, not an
oversight.

**And one proposal of my own that the review killed:** I claimed the Connect card had dead space.
Measured, the gap below the button is 36 px, of which 32 is the card's own padding plus the button's
margin — the design system correctly applied. I had misread a render. Nothing was removed.

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

### The download site — flashdesk.org (spec'd by Conor 2026-07-30; built as one static file)

**The site is NARROW on purpose:** one situation — Conor is on the phone with a client whose
computer is broken; they go to flashdesk.org, get the file, run it, read out a number. No
marketing, no pricing, no features grid, no cookie banner, no analytics. Anything that is not
"get the file and run it" lengthens the phone call. The client may be on their PHONE (the broken
machine is the one that needs help) — phone-first layout, and one line for exactly that case:
"Open this page on the computer you need help with."

- **The page's real work is TRUST, not looks:** Conor's real name and a real contact that is not
  the website; a plain what-it-does/what-it-does-not section ("you see everything; close it and
  it is gone; you can end it any time"); NOTHING invented — no fake company, no "trusted by
  thousands", no fake address. Placeholders stay visible placeholders until Conor supplies real
  details.
- **The scam warning is not optional** and lives where it will be read, not in a footer, verbatim:
  "If someone phoned you out of the blue and told you to come here, hang up. Only download this
  if you contacted us yourself."
- **The SmartScreen paragraph is on the page BEFORE it happens** ("Windows shows a blue warning
  for new software. Click More info, then Run anyway.") — otherwise every first run produces a
  frightened phone call at that exact moment.
- **Page order (fixed):** small mark+name · one line what it is · THE download button (file size,
  Windows, above the fold on phone) · three read-aloud steps · the SmartScreen paragraph · what
  it does/does not · the scam warning · who I am + contact. Nothing else.
- **Palette lock (site ↔ Theme, must never drift):** page background = `Theme.BrandTile #17191E`;
  the ONE clickable green = `Theme.BrandGreen #2BD16B`; text = `Theme.OperatorHeaderText #ECEFF3`.
  System fonts only, no downloaded fonts, no animation, no JavaScript. Recorded as a comment in
  `Theme.cs` and in `site/index.html` — a change in one place must be repeated in the other.
- **The download URL is STABLE forever.** ⚠️ **What is actually deployed is NOT `/download`** —
  verified live 2026-08-03, do not trust the older sentence that said otherwise. Conor asked for
  the button to point straight at the file, so there is **no redirect and `/download` returns
  404**. The live, stable URL is **`https://flashdesk.org/dl/FlashDesk.exe`**. It stays stable the
  same way: a new version REPLACES that file, the URL never changes. This matters beyond
  tidiness — SmartScreen reputation accrues to a file+URL, so changing either resets the little
  reputation that has been earned.

### 🚨 THE DOWNLOAD BUTTON POINTS AT GITHUB, NOT AT flashdesk.org (2026-08-05) — DO NOT "TIDY" IT BACK

Someone will eventually look at the page, see the green button pointing at `github.com` on a site
whose whole argument is "this is my own domain, my own name", and want to point it back at
`flashdesk.org/dl/FlashDesk.exe`. **That change would break the product for most people.** Here is
the evidence, so the argument is found before the edit is made.

**THE PROBLEM.** Chrome did not warn about the download from flashdesk.org — it BLOCKED it. It
transferred the whole 68.5 MB and then refused to hand it over, leaving it in the Downloads folder
as `Unconfirmed 815538.crdownload` and showing only **"Suspicious download blocked"**. Behind a small
chevron: *"This file isn't commonly downloaded and it may be dangerous"*, with **"Delete from
history"** as the solid dark blue primary button and **"Download suspicious file"** as the pale
secondary. So a path existed, but it was three clicks with the word *dangerous* in them, and Google
had deliberately made the eye land on *delete* first. For a frightened non-technical stranger that
is where the product ended.

**THE EXPERIMENT — one variable, measured, not reasoned.** The same file was published as a GitHub
release and downloaded in a fresh Chrome profile on the same machine minutes later:

| | flashdesk.org | GitHub release |
|---|---|---|
| bytes | 71,784,025 | 71,784,025 |
| SHA-256 | 72E45B4E…249B19 | **identical** |
| signature | NotSigned | NotSigned |
| file reputation | none | none |
| **result** | **BLOCKED** | **downloaded cleanly, no warning at all** |

Two weaker tests preceded it and are recorded so nobody repeats them thinking they settle anything:
a SIGNED installer from GitHub (Git for Windows) downloaded cleanly — but it differs in two ways at
once, host and signature, so it proves only that the test rig reports "not blocked" correctly. An
UNSIGNED but hugely popular tool (yt-dlp) also downloaded cleanly — but it carries years of its own
file reputation. **Only the FlashDesk test held the file constant and changed nothing but the host**,
which is why it is the one that counts.

**THE CONCLUSION: it was never the file. It was the host.** A small domain the browser had never
seen distributing software. Nothing about FlashDesk itself was the problem.

**WHAT THIS CHANGED.** The code-signing certificate (~€209/year) stopped being urgent. It had been
the planned rescue for a barrier that stopped everyone; that barrier is gone and it cost nothing.
Signing is now only a later decision about the remaining Windows warning — see the code-signing
section below, whose "buy at ~100 downloads a month" trigger was already withdrawn as
self-defeating.

**RULES THAT COME WITH THIS — all three matter:**
1. **The link must keep the `/releases/latest/download/FlashDesk.exe` form.** A tag-specific URL
   changes with every release and throws away whatever reputation the address has earned — the same
   trap already documented for `/dl/FlashDesk.exe`.
2. **`flashdesk.org/dl/FlashDesk.exe` stays live and stays on the page**, offered by name as a
   fallback and described honestly (identical file, browsers are more suspicious of it there).
3. **The page must not describe a block that no longer happens.** The Windows warning is step one
   now, because it is the only thing that certainly still occurs. The download-block wording is kept
   underneath as the "if it happens to you" case, since browser settings vary and the fallback link
   is still a bare `.exe` on a small domain. Describing a dialog nobody sees is the same class of
   failure as hiding one they do — this page has already made that mistake once.

**⚠️ THE TAKEDOWN RISK IS REAL, AND THE FALLBACK EXISTS FOR IT.** GitHub can remove a repository on
report, without notice, and a remote-control tool is precisely the category that attracts such
reports. Conor controls flashdesk.org; he does not control GitHub. **Verified 2026-08-05 that the
page survives that scenario:** `github.com` appears in `site/index.html` exactly ONCE, in the
button's `href`. There is no script, no image, no stylesheet, no external fetch of any kind. So if
the repository vanished, the page would render identically, the button would 404, and the fallback
link below it would still serve the same file from flashdesk.org (confirmed responding 200 with the
current build). The failure is degraded, not fatal. **Honest weakness:** the fallback sits under a
heading further down the page, so a person whose button 404s must read on to find it. If GitHub ever
does remove it, swap the button back the same day rather than relying on that.

### ✅ RESOLVED 2026-08-03 (was a hard blocker): flashdesk.org now has a REAL certificate

**Verified from `.223`, not assumed:** `issuer=C=GB, O=Sectigo Limited, CN=Sectigo Public Server
Authentication CA DV R36` and **`Verify return code: 0 (ok)`**. `https://flashdesk.org` returns 200,
the real download page is live (not the holding page), and `https://flashdesk.org/dl/FlashDesk.exe`
returns 200 with `Content-Length: 68,035,064` (~65 MB). The provider issued the certificate; the
support ticket did its job. **The hard gate below is CLEARED — the first two-person test is
unblocked.**

✅ **Both follow-up problems that were recorded here are FIXED — verified 2026-08-06. Do not
re-raise them, and do not "fix" them again:**
1. **`http://` DOES redirect now.** `curl http://flashdesk.org/` returns **301 → https://flashdesk.org/**.
2. **The polished page IS deployed.** The live page is **byte-identical** to `site/index.html`
   (16,114 bytes at the moment of checking) — established with `curl` + `diff`, not by looking at
   it in a browser.
The certificate still returns `Verify return code: 0 (ok)`, and both download URLs return 200 with
the same **71,784,025 bytes**. The original write-up below is kept because the RULE it taught —
check from OUTSIDE after any hosting change — is exactly what caught these.

⚠️ **Two files now have to be uploaded alongside `index.html`** (added 2026-08-06): `inter.woff2`
(24.2 KB, the page's typeface) and `flashdesk-window.png` (14.7 KB, the screenshot of the program).
If either is missing the page still works — the font falls back to Segoe UI with matched metrics,
and a missing image shows its alt text — but it will look unfinished. Deployment is three files, not
one.

The original blocker write-up is kept below unchanged, because the RULE it taught is still the most
valuable thing in this section and item 1 above proves it is still live.

---

Measured, not assumed: `openssl s_client` shows `subject=CN=flashdesk.org` and
`issuer=CN=flashdesk.org`, i.e. the site signed its own certificate — `Verify return code: 18
(self-signed certificate)`. This is cPanel's placeholder, installed automatically before a real
certificate is issued. (For contrast, `relay.flashdesk.org` returns `Verify return code: 0 (ok)`
because Caddy obtained a real Let's Encrypt certificate.)

**Consequence, and why it is a blocker rather than a cosmetic issue:** every visitor who types
`https://flashdesk.org` gets a full-page browser security warning before they see anything. On a
page whose whole job is persuading a wary stranger to download remote-access software, that
warning ends the conversation — and it *should*, because a self-signed certificate is exactly
what a fake site looks like. It also undoes the page's own trust work: the scam warning, the real
name, the honest explanations are all behind a screen saying the site cannot be trusted.

It went unnoticed because the page serves perfectly over **http://** (HTTP 200, correct content,
and `http://` is NOT redirected to `https://`), so anyone testing without typing the scheme sees
a working site.

**The fix is NOT in our hands (2026-08-03).** Conor's cPanel has no "Run AutoSSL" button — only
filters — so the certificate cannot be issued from the panel. He has opened a support ticket
asking the host to issue a proper certificate and enable AutoSSL. **There is nothing to build or
configure on our side; we are waiting on the provider.**

**HARD GATE: nothing is sent to any tester until `https://flashdesk.org` shows a padlock with no
warning.** Verify from `.223`:
`echo | openssl s_client -servername flashdesk.org -connect flashdesk.org:443 2>&1 | grep "Verify return code"`
— it must say `0 (ok)`.

### THE RULE THIS TAUGHT US — check the certificate from outside after ANY hosting change

**`http://` working proves NOTHING about `https://`.** That is exactly the trap we fell into: the
page served perfectly over http, http is not redirected to https, and so every check looked
green while every real visitor would have been stopped by a security warning. A browser on the
machine that made the change is not a check either — it may have cached, or the person may have
clicked through the warning weeks ago.

So the site gets the same treatment the relay already has. `relay.flashdesk.org/health` is the
one-glance check that the relay is alive; the site's equivalent is the certificate check above,
run **from `.223`, after every hosting change**, and it must return `0 (ok)`. Two commands, both
cheap, both to be run rather than assumed:

```
curl -sS -o /dev/null -w "%{http_code}\n" https://flashdesk.org
echo | openssl s_client -servername flashdesk.org -connect flashdesk.org:443 2>&1 | grep "Verify return code"
```

A non-zero verify code means the site is broken for every stranger, no matter how good it looks
from here.
- **Architecture (revised 2026-07-30 — Conor already OWNS hosting and wants it used):**
  `flashdesk.org` → his existing web hosting → the site + the `/download` file;
  `relay.flashdesk.org` → the small Warsaw VPS → the relay ONLY (unless his hosting proves able
  to run a long-lived custom process — assessed from his control-panel screenshot, never
  guessed). This makes the site/relay separation PHYSICAL: a broken site cannot touch the relay
  at all. Health check for Conor after any change: open `https://relay.flashdesk.org/health` —
  the relay answers OK + version; if that opens, the relay is alive. The Warsaw location
  decision is unchanged (14 ms / zero jitter, measured twice).
- **Until Stage 3 makes the file real, the public page must not lie:** the deployed root shows an
  honest one-line holding page (name + "being set up" + contact), with NO download promise; the
  real file lives at a private random URL only Conor knows, for his own cross-network testing.
  The full page goes live only when the 9-digit build actually works over the relay.
- **The installer question (Conor's own correction):** the public download stays ONE portable
  file — double-click and it runs, no admin. Inside the app, later, a quiet "Install on this
  computer" option (Apps & Features entry, uninstaller, firewall rule, autostart; warns BEFORE
  the admin prompt). **Install must keep the existing 9-digit ID** — implemented as the same
  adopt-never-regenerate rule as the Stage 6 migration; the install moment IS the migration
  moment (elevation is available and the installing user is known), so the two compose rather
  than collide.

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

## Code signing and SmartScreen — researched 2026-08-03, real numbers

**WINDOWS WARNS TWICE, NOT ONCE — found by Conor's own live test 2026-08-03, and this is the
correction that matters most in this section.** The page originally explained only the second
warning, and that was nearly a fatal gap:

1. **At DOWNLOAD time** — the browser (Chrome/Edge) says the file "isn't commonly downloaded" and
   offers to discard it; the person must open the downloads bubble and choose **Keep**.
2. **At RUN time** — Windows shows the blue "Windows protected your PC" box; the person must
   click **More info** then **Run anyway**.

**The download warning is the earlier and by far the more dangerous of the two, because a
stranger who stops there never reaches the second one** — and an unexplained warning at step one
of a process they were already nervous about is where they quit. Both are now explained on the
page, in that order, with a line under the download button pointing at them. Never remove one to
"simplify" the page; the pair is the point.

Every stranger downloading an unsigned exe hits both. Only a code signing certificate genuinely
fixes either, and even that does not fix them immediately (see below).

- **Since June 2023 the private key MUST live on a FIPS 140-2 hardware token or a cloud HSM.**
  The old "a .pfx file on your PC" route no longer exists at any CA.
- **Real prices (Certum, a European CA, cloud/SimplySign so no physical token to post):
  Standard (individual or organisation validated) €209/year · EV €379/year.** Physical-card sets
  are cheaper (Standard €169, EV €359) but involve shipping a smartcard.
  ⚠ Certum's €49 "Open Source Code Signing" is restricted to open-source projects and does
  **not** apply to FlashDesk — do not quote it as an option.
- **From 2026-02-27 a single code signing certificate may be valid for at most 459 days**, so
  this is a recurring cost, not one-off.
- **Signing does NOT remove the warning immediately, and EV no longer guarantees it either.**
  SSL.com states Microsoft has moved away from granting EV automatic instant reputation.
  Microsoft's own SmartScreen doc: *"If a URL, a file, an app, or a certificate has an
  established reputation, users don't see any warnings. If there's no reputation, the item is
  marked as a higher risk and presents a warning."* The useful part is that reputation attaches
  to the **certificate**, so it accrues across releases instead of resetting with every build.
- Expect **weeks to months of real downloads** before the warning stops, whichever certificate
  is bought.

**DECISION 2026-08-03 (Conor): LAUNCH UNSIGNED.** The reasoning, so a later session does not
"fix" this by spending money: signing does **not** remove either warning on the day you buy it —
it only lets reputation begin accumulating, and Microsoft has moved away from granting even EV
certificates instant reputation. Reputation needs weeks-to-months of *real* downloads; FlashDesk
has none yet. Meanwhile the 459-day validity clock (from 2026-02-27) runs whether anyone
downloads or not. So a certificate bought today would expire having earned almost nothing.
**Conor met this warning himself in a live test and it did not change the decision — precisely
because buying would not have prevented what he saw.**

### ⛔ THE OLD TRIGGER WAS WITHDRAWN — it was self-defeating (2026-08-04)

~~Roughly 100+ downloads a month from strangers, sustained over two or three months.~~ **Conor
demolished this and he was right:** the download barrier was what PREVENTED those downloads, so the
threshold could never arrive. A rule whose precondition is blocked by the thing it is meant to
decide about is not a rule. Recorded rather than deleted so nobody re-derives it.

### ✅ THE TRIGGER THAT REPLACES IT (Conor, 2026-08-05) — a person, not a number

> **Buy the certificate when a real tester stops at the SmartScreen screen and does not get through
> on their own — OR when Conor decides to promote FlashDesk beyond people he can telephone.**

**What €209/year actually buys, settled 2026-08-05 — and it is NOT the day-one publisher name:**

- **Without a certificate the blue box is PERMANENT.** SmartScreen reputation attaches to the FILE
  HASH, and every rebuild produces a new hash (this project embeds the git commit in the version, so
  every commit changes it). Reputation can therefore never accumulate. Waiting does not help. Ever.
- **With a certificate the box is TEMPORARY.** Reputation attaches to the CERTIFICATE and is
  inherited by every future version, so it accrues instead of resetting, and eventually the box
  stops appearing.
- Day one it changes exactly one line — `Publisher: Unknown publisher` becomes
  `Publisher: Conor Honor` — and the box still appears. That line matters, but it is not the
  purchase. **The purchase is turning a permanent obstacle into a temporary one.**

**Why waiting is currently free:** reputation only accrues on real downloads, and there have been
two. Nothing is being lost by not buying today.

⚠️ **And unlike the Chrome block, this one CANNOT be solved by moving the host.** SmartScreen judges
the file hash and the signing certificate; it shows `App:` and `Publisher:`, never
`Downloaded from:`. Moving to GitHub fixed Chrome because Google's Safe Browsing also weighs URL
reputation. Microsoft's does not. Do not retry that trick here.
⚠️ **The free Microsoft file submission is PER FILE HASH**, so every rebuild voids it. It is worth
doing only once a release has stopped changing — otherwise it is wasted effort.
**If it is ever bought, buy the CLOUD (SimplySign) version even though the card kit is ~€40
cheaper:** the card is a physical smartcard that has to be posted, which adds a delivery
dependency and a device to lose, and the cloud version has neither.

**What to do until (or instead of) signing — the free things, and their state:**
- ✅ **Exe metadata filled in** (done 2026-08-03, verified in file properties): Product
  `FlashDesk`, Company `FlashDesk`, Version `0.3.0.0`, and **File description
  `FlashDesk remote support`** — that last one is `<AssemblyTitle>`, NOT `<Description>`, and it
  is the line Windows shows in Task Manager *and inside the SmartScreen dialog itself*, so it
  must read as a sentence rather than a filename.
- ✅ Filename and download URL are fixed and never change, so whatever reputation accrues
  accrues in one place.
- ✅ The page explains the warning BEFORE it happens.
- ⬜ Add a screenshot of the real warning to the page, so it is recognised rather than frightening.
- ⬜ Submit to Microsoft's file-submission service if the binary is ever outright blocked.

## The consent dialog is now the only defence (2026-08-03)

With strangers connecting to strangers, the allow-list cannot be the primary protection. Honest
assessment given to Conor: **a consent dialog alone is NOT enough** — the entire tech-support
scam industry works by talking someone through exactly such a dialog, and AnyDesk and TeamViewer
both have one. A dialog defends against an accidental connection, not against social engineering.

Required additions, in order of how much they actually help:

1. **Say when it is a first contact.** If the connecting ID has never been accepted on this
   machine before, the dialog says so in plain words. Cheap to build, highest value.
2. **Put the scam warning inside the dialog**, not only on the website: "If someone phoned you
   unexpectedly and asked you to do this, press Reject."
3. **Delay the Accept button by ~3 seconds on first contact**, so "just click yes" cannot be
   reflexive. Reject stays available immediately, and Reject is never the harder button.
4. Session log, visible amber indicator and the always-present Disconnect strip (already
   specified) limit the damage of a session that should not have started.
5. Relay-side rate limiting per source, so IDs cannot be swept.

Honest limit that must not be papered over: none of this stops a determined social engineer, and
the category's real answer is user education — which is why the page's scam warning matters more
than any code we write.

**THE DIALOG IS ONE DIALOG (Conor, 2026-08-03) — this is a safety decision, not a convenience
one.** Everything the person needs to decide lives in a single window: who is connecting (their
9-digit number, and their name if known), **whether that number has ever connected to this
machine before, stated in words**, what they will be able to do (see the screen; control mouse
and keyboard), that it can be ended at any moment with one click, and the scam line in plain
words. Two buttons: Accept and Reject. **Reasoning that must survive into later sessions: asking
three times is WORSE than asking once — a person asked repeatedly learns to click through
without reading. One dialog that is actually read beats three that are dismissed. So the dialog
is never shortened to make it faster to pass; what we removed is repetition, not information.**
Reject is never smaller, greyer or harder to hit, and a timeout means Reject.

### 🔐 THE DIALOG SHOWS A NUMBER THE CLIENT HAD NOTHING TO COMPARE IT WITH (found + fixed 2026-08-06)

**This is a security fix, not a wording fix, and it is the most valuable thing the design review
produced.** Two independent reviewers found it separately, which is why it is recorded here rather
than only in the page copy.

The consent dialog's whole job is to let a person decide *who* is connecting. It shows the caller's
9-digit number in large type. But **nothing anywhere — not the page, not the app, not the steps —
ever told the client to obtain that number in advance.** So the number was unverifiable: a stranger
saw an authoritative-looking figure, had nothing to check it against, and pressed Accept anyway.
The check we built was decoration.

**The fix is one line on the download page**, now step 4 of "What to do": *"Ask them to read you
their number too, and check it matches the one that appears on your screen when they connect. If it
does not match, press Reject."* Without that line the first-contact warning is a speed bump; with
it, the dialog becomes an actual check.

**Consequence for the dialog itself:** the caller's number is now something a person READS OFF THE
SCREEN AND COMPARES against one spoken on a phone, so it is set in `Theme.DisplayStrong` (semibold
24 pt) rather than plain Display. It deliberately did **not** move up to `Theme.Hero`: Hero is the
client's OWN number, and that size difference is the only cue distinguishing "your number" from
"their number". Making them the same size would remove it — and a scammer's number would render
exactly as large and as authoritative as anyone's.

**First-contact delay:** on the FIRST connection from a given number, Accept becomes clickable
after a short delay and the dialog says plainly that the number is new; on EVERY later
connection from that same number there is no delay at all. The delay exists because the scam
depends on a reflexive click while a stranger talks on the phone — it is the difference between
a reflex and a decision, and costs an expected caller nothing. Reject is clickable immediately,
always. The known-numbers list lives only on the client's machine and is never transmitted.

## Phase 2 — not now

Deliberately out of scope until the tool has served real clients for months: code-signing
certificate, antivirus whitelisting, installer, auto-update, NAT hole punching, licensing.
Different project, different shape (charter §2).
