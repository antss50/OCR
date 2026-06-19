namespace LexVerse.Infrastructure.Configuration;

internal static class DotEnvFile
{
    public static void LoadNearest(string fileName = ".env", string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                Load(candidate);
                return;
            }

            current = current.Parent;
        }
    }

    public static void Load(string path)
    {
        var envFilePath = Path.GetFullPath(path);
        var envDirectory = Path.GetDirectoryName(envFilePath) ?? Directory.GetCurrentDirectory();

        foreach (var rawLine in File.ReadLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = Unquote(line[(separatorIndex + 1)..].Trim());

            if (string.IsNullOrWhiteSpace(key) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            if (key.Equals("GOOGLE_APPLICATION_CREDENTIALS", StringComparison.OrdinalIgnoreCase)
                && !Path.IsPathRooted(value))
            {
                value = Path.GetFullPath(Path.Combine(envDirectory, value));
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string Unquote(string value)
    {
        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            value = value[..commentIndex].TrimEnd();
        }

        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
