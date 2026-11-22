using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace SubMaterialNQ;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/submaterialnq";
    private const long ReadyTimeoutMs = 3000;
    private const long UpdateTimeoutMs = 5000;

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IClientState Client { get; set; } = null!;
    [PluginService] private static IPlayerState Player { get; set; } = null!;
    [PluginService] private static ICondition Conditions { get; set; } = null!;
    [PluginService] private static IDataManager Data { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly WindowSystem windows = new("SubMaterialNQ");
    private readonly MainWindow window;
    private readonly Dictionary<uint, string> itemNames;
    private Queue<BagSlot>? remaining;
    private BagSlot? submitted;
    private ulong characterId;
    private uint territory;
    private long deadline;
    private int converted;
    private int skipped;
    private bool startRequested;
    private bool stopRequested;

    internal Configuration Config { get; }
    internal bool Running => startRequested || remaining != null;
    internal string Status { get; private set; } = "Ready";

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new() { Whitelist = DefaultWhitelist.Create() };
        itemNames = Data.GetExcelSheet<Item>()
            .Where(item => item.RowId != 0)
            .Select(item => (item.RowId, Name: item.Name.ToString()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.RowId, item => item.Name);
        window = new MainWindow(this);
        windows.AddWindow(window);
        Commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open SubMaterialNQ." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;
        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
    }

    private void OnCommand(string command, string args) => Open();
    private void Open() => window.IsOpen = true;

    internal string ItemName(uint id) => itemNames.GetValueOrDefault(id, $"Item #{id}");

    internal IEnumerable<KeyValuePair<uint, string>> Search(string query) => itemNames
        .Where(item => !Config.Whitelist.Contains(item.Key)
            && (item.Value.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Key.ToString() == query))
        .OrderByDescending(item => item.Value.Equals(query, StringComparison.OrdinalIgnoreCase))
        .ThenBy(item => item.Value, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(item => item.Key)
        .Take(12);

    internal void Add(uint id)
    {
        if (Config.Whitelist.Add(id))
            PluginInterface.SavePluginConfig(Config);
    }

    internal void Remove(uint id)
    {
        if (Config.Whitelist.Remove(id))
            PluginInterface.SavePluginConfig(Config);
    }

    internal void Reset()
    {
        Config.Whitelist = DefaultWhitelist.Create();
        PluginInterface.SavePluginConfig(Config);
    }

    internal void Start()
    {
        if (!Running)
            startRequested = true;
    }

    internal void Stop() => stopRequested = true;

    private bool CanSubmit() => !Conditions[ConditionFlag.Occupied]
        && !Conditions[ConditionFlag.OccupiedInEvent]
        && !Conditions[ConditionFlag.OccupiedInQuestEvent]
        && !Conditions[ConditionFlag.OccupiedSummoningBell]
        && !Conditions[ConditionFlag.TradeOpen]
        && !Conditions[ConditionFlag.Crafting]
        && !Conditions[ConditionFlag.Gathering]
        && !Conditions[ConditionFlag.InCombat];

    private bool InventoryAccessible() => Client.IsLoggedIn && Player.IsLoaded
        && !Conditions[ConditionFlag.BetweenAreas] && !Conditions[ConditionFlag.BetweenAreas51];

    private void OnUpdate(IFramework framework)
    {
        if (!Running)
            return;

        try
        {
            var now = Environment.TickCount64;
            if (startRequested)
            {
                startRequested = false;
                converted = skipped = 0;
                if (!InventoryAccessible() || !CanSubmit())
                {
                    Finish("Open SubMaterialNQ while your character is idle.");
                    return;
                }

                remaining = BagInventory.FindTargets(Config.Whitelist);
                if (remaining == null || remaining.Count == 0)
                {
                    Finish(remaining == null ? "Inventory is still loading." : "No whitelisted HQ or collectable stacks found.");
                    return;
                }
                characterId = Player.ContentId;
                territory = Client.TerritoryType;
                deadline = now + ReadyTimeoutMs;
            }

            if (!InventoryAccessible() || characterId != Player.ContentId || territory != Client.TerritoryType)
            {
                Finish("Stopped because your inventory became unavailable.");
                return;
            }

            Advance(now);
        }
        catch (Exception error)
        {
            Log.Error(error, "SubMaterialNQ could not process the inventory.");
            Finish("Stopped because the inventory could not be processed. See the Dalamud log.");
        }
    }

    private void Advance(long now)
    {
        if (submitted is { } active)
        {
            var state = BagInventory.Read(active);
            if (state == SlotState.Changed)
            {
                Finish($"Stopped because {active.Location} changed during conversion.");
                return;
            }
            if (state != SlotState.Converted)
            {
                if (now >= deadline)
                    Finish($"Stopped at {ItemName(active.ItemId)} ({active.Location}). Check the item and auto-confirm.");
                return;
            }

            converted++;
            submitted = null;
            deadline = now + ReadyTimeoutMs;
        }

        if (stopRequested || !remaining!.TryPeek(out var next))
        {
            Finish(stopRequested ? "Stopped." : "Finished.");
            return;
        }

        var result = CanSubmit() ? BagInventory.TrySubmit(next) : RequestState.Busy;
        if (result == RequestState.Busy)
        {
            if (now >= deadline)
                Finish($"Stopped at {ItemName(next.ItemId)} ({next.Location}). The game or item data stayed busy.");
            else
                Status = $"Waiting to lower {ItemName(next.ItemId)} ({next.Location}).";
            return;
        }

        remaining.Dequeue();
        if (result == RequestState.Sent)
        {
            submitted = next;
            deadline = now + UpdateTimeoutMs;
            Status = $"Lowering {ItemName(next.ItemId)} ({next.Location}). {remaining.Count} queued.";
        }
        else
        {
            skipped++;
            deadline = now + ReadyTimeoutMs;
        }
    }

    private void Finish(string message)
    {
        remaining = null;
        submitted = null;
        stopRequested = false;
        Status = $"{message} Converted {converted} stack(s). Skipped {skipped}.";
    }
}
