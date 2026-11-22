using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Exd;

namespace SubMaterialNQ;

internal readonly record struct BagSlot(InventoryType Bag, int Index, uint ItemId, uint QualityId, int Quantity, ushort Collectability)
{
    internal string Location => $"bag {(int)Bag + 1}, slot {Index + 1}";
}

internal enum SlotState { Unavailable, Matches, Converted, Changed }
internal enum RequestState { Busy, Sent, Changed }

internal static unsafe class BagInventory
{
    private static readonly InventoryType[] Bags =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

    internal static Queue<BagSlot>? FindTargets(HashSet<uint> whitelist)
    {
        var targets = new Queue<BagSlot>();
        foreach (var bag in Bags)
        {
            var container = GetContainer(bag);
            if (container == null)
                return null;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item != null && item->VirtualTable != null && !item->IsEmpty()
                    && (item->IsHighQuality() || item->IsCollectable())
                    && whitelist.Contains(item->GetBaseItemId()))
                {
                    targets.Enqueue(new BagSlot(bag, slot, item->GetBaseItemId(), item->GetItemId(),
                        item->Quantity, item->SpiritbondOrCollectability));
                }
            }
        }
        return targets;
    }

    internal static SlotState Read(BagSlot target)
    {
        var item = GetSlot(target);
        if (item == null)
            return SlotState.Unavailable;
        if (item->ItemId == 0)
            return SlotState.Converted; // An HQ stack can merge into an existing NQ stack.
        if (item->VirtualTable == null)
            return SlotState.Unavailable;
        if (item->GetBaseItemId() != target.ItemId)
            return SlotState.Changed;
        if (!item->IsHighQuality() && !item->IsCollectable())
            return SlotState.Converted;
        return IsSameStack(item, target) ? SlotState.Matches : SlotState.Changed;
    }

    internal static RequestState TrySubmit(BagSlot target)
    {
        var item = GetSlot(target);
        if (item == null || item->VirtualTable == null)
            return RequestState.Busy;
        if (item->IsEmpty() || !IsSameStack(item, target))
            return RequestState.Changed;

        var agent = AgentInventoryContext.Instance();
        if (agent == null || agent->BlockingAddonId != 0 || agent->DialogType != 0)
            return RequestState.Busy;

        // LowerItemQuality defers to the context menu's saved target when the native Item row is cold.
        // Load/check the same row on the framework thread before submitting a direct request.
        if (ExdModule.GetItemRowById(target.QualityId) == null)
            return RequestState.Busy;

        agent->LowerItemQuality(item, target.Bag, target.Index, 0);
        return RequestState.Sent;
    }

    private static bool IsSameStack(InventoryItem* item, BagSlot target) =>
        item->GetItemId() == target.QualityId && item->Quantity == target.Quantity
        && (!item->IsCollectable() || item->SpiritbondOrCollectability == target.Collectability);

    private static InventoryItem* GetSlot(BagSlot target)
    {
        var container = GetContainer(target.Bag);
        return container != null && target.Index < container->Size ? container->GetInventorySlot(target.Index) : null;
    }

    private static InventoryContainer* GetContainer(InventoryType bag)
    {
        var manager = InventoryManager.Instance();
        var container = manager != null ? manager->GetInventoryContainer(bag) : null;
        return container != null && container->IsLoaded ? container : null;
    }
}
