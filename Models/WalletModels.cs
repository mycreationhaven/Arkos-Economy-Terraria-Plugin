namespace ArkoviaEconomy.Models;

public sealed record ArkoviaPlayerWallet(
    int TShockUserId,
    string AccountId,
    string AccountRS,
    string PublicKey,
    DateTime CreatedUtc
);

public sealed record GeneratedArkoviaWallet(
    string SecretPhrase,
    string AccountId,
    string AccountRS,
    string PublicKey
);
