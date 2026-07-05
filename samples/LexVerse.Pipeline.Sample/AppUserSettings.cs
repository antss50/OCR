using System.IO;
using System.Windows.Input;

namespace LexVerse.Pipeline.Sample;

internal sealed class AppUserSettings
{
    private const string SettingsDirectoryName = "LexVerse";
    private const string SettingsFileName = "pipeline-sample-settings.ini";

    public string AppLanguage { get; set; } = "en";

    public Dictionary<string, ShortcutGesture> Shortcuts { get; set; } = CreateDefaultShortcuts();

    public static AppUserSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new AppUserSettings();
            }

            var settings = new AppUserSettings();
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();

                if (string.Equals(key, "AppLanguage", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AppLanguage = string.Equals(value, "vi", StringComparison.OrdinalIgnoreCase)
                        ? "vi"
                        : "en";
                    continue;
                }

                const string shortcutPrefix = "Shortcut.";
                if (key.StartsWith(shortcutPrefix, StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse<ShortcutAction>(key[shortcutPrefix.Length..], ignoreCase: true, out var action) &&
                    ShortcutGesture.TryParseStorageText(value, out var gesture))
                {
                    settings.Shortcuts[action.ToString()] = gesture;
                }
            }

            settings.EnsureDefaults();
            return settings;
        }
        catch
        {
            return new AppUserSettings();
        }
    }

    public void Save()
    {
        var path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EnsureDefaults();

        var lines = new List<string>
        {
            "# LexVerse pipeline sample settings",
            $"AppLanguage={AppLanguage}"
        };

        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            lines.Add($"Shortcut.{action}={GetShortcut(action).ToStorageText()}");
        }

        File.WriteAllLines(path, lines);
    }

    public ShortcutGesture GetShortcut(ShortcutAction action)
    {
        EnsureDefaults();
        return Shortcuts.TryGetValue(action.ToString(), out var shortcut)
            ? shortcut
            : ShortcutDefaults[action];
    }

    public void SetShortcut(ShortcutAction action, ShortcutGesture gesture)
    {
        EnsureDefaults();
        Shortcuts[action.ToString()] = gesture;
    }

    private void EnsureDefaults()
    {
        if (Shortcuts is null)
        {
            Shortcuts = CreateDefaultShortcuts();
            return;
        }

        foreach (var (action, shortcut) in ShortcutDefaults)
        {
            Shortcuts.TryAdd(action.ToString(), shortcut);
        }
    }

    private static Dictionary<string, ShortcutGesture> CreateDefaultShortcuts()
    {
        return ShortcutDefaults.ToDictionary(
            pair => pair.Key.ToString(),
            pair => pair.Value);
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            SettingsDirectoryName,
            SettingsFileName);
    }

    private static readonly IReadOnlyDictionary<ShortcutAction, ShortcutGesture> ShortcutDefaults =
        new Dictionary<ShortcutAction, ShortcutGesture>
        {
            [ShortcutAction.PopupTranslate] = new(Key.F6, ModifierKeys.None),
            [ShortcutAction.RegionTranslate] = new(Key.R, ModifierKeys.Control | ModifierKeys.Shift),
            [ShortcutAction.FullScreenRealtime] = new(Key.F, ModifierKeys.Control | ModifierKeys.Shift),
            [ShortcutAction.StopRealtime] = new(Key.Escape, ModifierKeys.None),
            [ShortcutAction.ResetOverlay] = new(Key.Back, ModifierKeys.Control | ModifierKeys.Shift)
        };
}

internal enum ShortcutAction
{
    PopupTranslate,
    RegionTranslate,
    FullScreenRealtime,
    StopRealtime,
    ResetOverlay
}

internal sealed record ShortcutGesture(Key Key, ModifierKeys Modifiers)
{
    public bool IsEmpty => Key == Key.None;

    public static bool TryParseStorageText(string text, out ShortcutGesture gesture)
    {
        gesture = new ShortcutGesture(Key.None, ModifierKeys.None);

        var parts = text.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 ||
            !Enum.TryParse<Key>(parts[0], ignoreCase: true, out var key))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        if (parts.Length > 1 &&
            !string.Equals(parts[1], "None", StringComparison.OrdinalIgnoreCase) &&
            !Enum.TryParse(parts[1], ignoreCase: true, out modifiers))
        {
            return false;
        }

        gesture = new ShortcutGesture(key, modifiers);
        return true;
    }

    public static ShortcutGesture FromKeyEvent(KeyEventArgs e)
    {
        var key = NormalizeKey(e);
        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        return new ShortcutGesture(key, modifiers);
    }

    public string ToStorageText()
    {
        return $"{Key}|{Modifiers}";
    }

    public string DisplayText()
    {
        if (IsEmpty)
        {
            return "None";
        }

        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyToDisplayText(Key));
        return string.Join(" + ", parts);
    }

    public bool Matches(KeyEventArgs e)
    {
        var gesture = FromKeyEvent(e);
        return gesture.Key == Key && gesture.Modifiers == Modifiers;
    }

    private static Key NormalizeKey(KeyEventArgs e)
    {
        return e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
    }

    private static string KeyToDisplayText(Key key)
    {
        return key switch
        {
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Return => "Enter",
            Key.Space => "Space",
            _ => key.ToString()
        };
    }
}
