using System;

namespace MinecraftHelper.Services;

internal static class BackgroundTextInput
{
    private const uint KeyDown = 0x0100;
    private const uint KeyUp = 0x0101;
    private const uint Character = 0x0102;
    private const int UnicodePacketKey = 0xE7; // VK_PACKET; LWJGL maps it to KEY_NONE.

    internal static bool SendCharacter(char character, Func<uint, IntPtr, IntPtr, bool> post)
    {
        // A bare WM_CHAR may attach to the previous T key-UP, or be decoded
        // with state=UP. Give every character its own pressed event. VK_PACKET
        // carries no physical letter/bind and needs no global Shift/AltGr state.
        // Scan code zero avoids generating a second printable character through
        // TranslateMessage; the exact UTF-16 character is supplied by WM_CHAR.
        if (!post(KeyDown, (IntPtr)UnicodePacketKey, (IntPtr)1))
            return false;

        bool sent;
        bool released = false;
        try
        {
            sent = post(Character, (IntPtr)character, (IntPtr)1);
        }
        finally
        {
            // A partial send must not leave the carrier pressed in LWJGL.
            released = post(KeyUp, (IntPtr)UnicodePacketKey, unchecked((IntPtr)(int)0xC0000001));
        }

        return sent && released;
    }
}
