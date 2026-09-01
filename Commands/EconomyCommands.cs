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

    public EconomyCommands(EconomyService economy, EconomyDatabase db, ConfigManager config, ArkoviaFundingSynchronizer sync)
    { _economy = economy; _db = db; _config = config; _sync = sync; }

    public IEnumerable<Command> Build()
    {
        yield return new Command(Permissions.Use, Balance, "balance", "bal", "money") { HelpText = "Shows your Arkovia Economy balance." };
        yield return new Command(Permissions.Pay, Pay, "pay") { AllowServer = false, HelpText = "/pay <player> <amount> - transfer ARK to another TShock account." };
        yield return new Command(Permissions.Bank, Bank, "bank") { AllowServer = false, HelpText = "/bank balance|deposit|withdraw <amount>" };
        yield return new Command(Permissions.Use, History, "econhistory", "moneyhistory") { HelpText = "/econhistory [count] - show recent economy transactions." };
        yield return new Command(Permissions.TreasuryView, Treasury, "treasury") { HelpText = "Shows the Terraria economy treasury and Arkovia sync state." };
        yield return new Command(Permissions.Admin, Admin, "economy", "eco") { HelpText = "Arkovia Economy administration." };
    }

    private EconomyAccount RequirePlayerAccount(CommandArgs args)
    {
        if (!args.Player.RealPlayer || !args.Player.IsLoggedIn || args.Player.Account is null) throw new InvalidOperationException("You must be logged into a TShock account.");
        return _economy.GetOrCreatePlayer(args.Player.Account.ID, args.Player.Account.Name);
    }

    private void Balance(CommandArgs args)
    {
        try
        {
            if (args.Parameters.Count > 0 && args.Player.HasPermission(Permissions.AdminAudit))
            {
                var user = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
                if (user is null) { args.Player.SendErrorMessage("TShock account not found."); return; }
                var account = _economy.GetOrCreatePlayer(user.ID, user.Name);
                args.Player.SendInfoMessage($"{user.Name}: wallet {_config.Current.Format(account.WalletAtomic)}, bank {_config.Current.Format(account.BankAtomic)}.");
                return;
            }
            var mine = RequirePlayerAccount(args);
            args.Player.SendSuccessMessage($"Wallet: {_config.Current.Format(mine.WalletAtomic)} | Bank: {_config.Current.Format(mine.BankAtomic)} | Total: {_config.Current.Format(checked(mine.WalletAtomic + mine.BankAtomic))}");
        }
        catch (Exception ex) { args.Player.SendErrorMessage(ex.Message); }
    }

    private void Pay(CommandArgs args)
    {
        try
        {
            if (args.Parameters.Count < 2) { args.Player.SendErrorMessage("Usage: /pay <TShockAccount> <amount>"); return; }
            var sender = RequirePlayerAccount(args);
            var user = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
            if (user is null) { args.Player.SendErrorMessage("Recipient TShock account not found."); return; }
            if (user.ID == args.Player.Account!.ID) { args.Player.SendErrorMessage("You cannot pay yourself."); return; }
            if (!TryAmount(args.Parameters[1], out var amount)) { args.Player.SendErrorMessage("Invalid amount."); return; }
            if (amount < _config.Current.ToAtomic(_config.Current.MinimumTransfer)) { args.Player.SendErrorMessage("Amount is below the minimum transfer."); return; }
            var recipient = _economy.GetOrCreatePlayer(user.ID, user.Name);
            var fee = _economy.Percent(amount, _config.Current.PlayerTransferFeePercent);
            _economy.Transfer(sender, recipient, amount, "player_transfer", "tshock", Guid.NewGuid().ToString("N"), $"Player payment to {user.Name}", args.Player.Account.Name, fee);
            args.Player.SendSuccessMessage($"Paid {user.Name} {_config.Current.Format(amount)}" + (fee > 0 ? $" (fee {_config.Current.Format(fee)})" : "") + ".");
            foreach (var p in TShock.Players.Where(p => p?.Account?.ID == user.ID)) p.SendSuccessMessage($"You received {_config.Current.Format(amount)} from {args.Player.Account.Name}.");
        }
        catch (Exception ex) { args.Player.SendErrorMessage(ex.Message); }
    }

    private void Bank(CommandArgs args)
    {
        try
        {
            if (!_config.Current.Banking.Enabled) { args.Player.SendErrorMessage("Banking is disabled."); return; }
            var account = RequirePlayerAccount(args);
            if (args.Parameters.Count == 0 || args.Parameters[0].Equals("balance", StringComparison.OrdinalIgnoreCase))
            { args.Player.SendInfoMessage($"Bank: {_config.Current.Format(account.BankAtomic)} | Wallet: {_config.Current.Format(account.WalletAtomic)}"); return; }
            if (args.Parameters.Count < 2 || !TryAmount(args.Parameters[1], out var amount)) { args.Player.SendErrorMessage("Usage: /bank deposit|withdraw <amount>"); return; }
            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "deposit": _economy.Deposit(account, amount, args.Player.Account!.Name); args.Player.SendSuccessMessage($"Deposited {_config.Current.Format(amount)}."); break;
                case "withdraw": _economy.Withdraw(account, amount, args.Player.Account!.Name); args.Player.SendSuccessMessage($"Withdrew {_config.Current.Format(amount)}."); break;
                default: args.Player.SendErrorMessage("Usage: /bank balance|deposit|withdraw <amount>"); break;
            }
        }
        catch (Exception ex) { args.Player.SendErrorMessage(ex.Message); }
    }

    private void History(CommandArgs args)
    {
        try
        {
            var account = RequirePlayerAccount(args);
            var count = args.Parameters.Count > 0 && int.TryParse(args.Parameters[0], out var n) ? Math.Clamp(n, 1, 20) : 10;
            var list = _db.GetTransactions(account.Id, count);
            if (list.Count == 0) { args.Player.SendInfoMessage("No economy transactions yet."); return; }
            foreach (var tx in list)
            {
                var sign = tx.ToAccountId == account.Id ? "+" : tx.FromAccountId == account.Id ? "-" : "";
                args.Player.SendInfoMessage($"#{tx.Id} {sign}{_config.Current.Format(tx.AmountAtomic)} [{tx.Type}] {tx.Description}");
            }
        }
        catch (Exception ex) { args.Player.SendErrorMessage(ex.Message); }
    }

    private void Treasury(CommandArgs args)
    {
        var t = _economy.GetTreasury();
        args.Player.SendInfoMessage($"Terraria Treasury: {_config.Current.Format(t.WalletAtomic)} | Arkovia 5% source: {_config.Current.Arkovia.CommunityDevelopmentAccount}");
        args.Player.SendInfoMessage($"Blockchain sync: {_sync.LastStatus} Last success: {(_sync.LastSuccessUtc?.ToString("u") ?? "never")}");
    }

    private void Admin(CommandArgs args)
    {
        if (args.Parameters.Count == 0) { AdminHelp(args); return; }
        try
        {
            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "help": AdminHelp(args); break;
                case "reload":
                    if (!args.Player.HasPermission(Permissions.AdminConfig)) throw new InvalidOperationException("Missing permission: " + Permissions.AdminConfig);
                    _config.Load(); args.Player.SendSuccessMessage("Arkovia Economy configuration reloaded."); break;
                case "sync":
                    if (!args.Player.HasPermission(Permissions.AdminTreasury)) throw new InvalidOperationException("Missing permission: " + Permissions.AdminTreasury);
                    _ = Task.Run(async () => { try { var n = await _sync.SyncOnceAsync(); args.Player.SendSuccessMessage($"Arkovia sync complete. {n} new funding entries credited."); } catch (Exception ex) { args.Player.SendErrorMessage(ex.Message); } });
                    args.Player.SendInfoMessage("Arkovia funding synchronization started."); break;
                case "give": Adjust(args, positive:true); break;
                case "take": Adjust(args, positive:false); break;
                case "freeze": Freeze(args, true); break;
                case "unfreeze": Freeze(args, false); break;
                case "reward": Reward(args); break;
                default: AdminHelp(args); break;
            }
        }
        catch (Exception ex) { args.Player.SendErrorMessage(ex.Message); }
    }

    private void Adjust(CommandArgs args, bool positive)
    {
        if (!args.Player.HasPermission(Permissions.AdminAdjust)) throw new InvalidOperationException("Missing permission: " + Permissions.AdminAdjust);
        if (args.Parameters.Count < 4 || !TryAmount(args.Parameters[2], out var amount)) throw new InvalidOperationException("Usage: /eco give|take <account> <amount> <reason>");
        var user = TShock.UserAccounts.GetUserAccountByName(args.Parameters[1]) ?? throw new InvalidOperationException("TShock account not found.");
        var account = _economy.GetOrCreatePlayer(user.ID, user.Name);
        var reason = string.Join(" ", args.Parameters.Skip(3));
        _economy.AdminAdjust(account, positive ? amount : -amount, reason, args.Player.Account?.Name ?? "console");
        args.Player.SendSuccessMessage($"Adjusted {user.Name} by {(positive ? "+" : "-")}{_config.Current.Format(amount)}. Reason: {reason}");
    }

    private void Freeze(CommandArgs args, bool freeze)
    {
        if (!args.Player.HasPermission(Permissions.Admin)) throw new InvalidOperationException("Missing admin permission.");
        if (args.Parameters.Count < 2) throw new InvalidOperationException("Usage: /eco freeze|unfreeze <account>");
        var user = TShock.UserAccounts.GetUserAccountByName(args.Parameters[1]) ?? throw new InvalidOperationException("TShock account not found.");
        var account = _economy.GetOrCreatePlayer(user.ID, user.Name);
        _db.SetFrozen(account.Id, freeze);
        args.Player.SendSuccessMessage($"{user.Name} economy account {(freeze ? "frozen" : "unfrozen")}.");
    }

    private void Reward(CommandArgs args)
    {
        if (!args.Player.HasPermission(Permissions.AdminTreasury)) throw new InvalidOperationException("Missing permission: " + Permissions.AdminTreasury);
        if (args.Parameters.Count < 4 || !TryAmount(args.Parameters[2], out var amount)) throw new InvalidOperationException("Usage: /eco reward <account> <amount> <reason>");
        var user = TShock.UserAccounts.GetUserAccountByName(args.Parameters[1]) ?? throw new InvalidOperationException("TShock account not found.");
        var target = _economy.GetOrCreatePlayer(user.ID, user.Name);
        var treasury = _economy.GetTreasury();
        var reason = string.Join(" ", args.Parameters.Skip(3));
        _economy.Transfer(treasury, target, amount, "admin_reward", "game_reward", Guid.NewGuid().ToString("N"), reason, args.Player.Account?.Name ?? "console");
        args.Player.SendSuccessMessage($"Treasury paid {user.Name} {_config.Current.Format(amount)}. Reason: {reason}");
    }

    private void AdminHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage("/eco reload | sync | give <user> <amount> <reason> | take <user> <amount> <reason>");
        args.Player.SendInfoMessage("/eco freeze <user> | unfreeze <user> | reward <user> <amount> <reason>");
    }

    private bool TryAmount(string text, out long atomic)
    {
        atomic = 0;
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0) return false;
        try { atomic = _config.Current.ToAtomic(value); return atomic > 0; } catch { return false; }
    }
}
