using System.Text.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace ArkoviaCrossplay;

[ApiVersion(2, 1)]
public sealed class ArkoviaCrossplayPlugin : TerrariaPlugin
{
    private readonly string[] _clientProtocols = new string[Main.maxPlayers];
    private CrossplayConfig _config = CrossplayConfig.CreateDefault();
    private Command? _command;

    public override string Name => "Arkovia Crossplay Bridge";
    public override string Author => "My Creation Haven";
    public override string Description => "Allows explicitly approved Terraria 1.4.5.x protocol versions to pass the TShock version handshake.";
    public override Version Version => new(0, 1, 0);

    private static string ConfigPath => Path.Combine(TShock.SavePath, "ArkoviaCrossplay.json");
    private static string ServerProtocol => $"Terraria{Main.curRelease}";

    public ArkoviaCrossplayPlugin(Main game) : base(game)
    {
        // Run before normal version validation so we can safely rewrite the
        // approved ConnectRequest handshake in-place.
        Order = -1000;
    }

    public override void Initialize()
    {
        LoadConfig();

        ServerApi.Hooks.NetGetData.Register(this, OnGetData, int.MaxValue);
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        GeneralHooks.ReloadEvent += OnReload;

        _command = new Command("arkovia.crossplay.admin", HandleCommand, "arcrossplay", "acp")
        {
            HelpText = "Arkovia crossplay bridge status and diagnostics."
        };
        Commands.ChatCommands.Add(_command);

        TShock.Log.ConsoleInfo($"[ArkoviaCrossplay] Started on Terraria {Main.versionNumber} ({ServerProtocol}).");
        TShock.Log.ConsoleInfo($"[ArkoviaCrossplay] Approved client protocols: {string.Join(", ", _config.AllowedClientProtocols.Keys.OrderBy(x => x))}");
        TShock.Log.ConsoleWarn("[ArkoviaCrossplay] Compatibility bridge is handshake-only. It does not translate packets or provide console networking features.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            GeneralHooks.ReloadEvent -= OnReload;
            if (_command is not null)
                Commands.ChatCommands.Remove(_command);
        }
        base.Dispose(disposing);
    }

    private void OnReload(ReloadEventArgs args)
    {
        LoadConfig();
        args.Player?.SendSuccessMessage("Arkovia Crossplay configuration reloaded.");
    }

    private void OnLeave(LeaveEventArgs args)
    {
        if (args.Who >= 0 && args.Who < _clientProtocols.Length)
            _clientProtocols[args.Who] = string.Empty;
    }

    private void OnGetData(GetDataEventArgs args)
    {
        if (!_config.Enabled || args.MsgID != PacketTypes.ConnectRequest)
            return;

        var index = args.Msg.whoAmI;
        if (index < 0 || index >= _clientProtocols.Length)
            return;

        string clientProtocol;
        try
        {
            using var stream = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length, writable: false);
            using var reader = new BinaryReader(stream);
            clientProtocol = reader.ReadString();
        }
        catch (Exception ex)
        {
            if (_config.Verbose)
                TShock.Log.ConsoleWarn($"[ArkoviaCrossplay] Could not read ConnectRequest for slot {index}: {ex.Message}");
            return;
        }

        _clientProtocols[index] = clientProtocol;

        if (_config.Verbose)
            TShock.Log.ConsoleInfo($"[ArkoviaCrossplay] Slot {index} requested protocol '{clientProtocol}'. Server is '{ServerProtocol}'.");

        if (string.Equals(clientProtocol, ServerProtocol, StringComparison.Ordinal))
            return;

        if (!_config.AllowedClientProtocols.TryGetValue(clientProtocol, out var description))
        {
            // Do not override TShock's normal behavior for unknown versions.
            // This keeps the compatibility boundary explicit and fail-closed.
            if (_config.Verbose)
                TShock.Log.ConsoleWarn($"[ArkoviaCrossplay] Slot {index} protocol '{clientProtocol}' is not approved; leaving normal server validation in place.");
            return;
        }

        // Terraria 1.4.5.x protocol strings are the same encoded length
        // ("Terraria" + a three-digit protocol number). Refuse any unexpected
        // length mismatch rather than resizing TShock's network buffer.
        var replacement = BuildConnectRequest(ServerProtocol);
        var originalPacketLength = args.Length + 3;
        if (replacement.Length != originalPacketLength)
        {
            TShock.Log.ConsoleWarn($"[ArkoviaCrossplay] Refused rewrite for slot {index}: packet length mismatch ({originalPacketLength} -> {replacement.Length}).");
            return;
        }

