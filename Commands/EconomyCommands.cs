using System.Collections.Concurrent;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Integrations;
using ArkoviaEconomy.Models;
using TShockAPI;

namespace ArkoviaEconomy.Commands;

public sealed class EconomyCommands
{
    private readonly EconomyService _economy;
    private readonly EconomyDatabase _db;
    private readonly ConfigManager _config;
    private readonly ArkoviaFundingSynchronizer _sync;
    private readonly ArkoviaNodeClient _node;
    private readonly WalletClaimClient _walletClaimClient;

    private readonly ConcurrentDictionary<int, byte> _walletCreationInProgress = new();

    private const string WalletRecoveryDirectory =
        "/opt/terraria/wallet-recovery";

    public EconomyCommands(
        EconomyService economy,
        EconomyDatabase db,
        ConfigManager config,
        ArkoviaFundingSynchronizer sync,
        ArkoviaNodeClient node,
        WalletClaimClient walletClaimClient)
    {
        _economy = economy;
        _db = db;
        _config = config;
        _sync = sync;
        _node = node;
        _walletClaimClient = walletClaimClient;
    }

    public IEnumerable<Command> Build()
    {
        yield return new Command(
            Permissions.Use,
            Balance,
            "balance",
            "bal",
            "money")
        {
            HelpText = "Shows your Arkovia Economy balance."
        };

        yield return new Command(
            Permissions.Pay,
            Pay,
            "pay")
        {
            AllowServer = false,
            HelpText = "/pay <player> <amount> - transfer ARKOS to another TShock account."
        };

        yield return new Command(
            Permissions.Bank,
            Bank,
            "bank")
        {
            AllowServer = false,
            HelpText = "/bank balance|deposit|withdraw <amount>"
        };

        yield return new Command(
            Permissions.Use,
            History,
            "econhistory",
            "moneyhistory")
        {
            HelpText = "/econhistory [count] - show recent economy transactions."
        };

        yield return new Command(
            new List<string> { Permissions.TreasuryView, Permissions.AdminTreasury },
            Treasury,
            "treasury")
        {
            HelpText = "Shows the Terraria economy treasury and Arkovia sync state."
        };

        yield return new Command(
            Permissions.Wallet,
            Arkos,
            "arkos")
        {
            AllowServer = false,
            HelpText = "/arkos balance | wallet create|address|status|recovery - manage your Arkovia blockchain wallet."
        };

        yield return new Command(
            Permissions.Admin,
            Admin,
            "economy",
            "eco")
        {
            HelpText = "Arkovia Economy administration."
        };
    }

    private EconomyAccount RequirePlayerAccount(CommandArgs args)
    {
        if (!args.Player.RealPlayer ||
            !args.Player.IsLoggedIn ||
            args.Player.Account is null)
        {
            throw new InvalidOperationException(
                "You must be logged into a TShock account.");
        }

        return _economy.GetOrCreatePlayer(
            args.Player.Account.ID,
            args.Player.Account.Name);
    }

    private int RequireTShockUserId(CommandArgs args)
    {
        if (!args.Player.RealPlayer ||
            !args.Player.IsLoggedIn ||
            args.Player.Account is null)
        {
            throw new InvalidOperationException(
                "You must be logged into a TShock account.");
        }

        return args.Player.Account.ID;
    }

