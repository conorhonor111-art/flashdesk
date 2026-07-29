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

**Not yet verified:** the first live input test. Kill-switch drill to be done first.
