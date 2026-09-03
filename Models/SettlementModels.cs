namespace ArkoviaEconomy.Models;

public sealed record EconomyOperation(string Id, string Kind, int UserId, string Status,
    DateTime CreatedUtc, string CurrencyId = "", long Atomic = 0, long Units = 0,
    string FullHash = "", string SignedBytes = "", string Recipient = "", string Sender = "",
    long FeeNqt = 0, int Timestamp = 0, int Deadline = 0)
{
    public Dictionary<int, long>? Allocations { get; init; }
}

public sealed record PinRecord(string Salt, string Hash, int Iterations,
    int Failures = 0, DateTime LockedUntilUtc = default);
