using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>What kind of input a single event carries.</summary>
public enum InputEventKind : byte
{
    MouseMove = 1,
    MouseButton = 2,
    MouseWheel = 3,
    Key = 4,
}

/// <summary>Which mouse button an event is about.</summary>
public enum MouseButton : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3,
}

/// <summary>
/// One input event travelling viewer -> host. Fixed 17-byte layout so it is trivial to frame and
/// parse. Mouse positions are already in HOST pixels — the viewer does the coordinate mapping, so
/// the host just injects. Keys travel as a hardware **scan code**, not a character, so a Georgian
/// layout on the viewer produces the correct physical key on a host set to a different layout.
/// </summary>
public readonly record struct InputEvent(
    InputEventKind Kind,
    int X,
    int Y,
    MouseButton Button,
    bool IsDown,
    int WheelDelta,
    ushort ScanCode,
    bool Extended)
{
    public const int Size = 17;

    public static InputEvent MouseMove(int hostX, int hostY) =>
        new(InputEventKind.MouseMove, hostX, hostY, MouseButton.None, false, 0, 0, false);

    public static InputEvent MouseButtonEvent(MouseButton button, bool isDown, int hostX, int hostY) =>
        new(InputEventKind.MouseButton, hostX, hostY, button, isDown, 0, 0, false);

    public static InputEvent MouseWheel(int wheelDelta, int hostX, int hostY) =>
        new(InputEventKind.MouseWheel, hostX, hostY, MouseButton.None, false, wheelDelta, 0, false);

    public static InputEvent Key(ushort scanCode, bool isDown, bool extended) =>
        new(InputEventKind.Key, 0, 0, MouseButton.None, isDown, 0, scanCode, extended);

    public byte[] ToBytes()
    {
        var b = new byte[Size];
        b[0] = (byte)Kind;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), X);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), Y);
        b[9] = (byte)Button;
        byte flags = 0;
        if (IsDown) flags |= 0x01;
        if (Extended) flags |= 0x02;
        b[10] = flags;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(11), WheelDelta);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(15), ScanCode);
        return b;
    }

    public static InputEvent FromBytes(ReadOnlySpan<byte> b)
    {
        if (b.Length < Size) throw new InvalidDataException("InputEvent too short.");
        byte flags = b[10];
        return new InputEvent(
            (InputEventKind)b[0],
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(1)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(5)),
            (MouseButton)b[9],
            (flags & 0x01) != 0,
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(11)),
            BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(15)),
            (flags & 0x02) != 0);
    }
}
