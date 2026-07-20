using System.ComponentModel;
using System.Runtime.InteropServices;
using LexVerse.Application.Popup;
using Windows.ApplicationModel.DataTransfer;

namespace LexVerse.Infrastructure.Windows;

/// <summary>
/// Reads highlighted text from the foreground application by sending Ctrl+C and observing
/// the clipboard sequence number. Text clipboard content is restored on a best-effort basis.
/// </summary>
public sealed class WindowsSelectedTextReader : ISelectedTextReader
{
    private const ushort VkControl = 0x11;
    private const ushort VkC = 0x43;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private readonly TimeSpan _copyTimeout;
    private readonly TimeSpan _pollInterval;

    public WindowsSelectedTextReader(
        TimeSpan? copyTimeout = null,
        TimeSpan? pollInterval = null)
    {
        _copyTimeout = ValidateDuration(copyTimeout ?? TimeSpan.FromMilliseconds(450), nameof(copyTimeout));
        _pollInterval = ValidateDuration(pollInterval ?? TimeSpan.FromMilliseconds(20), nameof(pollInterval));
    }

    public async Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken = default)
    {
        var sequenceBeforeCopy = GetClipboardSequenceNumber();
        var previousText = await TryReadClipboardTextAsync();

        SendCopyShortcut();
        var deadline = DateTimeOffset.UtcNow.Add(_copyTimeout);
        var sequenceAfterCopy = sequenceBeforeCopy;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            sequenceAfterCopy = GetClipboardSequenceNumber();
            if (sequenceAfterCopy != sequenceBeforeCopy)
            {
                break;
            }
        }

        var selectedText = (await TryReadClipboardTextAsync())?.Trim();
        if (previousText is not null &&
            !string.Equals(previousText, selectedText, StringComparison.Ordinal))
        {
            TryRestoreClipboardText(previousText);
        }

        if (sequenceAfterCopy == sequenceBeforeCopy || string.IsNullOrWhiteSpace(selectedText))
        {
            return null;
        }

        return selectedText;
    }

    private static async Task<string?> TryReadClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            return content.Contains(StandardDataFormats.Text)
                ? await content.GetTextAsync()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryRestoreClipboardText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch
        {
        }
    }

    private static void SendCopyShortcut()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VkControl, 0),
            CreateKeyboardInput(VkC, 0),
            CreateKeyboardInput(VkC, KeyEventKeyUp),
            CreateKeyboardInput(VkControl, KeyEventKeyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not send the copy shortcut.");
        }
    }

    private static NativeInput CreateKeyboardInput(ushort virtualKey, uint flags) =>
        new()
        {
            Type = InputKeyboard,
            Data = new InputData
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };

    private static TimeSpan ValidateDuration(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Duration must be positive.");
        }

        return value;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
