namespace LexVerse.Application.Account;

public sealed record AccountSession
{
    public AccountSession(
        string subjectId,
        string displayName,
        DateTimeOffset authenticatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        SubjectId = subjectId.Trim();
        DisplayName = displayName.Trim();
        AuthenticatedAtUtc = authenticatedAtUtc;
    }

    public string SubjectId { get; }

    public string DisplayName { get; }

    public DateTimeOffset AuthenticatedAtUtc { get; }
}