        Buffer.BlockCopy(replacement, 0, args.Msg.readBuffer, args.Index - 3, replacement.Length);
        TShock.Log.ConsoleInfo($"[ArkoviaCrossplay] Bridged slot {index}: {clientProtocol} ({description}) -> {ServerProtocol} ({Main.versionNumber}).");
    }

    private static byte[] BuildConnectRequest(string protocol)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0);
        writer.Write((byte)PacketTypes.ConnectRequest);
        writer.Write(protocol);
        writer.Flush();

        var data = stream.ToArray();
        BitConverter.GetBytes((short)data.Length).CopyTo(data, 0);
        return data;
    }

    private void HandleCommand(CommandArgs args)
    {
        var action = args.Parameters.Count == 0 ? "info" : args.Parameters[0].ToLowerInvariant();
        switch (action)
        {
            case "info":
            case "status":
                var bridged = _clientProtocols.Count(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, ServerProtocol, StringComparison.Ordinal));
                args.Player.SendInfoMessage($"Arkovia Crossplay: {(_config.Enabled ? "enabled" : "disabled")}");
                args.Player.SendInfoMessage($"Server: Terraria {Main.versionNumber} / {ServerProtocol}");
                args.Player.SendInfoMessage($"Approved older protocols: {_config.AllowedClientProtocols.Count}; active non-native clients observed: {bridged}");
                args.Player.SendInfoMessage("Mode: handshake compatibility bridge (no packet translation). Console connectivity is not provided by this plugin.");
                break;

            case "versions":
                args.Player.SendInfoMessage("Approved client protocols:");
                foreach (var pair in _config.AllowedClientProtocols.OrderBy(x => x.Key))
                    args.Player.SendInfoMessage($"  {pair.Key} — {pair.Value}");
                break;

            case "verbose":
                _config.Verbose = !_config.Verbose;
                SaveConfig();
                args.Player.SendSuccessMessage($"Arkovia Crossplay verbose logging {(_config.Verbose ? "enabled" : "disabled")}.");
                break;

            case "reload":
                LoadConfig();
                args.Player.SendSuccessMessage("Arkovia Crossplay configuration reloaded.");
                break;

            default:
                args.Player.SendInfoMessage("Usage: /arcrossplay [info|versions|verbose|reload]");
                break;
        }
    }

    private void LoadConfig()
    {
        Directory.CreateDirectory(TShock.SavePath);
        if (!File.Exists(ConfigPath))
        {
            _config = CrossplayConfig.CreateDefault();
            SaveConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            _config = JsonSerializer.Deserialize<CrossplayConfig>(json, JsonOptions()) ?? CrossplayConfig.CreateDefault();
            _config.AllowedClientProtocols ??= new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[ArkoviaCrossplay] Failed to read {ConfigPath}: {ex.Message}");
            _config = CrossplayConfig.CreateDefault();
        }
    }

    private void SaveConfig()
    {
        Directory.CreateDirectory(TShock.SavePath);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_config, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class CrossplayConfig
{
    public bool Enabled { get; set; } = true;
    public bool Verbose { get; set; } = false;
    public Dictionary<string, string>? AllowedClientProtocols { get; set; }

    public static CrossplayConfig CreateDefault() => new()
    {
        Enabled = true,
        Verbose = false,
        // Keep this deliberately narrow. These are known Terraria 1.4.5.x
        // handshake protocols. New versions should be tested before being
        // added rather than accepting an arbitrary version range.
        AllowedClientProtocols = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Terraria311"] = "Terraria 1.4.5.0 (PC/mobile)",
            ["Terraria312"] = "Terraria 1.4.5.1 (PC/mobile)",
            ["Terraria313"] = "Terraria 1.4.5.x compatibility protocol",
            ["Terraria314"] = "Terraria 1.4.5.x compatibility protocol",
            ["Terraria315"] = "Terraria 1.4.5.2 (PC/mobile)",
            ["Terraria316"] = "Terraria 1.4.5.3 (PC/mobile)",
            ["Terraria317"] = "Terraria 1.4.5.4 (PC/mobile)",
            ["Terraria318"] = "Terraria 1.4.5.5 (PC/mobile)",
            ["Terraria319"] = "Terraria 1.4.5.6 (PC/mobile)"
        }
    };
}