    private void Arkos(CommandArgs args)
    {
        try
        {
            RequireTShockUserId(args);

            if (args.Parameters.Count == 0)
            {
                ArkosHelp(args);
                return;
            }

            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "wallet":
                    ArkosWallet(args);
                    break;

                case "balance":
                    _ = ShowOnChainBalanceAsync(args);
                    break;

                case "help":
                    ArkosHelp(args);
                    break;

                default:
                    ArkosHelp(args);
                    break;
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void ArkosWallet(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            ArkosWalletHelp(args);
            return;
        }

        switch (args.Parameters[1].ToLowerInvariant())
        {
            case "create":
                _ = CreateWalletAsync(args);
                break;

            case "address":
                ShowWalletAddress(args);
                break;

            case "status":
                ShowWalletStatus(args);
                break;

            case "recovery":
                _ = ReissueWalletRecoveryAsync(args);
                break;

            default:
                ArkosWalletHelp(args);
                break;
        }
    }

    private async Task ReissueWalletRecoveryAsync(CommandArgs args)
    {
        int userId;

        try
        {
            userId = RequireTShockUserId(args);
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
            return;
        }

        if (!_walletCreationInProgress.TryAdd(userId, 0))
        {
            args.Player.SendErrorMessage(
                "A wallet operation is already in progress for your account.");

            return;
        }

        try
        {
            var wallet =
                _db.GetPlayerWallet(userId);

            if (wallet is null)
            {
                args.Player.SendErrorMessage(
                    "You do not have an Arkovia wallet yet.");

                args.Player.SendInfoMessage(
                    "Use /arkos wallet create to create one.");

                return;
            }

            if (!Directory.Exists(WalletRecoveryDirectory))
            {
                args.Player.SendErrorMessage(
                    "No protected recovery package is available.");

                args.Player.SendWarningMessage(
                    "Contact a server administrator.");

                return;
            }

            var pattern =
                $"wallet-{userId}-*.txt";

            var recoveryFile =
                Directory
                    .GetFiles(
                        WalletRecoveryDirectory,
                        pattern,
                        SearchOption.TopDirectoryOnly)
                    .OrderByDescending(
                        File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(recoveryFile) ||
                !File.Exists(recoveryFile))
            {
                args.Player.SendErrorMessage(
                    "No unclaimed recovery package is available for this wallet.");

                args.Player.SendInfoMessage(
                    "If you already claimed your recovery secret, this is expected.");

                return;
            }

            args.Player.SendInfoMessage(
                "Creating a new secure recovery claim...");

            WalletClaimResult claim;

            try
            {
                claim =
                    await _walletClaimClient.CreateClaimAsync(
                        userId,
                        wallet.AccountRS,
                        recoveryFile,
                        CancellationToken.None);
            }
            catch (Exception ex)
            {
                TShock.Log.Error(
                    $"[ArkoviaEconomy] Recovery claim reissue failed for " +
                    $"TShock user ID {userId}. Error: {ex.Message}");

                args.Player.SendErrorMessage(
                    "A secure recovery claim could not be created.");

                args.Player.SendWarningMessage(
                    "Your protected recovery package has not been deleted.");

                return;
            }

            args.Player.SendSuccessMessage(
                "A new one-time wallet recovery claim was created.");

            args.Player.SendInfoMessage(
                $"Wallet: {wallet.AccountRS}");

            args.Player.SendInfoMessage(
                "Secure recovery page:");

            args.Player.SendInfoMessage(
                "https://arkovia-node1.mywire.org/wallet/");

            args.Player.SendWarningMessage(
                $"One-time claim code: {claim.Code}");

            args.Player.SendWarningMessage(
                $"This code expires in {claim.ExpiresInMinutes} minutes.");

            args.Player.SendWarningMessage(
                "Save your recovery secret securely when it is displayed.");

            TShock.Log.ConsoleInfo(
                $"[ArkoviaEconomy] Recovery claim issued for " +
                $"TShock user ID {userId}. " +
                $"Public address: {wallet.AccountRS}.");
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(
                $"Recovery claim failed: {ex.Message}");
        }
        finally
        {
            _walletCreationInProgress.TryRemove(
                userId,
                out _);
        }
    }

    private async Task CreateWalletAsync(CommandArgs args)
    {
        int userId;

        try
        {
            userId = RequireTShockUserId(args);
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
            return;
        }

        if (!_walletCreationInProgress.TryAdd(userId, 0))
        {
            args.Player.SendErrorMessage(
                "A wallet creation request is already in progress for your account.");

            return;
        }

        string? recoveryFile = null;

        try
        {
            var existing = _db.GetPlayerWallet(userId);

            if (existing is not null)
            {
                args.Player.SendErrorMessage(
                    "Your TShock account already has an Arkovia wallet.");

                args.Player.SendInfoMessage(
                    $"Address: {existing.AccountRS}");

                return;
            }

            args.Player.SendInfoMessage(
                "Generating your Arkovia blockchain wallet...");

            var generated =
                await _node.GenerateWalletAsync(CancellationToken.None);

            Directory.CreateDirectory(WalletRecoveryDirectory);

            var createdUtc = DateTime.UtcNow;

            var safeTimestamp =
                createdUtc.ToString("yyyyMMdd-HHmmssfff");

            recoveryFile =
                Path.Combine(
                    WalletRecoveryDirectory,
                    $"wallet-{userId}-{safeTimestamp}.txt");

            var recoveryContents =
                "ARKOVIA WALLET RECOVERY SECRET\n" +
                "================================\n" +
                "KEEP THIS FILE PRIVATE. ANYONE WITH THE RECOVERY SECRET " +
                "CAN CONTROL THIS WALLET.\n\n" +
                $"TShock User ID: {userId}\n" +
                $"TShock Username: {args.Player.Account!.Name}\n" +
                $"Arkovia Address: {generated.AccountRS}\n" +
                $"Account ID: {generated.AccountId}\n" +
                $"Public Key: {generated.PublicKey}\n" +
                $"Created UTC: {createdUtc:O}\n\n" +
                $"RECOVERY SECRET: {generated.SecretPhrase}\n";

            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode =
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead
            };

            await using (var stream =
                new FileStream(recoveryFile, fileOptions))
            {
                await using var writer =
                    new StreamWriter(stream);

                await writer.WriteAsync(recoveryContents);
                await writer.FlushAsync();
            }

            _db.CreatePlayerWallet(
                userId,
                generated.AccountId,
                generated.AccountRS,
                generated.PublicKey);

            WalletClaimResult? claim = null;

            try
            {
                claim =
                    await _walletClaimClient.CreateClaimAsync(
                        userId,
                        generated.AccountRS,
                        recoveryFile,
                        CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The wallet itself already exists at this point.
                // Keep the recovery file so the secret is not lost.
                // Never log the recovery secret or internal API key.
                TShock.Log.Error(
                    $"[ArkoviaEconomy] Wallet claim setup failed for " +
                    $"TShock user ID {userId}. Error: {ex.Message}");
            }

            args.Player.SendSuccessMessage(
                "Your Arkovia wallet has been created.");

            args.Player.SendInfoMessage(
                $"Address: {generated.AccountRS}");

            args.Player.SendInfoMessage(
                $"Account ID: {generated.AccountId}");

            args.Player.SendWarningMessage(
                "Your recovery secret was NOT sent through Terraria chat.");

            if (claim is not null)
            {
                args.Player.SendInfoMessage(
                    "Secure recovery page:");

                args.Player.SendInfoMessage(
                    "https://arkovia-node1.mywire.org/wallet/");

                args.Player.SendWarningMessage(
                    $"One-time claim code: {claim.Code}");

                args.Player.SendWarningMessage(
                    $"This code expires in {claim.ExpiresInMinutes} minutes.");

                args.Player.SendWarningMessage(
                    "Save your recovery secret securely when it is displayed.");
            }
            else
            {
                args.Player.SendErrorMessage(
                    "Secure recovery setup could not be completed.");

                args.Player.SendWarningMessage(
                    "Your recovery secret remains protected on the server.");

                args.Player.SendWarningMessage(
                    "Contact a server administrator before using this wallet.");
            }

            TShock.Log.ConsoleInfo(
                $"[ArkoviaEconomy] Wallet created for TShock user ID {userId}. " +
                $"Public address: {generated.AccountRS}. " +
                (claim is not null
                    ? "Secure recovery claim created."
                    : "Recovery claim unavailable; protected recovery file retained."));
        }
        catch (Exception ex)
        {
            if (recoveryFile is not null)
            {
                try
                {
                    // If creation failed before the wallet was linked,
                    // remove the temporary recovery artifact.
                    if (_db.GetPlayerWallet(userId) is null &&
                        File.Exists(recoveryFile))
                    {
                        File.Delete(recoveryFile);
                    }
                }
                catch
                {
                    // Never include recovery contents in error/log output.
                }
            }

            args.Player.SendErrorMessage(
                $"Wallet creation failed: {ex.Message}");
        }
        finally
        {
            _walletCreationInProgress.TryRemove(
                userId,
                out _);
        }
    }

    private void ShowWalletAddress(CommandArgs args)
    {
        try
        {
            var userId = RequireTShockUserId(args);
            var wallet = _db.GetPlayerWallet(userId);

            if (wallet is null)
            {
                args.Player.SendErrorMessage(
                    "You do not have an Arkovia wallet yet.");

                args.Player.SendInfoMessage(
                    "Use /arkos wallet create to create one.");

                return;
            }

            args.Player.SendSuccessMessage(
                $"Arkovia address: {wallet.AccountRS}");

            args.Player.SendInfoMessage(
                $"Account ID: {wallet.AccountId}");
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void ShowWalletStatus(CommandArgs args)
    {
        try
        {
            var userId = RequireTShockUserId(args);
            var wallet = _db.GetPlayerWallet(userId);

            if (wallet is null)
            {
                args.Player.SendInfoMessage(
                    "Arkovia wallet status: Not created");

                args.Player.SendInfoMessage(
                    "Use /arkos wallet create to create one.");

                return;
            }

            args.Player.SendSuccessMessage(
                "Arkovia wallet status: Created");

            args.Player.SendInfoMessage(
                $"Address: {wallet.AccountRS}");

            args.Player.SendInfoMessage(
                $"Account ID: {wallet.AccountId}");

            args.Player.SendInfoMessage(
                $"Created: {wallet.CreatedUtc:u}");
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private async Task ShowOnChainBalanceAsync(
        CommandArgs args)
    {
        try
        {
            var userId = RequireTShockUserId(args);
            var wallet = _db.GetPlayerWallet(userId);

            if (wallet is null)
            {
                args.Player.SendErrorMessage(
                    "You do not have an Arkovia wallet yet.");

                args.Player.SendInfoMessage(
                    "Use /arkos wallet create to create one.");

                return;
            }

            args.Player.SendInfoMessage(
                $"Checking your on-chain {_config.Current.CurrencySymbol} balance...");

            var balanceAtomic =
                await _node.GetAccountBalanceAtomicAsync(
                    wallet.AccountRS,
                    CancellationToken.None);

            args.Player.SendSuccessMessage(
                $"On-chain balance: {_config.Current.FormatBlockchain(balanceAtomic)}");

            args.Player.SendInfoMessage(
                $"Wallet: {wallet.AccountRS}");
        }
        catch (Exception ex)
        {
            TShock.Log.Error(
                $"[ArkoviaEconomy] On-chain balance lookup failed: " +
                $"{ex.Message}");

            args.Player.SendErrorMessage(
                "Unable to retrieve your selected on-chain currency balance.");
        }
    }

    private void ArkosHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage(
            "Arkovia / ARKOS blockchain commands:");

        args.Player.SendInfoMessage(
            "/arkos balance - show your on-chain ARKOS balance");

        args.Player.SendInfoMessage(
            "/arkos wallet create - create your Arkovia wallet");

        args.Player.SendInfoMessage(
            "/arkos wallet address - show your blockchain address");

        args.Player.SendInfoMessage(
            "/arkos wallet status - show your wallet status");

        args.Player.SendInfoMessage(
            "/arkos wallet recovery - create a new secure recovery claim");
    }

    private void ArkosWalletHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage(
            "/arkos wallet create");

        args.Player.SendInfoMessage(
            "/arkos wallet address");

        args.Player.SendInfoMessage(
            "/arkos wallet status");

        args.Player.SendInfoMessage(
            "/arkos wallet recovery");
    }

    private void Balance(CommandArgs args)
    {
        try
        {
            if (args.Parameters.Count > 0 &&
                args.Player.HasPermission(Permissions.AdminAudit))
            {
                var user =
                    TShock.UserAccounts.GetUserAccountByName(
                        args.Parameters[0]);

                if (user is null)
                {
                    args.Player.SendErrorMessage(
                        "TShock account not found.");

                    return;
                }

                var account =
                    _economy.GetOrCreatePlayer(
                        user.ID,
                        user.Name);

                args.Player.SendInfoMessage(
                    $"{user.Name}: wallet {_config.Current.Format(account.WalletAtomic)}, " +
                    $"bank {_config.Current.Format(account.BankAtomic)}.");

                return;
            }

            var mine = RequirePlayerAccount(args);

            args.Player.SendSuccessMessage(
                $"Wallet: {_config.Current.Format(mine.WalletAtomic)} | " +
                $"Bank: {_config.Current.Format(mine.BankAtomic)} | " +
                $"Total: {_config.Current.Format(checked(mine.WalletAtomic + mine.BankAtomic))}");
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void Pay(CommandArgs args)
    {
        try
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage(
                    "Usage: /pay <TShockAccount> <amount>");

                return;
            }

            var sender = RequirePlayerAccount(args);

            var user =
                TShock.UserAccounts.GetUserAccountByName(
                    args.Parameters[0]);

            if (user is null)
            {
                args.Player.SendErrorMessage(
                    "Recipient TShock account not found.");

                return;
            }

            if (user.ID == args.Player.Account!.ID)
            {
                args.Player.SendErrorMessage(
                    "You cannot pay yourself.");

                return;
            }

            if (!TryAmount(
                    args.Parameters[1],
                    out var amount))
            {
                args.Player.SendErrorMessage(
                    "Invalid amount.");

                return;
            }

            if (amount <
                _config.Current.ToAtomic(
                    _config.Current.MinimumTransfer))
            {
                args.Player.SendErrorMessage(
                    "Amount is below the minimum transfer.");

                return;
            }

            var recipient =
                _economy.GetOrCreatePlayer(
                    user.ID,
                    user.Name);

            var fee =
                _economy.Percent(
                    amount,
                    _config.Current.PlayerTransferFeePercent);

            _economy.Transfer(
                sender,
                recipient,
                amount,
                "player_transfer",
                "tshock",
                Guid.NewGuid().ToString("N"),
                $"Player payment to {user.Name}",
                args.Player.Account.Name,
                fee);

            args.Player.SendSuccessMessage(
                $"Paid {user.Name} {_config.Current.Format(amount)}" +
                (fee > 0
                    ? $" (fee {_config.Current.Format(fee)})"
                    : "") +
                ".");

            foreach (var player in
                     TShock.Players.Where(
                         p => p?.Account?.ID == user.ID))
            {
                player.SendSuccessMessage(
                    $"You received {_config.Current.Format(amount)} " +
                    $"from {args.Player.Account.Name}.");
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void Bank(CommandArgs args)
    {
        try
        {
            if (!_config.Current.Banking.Enabled)
            {
                args.Player.SendErrorMessage(
                    "Banking is disabled.");

                return;
            }

            var account = RequirePlayerAccount(args);

            if (args.Parameters.Count == 0 ||
                args.Parameters[0].Equals(
                    "balance",
                    StringComparison.OrdinalIgnoreCase))
            {
                args.Player.SendInfoMessage(
                    $"Bank: {_config.Current.Format(account.BankAtomic)} | " +
                    $"Wallet: {_config.Current.Format(account.WalletAtomic)}");

                return;
            }

            if (args.Parameters.Count < 2 ||
                !TryAmount(
                    args.Parameters[1],
                    out var amount))
            {
                args.Player.SendErrorMessage(
                    "Usage: /bank deposit|withdraw <amount>");

                return;
            }

            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "deposit":
                    _economy.Deposit(
                        account,
                        amount,
                        args.Player.Account!.Name);

                    args.Player.SendSuccessMessage(
                        $"Deposited {_config.Current.Format(amount)}.");

                    break;

                case "withdraw":
                    _economy.Withdraw(
                        account,
                        amount,
                        args.Player.Account!.Name);

                    args.Player.SendSuccessMessage(
                        $"Withdrew {_config.Current.Format(amount)}.");

                    break;

                default:
                    args.Player.SendErrorMessage(
                        "Usage: /bank balance|deposit|withdraw <amount>");

                    break;
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void History(CommandArgs args)
    {
        try
        {
            var account = RequirePlayerAccount(args);

            var count =
                args.Parameters.Count > 0 &&
                int.TryParse(
                    args.Parameters[0],
                    out var n)
                    ? Math.Clamp(n, 1, 20)
                    : 10;

            var list =
                _db.GetTransactions(
                    account.Id,
                    count);

            if (list.Count == 0)
            {
                args.Player.SendInfoMessage(
                    "No economy transactions yet.");

                return;
            }

            foreach (var tx in list)
            {
                var sign =
                    tx.ToAccountId == account.Id
                        ? "+"
                        : tx.FromAccountId == account.Id
                            ? "-"
                            : "";

                args.Player.SendInfoMessage(
                    $"#{tx.Id} {sign}{_config.Current.Format(tx.AmountAtomic)} " +
                    $"[{tx.Type}] {tx.Description}");
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void Treasury(CommandArgs args)
    {
        try
        {
            if (args.Parameters.Count > 0)
            {
                if (!args.Player.HasPermission(Permissions.AdminTreasury))
                    throw new InvalidOperationException("Missing permission: " + Permissions.AdminTreasury);
                if (args.Parameters.Count != 2 ||
                    !(args.Parameters[0].Equals("add", StringComparison.OrdinalIgnoreCase) ||
                      args.Parameters[0].Equals("take", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Usage: /treasury [add|take <amount>]");
                if (!TryAmount(args.Parameters[1], out var amount))
                    throw new InvalidOperationException("Enter a positive amount within the supported range.");
                var add = args.Parameters[0].Equals("add", StringComparison.OrdinalIgnoreCase);
                _economy.AdminAdjust(_economy.GetTreasury(), add ? amount : -amount,
                    add ? "Admin treasury addition" : "Admin treasury deduction", args.Player.Name);
                args.Player.SendSuccessMessage("Treasury adjustment recorded (off-chain only).");
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
            return;
        }
        var treasury = _economy.GetTreasury();

        args.Player.SendInfoMessage(
            $"Terraria Treasury: {_config.Current.Format(treasury.WalletAtomic)} | " +
            $"Arkovia funding source: {_config.Current.Arkovia.CommunityDevelopmentAccount}");

        args.Player.SendInfoMessage(
            $"Blockchain sync: {_sync.LastStatus} " +
            $"Last success: {(_sync.LastSuccessUtc?.ToString("u") ?? "never")}");
    }

    private void Admin(CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            AdminHelp(args);
            return;
        }

        try
        {
            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "help":
                    AdminHelp(args);
                    break;

                case "reload":
                    if (!args.Player.HasPermission(
                            Permissions.AdminConfig))
                    {
                        throw new InvalidOperationException(
                            "Missing permission: " +
                            Permissions.AdminConfig);
                    }

                    _config.Load();

                    args.Player.SendSuccessMessage(
                        "Arkovia Economy configuration reloaded.");

                    break;

                case "sync":
                    if (!args.Player.HasPermission(
                            Permissions.AdminTreasury))
                    {
                        throw new InvalidOperationException(
                            "Missing permission: " +
                            Permissions.AdminTreasury);
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var count =
                                await _sync.SyncOnceAsync();

                            args.Player.SendSuccessMessage(
                                $"Arkovia sync complete. " +
                                $"{count} new funding entries credited.");
                        }
                        catch (Exception ex)
                        {
                            args.Player.SendErrorMessage(
                                ex.Message);
                        }
                    });

                    args.Player.SendInfoMessage(
                        "Arkovia funding synchronization started.");

                    break;

                case "give":
                    Adjust(args, positive: true);
                    break;

                case "take":
                    Adjust(args, positive: false);
                    break;

                case "freeze":
                    Freeze(args, true);
                    break;

                case "unfreeze":
                    Freeze(args, false);
                    break;

                case "reward":
                    Reward(args);
                    break;

                default:
                    AdminHelp(args);
                    break;
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void Adjust(
        CommandArgs args,
        bool positive)
    {
        if (!args.Player.HasPermission(
                Permissions.AdminAdjust))
        {
            throw new InvalidOperationException(
                "Missing permission: " +
                Permissions.AdminAdjust);
        }

        if (args.Parameters.Count < 4 ||
            !TryAmount(
                args.Parameters[2],
                out var amount))
        {
            throw new InvalidOperationException(
                "Usage: /eco give|take <account> <amount> <reason>");
        }

        var user =
            TShock.UserAccounts.GetUserAccountByName(
                args.Parameters[1])
            ?? throw new InvalidOperationException(
                "TShock account not found.");

        var account =
            _economy.GetOrCreatePlayer(
                user.ID,
                user.Name);

        var reason =
            string.Join(
                " ",
                args.Parameters.Skip(3));

        _economy.AdminAdjust(
            account,
            positive ? amount : -amount,
            reason,
            args.Player.Account?.Name ?? "console");

        args.Player.SendSuccessMessage(
            $"Adjusted {user.Name} by " +
            $"{(positive ? "+" : "-")}" +
            $"{_config.Current.Format(amount)}. " +
            $"Reason: {reason}");
    }

    private void Freeze(
        CommandArgs args,
        bool freeze)
    {
        if (!args.Player.HasPermission(
                Permissions.Admin))
        {
            throw new InvalidOperationException(
                "Missing admin permission.");
        }

        if (args.Parameters.Count < 2)
        {
            throw new InvalidOperationException(
                "Usage: /eco freeze|unfreeze <account>");
        }

        var user =
            TShock.UserAccounts.GetUserAccountByName(
                args.Parameters[1])
            ?? throw new InvalidOperationException(
                "TShock account not found.");

        var account =
            _economy.GetOrCreatePlayer(
                user.ID,
                user.Name);

        _db.SetFrozen(
            account.Id,
            freeze);

        args.Player.SendSuccessMessage(
            $"{user.Name} economy account " +
            $"{(freeze ? "frozen" : "unfrozen")}.");
    }

    private void Reward(CommandArgs args)
    {
        if (!args.Player.HasPermission(
                Permissions.AdminTreasury))
        {
            throw new InvalidOperationException(
                "Missing permission: " +
                Permissions.AdminTreasury);
        }

        if (args.Parameters.Count < 4 ||
            !TryAmount(
                args.Parameters[2],
                out var amount))
        {
            throw new InvalidOperationException(
                "Usage: /eco reward <account> <amount> <reason>");
        }

        var user =
            TShock.UserAccounts.GetUserAccountByName(
                args.Parameters[1])
            ?? throw new InvalidOperationException(
                "TShock account not found.");

        var target =
            _economy.GetOrCreatePlayer(
                user.ID,
                user.Name);

        var treasury =
            _economy.GetTreasury();

        var reason =
            string.Join(
                " ",
                args.Parameters.Skip(3));

        _economy.Transfer(
            treasury,
            target,
            amount,
            "admin_reward",
            "game_reward",
            Guid.NewGuid().ToString("N"),
            reason,
            args.Player.Account?.Name ?? "console");

        args.Player.SendSuccessMessage(
            $"Treasury paid {user.Name} " +
            $"{_config.Current.Format(amount)}. " +
            $"Reason: {reason}");
    }

    private void AdminHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage(
            "/eco reload | sync | give <user> <amount> <reason> | " +
            "take <user> <amount> <reason>");

        args.Player.SendInfoMessage(
            "/eco freeze <user> | unfreeze <user> | " +
            "reward <user> <amount> <reason>");
    }

    private bool TryAmount(
        string text,
        out long atomic)
    {
        atomic = 0;

        if (!decimal.TryParse(
                text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value) ||
            value <= 0)
        {
            return false;
        }

        try
        {
            atomic =
                _config.Current.ToAtomic(value);

            return atomic > 0;
        }
        catch
        {
            return false;
        }
    }
}
