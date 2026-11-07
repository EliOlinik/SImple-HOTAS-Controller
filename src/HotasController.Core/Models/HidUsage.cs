namespace HotasController.Core.Models;

/// <summary>Top-level HID usage (page + usage) of a HID interface.</summary>
public readonly record struct HidUsage(ushort Page, ushort Usage)
{
    public const ushort GenericDesktopPage = 0x01;

    public bool IsGameController =>
        Page == GenericDesktopPage && Usage is 0x04 or 0x05 or 0x08; // Joystick, Game Pad, Multi-axis

    public bool IsKeyboardOrMouse =>
        Page == GenericDesktopPage && Usage is 0x02 or 0x06 or 0x07; // Mouse, Keyboard, Keypad
}
