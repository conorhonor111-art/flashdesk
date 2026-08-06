using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RemoteDesktop.Host.Capture;

/// <summary>
/// DXGI Desktop Duplication capture — the fast path. The duplication is fragile: Windows revokes it
/// (ACCESS_LOST) on a UAC secure-desktop prompt, a lock, a resolution or monitor change, or a user
/// switch — all routine with a real client. Rather than let that crash the session, this recreates the
/// duplication (picking up any new resolution) and carries on, reporting each loss and recovery to the
/// <see cref="CaptureHealthLog"/>. If it truly cannot recover after a few seconds it throws, and
/// <see cref="ResilientScreenCapture"/> drops to GDI for the rest of the session.
/// </summary>
public sealed class DxgiScreenCapture : IScreenCapture
{
    private const int GiveUpAfterMs = 30_000; // ~30 s of continuous failure before giving up on DXGI — a
                                              // lock/UAC recovers well inside this, and it is rate-
                                              // independent (a tick count would give up sooner at 2 fps).

    private readonly CaptureHealthLog? _health;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private byte[] _buffer = Array.Empty<byte>();
    private long _failingSince; // Environment.TickCount64 when the current failure streak began; 0 = healthy

    /// <summary>Which DXGI output is being duplicated. Changed only through <see cref="SwitchTo"/>.</summary>
    private int _outputIndex;

    public CaptureMethod Method => CaptureMethod.Dxgi;
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <inheritdoc/>
    public int OriginX { get; private set; }

    /// <inheritdoc/>
    public int OriginY { get; private set; }

    /// <summary>
    /// Capture a different screen. Returns false if the new output cannot be duplicated, having
    /// already fallen back to a working one — a failed switch must never leave the session with no
    /// picture at all.
    ///
    /// <para><b>⚠ UNVERIFIED ON REAL HARDWARE.</b> This has only ever run on a machine with one
    /// screen, where it is a no-op that rebuilds the same output. The path that actually changes
    /// screens has never been executed.</para>
    /// </summary>
    public bool SwitchTo(int outputIndex)
    {
        if (outputIndex < 0) return false;

        int previous = _outputIndex;
        DropDuplication();
        _outputIndex = outputIndex;

        if (EnsureDuplication()) return true;

        _outputIndex = previous;
        EnsureDuplication();
        return false;
    }

    public DxgiScreenCapture(CaptureHealthLog? health = null)
    {
        _health = health;

        D3D11.D3D11CreateDevice(
            null!, // default adapter
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device? device,
            out ID3D11DeviceContext? context).CheckError();

        _device = device!;
        _context = context!;

        if (!EnsureDuplication())
            throw new InvalidOperationException("DXGI Desktop Duplication is not available on this display.");
    }

    public bool TryCapture(int timeoutMilliseconds, out CapturedFrame frame)
    {
        frame = default;

        if (!EnsureDuplication())
        {
            if (_failingSince == 0) _failingSince = Environment.TickCount64;
            else if (Environment.TickCount64 - _failingSince >= GiveUpAfterMs)
                throw new InvalidOperationException("DXGI Desktop Duplication could not recover.");
            return false;
        }

        Result result = _duplication!.AcquireNextFrame((uint)timeoutMilliseconds, out _, out IDXGIResource? desktopResource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            return false; // screen unchanged within the timeout

        if (result == Vortice.DXGI.ResultCode.AccessLost)
        {
            // The UAC prompt / lock / resolution-change path. Rebuild the duplication next tick.
            _health?.Interrupted(CaptureMethod.Dxgi, "DXGI access lost (UAC prompt, lock, or resolution/monitor change)");
            DropDuplication();
            return false;
        }
        result.CheckError(); // anything else is unexpected — let the wrapper fall back to GDI

        try
        {
            using var texture = desktopResource!.QueryInterface<ID3D11Texture2D>();
            _context.CopyResource(_staging!, texture);

            MappedSubresource map = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int stride = Width * 4;
                for (int y = 0; y < Height; y++)
                {
                    IntPtr src = map.DataPointer + y * (int)map.RowPitch;
                    Marshal.Copy(src, _buffer, y * stride, stride);
                }
            }
            finally
            {
                _context.Unmap(_staging!, 0);
            }

            _failingSince = 0;
            frame = new CapturedFrame(Width, Height, _buffer);
            return true;
        }
        finally
        {
            desktopResource?.Dispose();
            _duplication!.ReleaseFrame();
        }
    }

    // (Re)create the duplication if we don't have one, adopting a new resolution if it changed. Returns
    // false — without throwing — when it cannot be created right now (e.g. during a mode switch).
    private bool EnsureDuplication()
    {
        if (_duplication != null) return true;
        try
        {
            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            // ⚠ THE OUTPUT INDEX IS THE ONLY THING THAT CHANGES WHEN SWITCHING SCREENS, and that is
            // deliberate: switching is the SAME operation as recovering from ACCESS_LOST — drop the
            // duplication, build a new one — so it reuses this path rather than adding a second
            // implementation of the same thing. Whatever hardens recovery hardens switching.
            // If the chosen output has gone (a monitor unplugged), fall back to the first one rather
            // than failing: a session must never end because a screen was disconnected.
            if (adapter.EnumOutputs((uint)_outputIndex, out IDXGIOutput? output).Failure || output is null)
            {
                _outputIndex = 0;
                adapter.EnumOutputs(0, out output).CheckError();
            }

            using var chosen = output!;
            using var output1 = chosen.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(_device);

            // Where this screen sits on the whole virtual desktop. Zero for a single-screen machine,
            // and NEGATIVE for a screen placed to the left of or above the primary — which is what
            // the input mapping needs and what it would otherwise have to guess.
            var coords = chosen.Description.DesktopCoordinates;
            OriginX = coords.Left;
            OriginY = coords.Top;

            var desc = _duplication.Description;
            int w = (int)desc.ModeDescription.Width;
            int h = (int)desc.ModeDescription.Height;
            if (w != Width || h != Height || _staging is null)
            {
                Width = w;
                Height = h;
                _staging?.Dispose();
                _staging = _device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)w,
                    Height = (uint)h,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    MiscFlags = ResourceOptionFlags.None,
                });
                _buffer = new byte[w * h * 4];
            }

            _failingSince = 0;
            _health?.Recovered(CaptureMethod.Dxgi); // no-op unless we were interrupted
            return true;
        }
        catch
        {
            _health?.Interrupted(CaptureMethod.Dxgi, "DXGI duplication could not be recreated yet");
            DropDuplication();
            return false;
        }
    }

    private void DropDuplication()
    {
        _duplication?.Dispose();
        _duplication = null;
    }

    public void Dispose()
    {
        DropDuplication();
        _staging?.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
