using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LexVerse.Infrastructure.Commerce;

public sealed class WindowsDpapiAccountStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("LexVerse.Account.v1");
    private readonly string _path;

    public WindowsDpapiAccountStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    internal async Task<StoredAccount?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var encrypted = await File.ReadAllBytesAsync(_path, cancellationToken);
            if (encrypted.Length is <= 0 or > 128 * 1024)
            {
                return null;
            }

            var plaintext = Unprotect(encrypted);
            try
            {
                return JsonSerializer.Deserialize<StoredAccount>(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    internal async Task WriteAsync(StoredAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(account);
        byte[] encrypted;
        try
        {
            encrypted = Protect(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The account store path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

    private static byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(OptionalEntropy, GCHandleType.Pinned);
        try
        {
            var inputBlob = new DataBlob(input.Length, inputHandle.AddrOfPinnedObject());
            var entropyBlob = new DataBlob(OptionalEntropy.Length, entropyHandle.AddrOfPinnedObject());
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out var outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob);
            if (!succeeded)
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            try
            {
                var output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                _ = LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            inputHandle.Free();
            entropyHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob(int size, IntPtr data)
    {
        public readonly int Size = size;
        public readonly IntPtr Data = data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}

internal sealed class StoredAccount
{
    public required string SubjectId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset AuthenticatedAtUtc { get; init; }
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }
}
