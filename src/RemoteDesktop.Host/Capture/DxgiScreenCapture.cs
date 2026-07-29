using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RemoteDesktop.Host.Capture;

/// <summary>
/// DXGI Desktop Duplication capture — the fast path. Asks Windows, through the GPU, for the desktop
/// image and only wakes when it changes. Needs a real graphics adapter, so construction can fail on
/// a virtual machine; the factory catches that and falls back to GDI.
/// </summary>
public sealed class DxgiScreenCapture : IScreenCapture
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _staging;
    private readonly byte[] _buffer;

    public CaptureMethod Method => CaptureMethod.Dxgi;
    public int Width { get; }
    public int Height { get; }

    public DxgiScreenCapture()
    {
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null,
            out ID3D11Device? device,
            out ID3D11DeviceContext? context).CheckError();

        _device = device!;
        _context = context!;

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        adapter.EnumOutputs(0, out IDXGIOutput? output).CheckError();
        using var output0 = output!;
        using var output1 = output0.QueryInterface<IDXGIOutput1>();
        _duplication = output1.DuplicateOutput(_device);

        var desc = _duplication.Description;
        Width = (int)desc.ModeDescription.Width;
        Height = (int)desc.ModeDescription.Height;

        var stagingDesc = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };
        _staging = _device.CreateTexture2D(stagingDesc);
        _buffer = new byte[Width * Height * 4];
    }

    public bool TryCapture(int timeoutMilliseconds, out CapturedFrame frame)
    {
        frame = default;

        Result result = _duplication.AcquireNextFrame((uint)timeoutMilliseconds, out _, out IDXGIResource? desktopResource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            return false;                       // screen unchanged within the timeout
        result.CheckError();

        try
        {
            using var texture = desktopResource!.QueryInterface<ID3D11Texture2D>();
            _context.CopyResource(_staging, texture);

            MappedSubresource map = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
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
                _context.Unmap(_staging, 0);
            }

            frame = new CapturedFrame(Width, Height, _buffer);
            return true;
        }
        finally
        {
            desktopResource?.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    public void Dispose()
    {
        _staging.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
