# SubMaterialNQ

SubMaterialNQ is a standalone Dalamud plugin that lowers the quality of whitelisted HQ and collectable stacks in your four personal inventory bags.

## Use

1. Install SubMaterialNQ from [Solterax's Dalamud catalogue](https://github.com/Solterax/DalamudPlugins).
2. Enable Lower Quality in your auto-confirm plugin.
3. Open `/submaterialnq`. Search by item name or ID to add items, or use **Remove** beside a whitelist entry. **Restore defaults** replaces the list with the original 76 entries.
4. Click **Lower Quality** while idle. Keep the bags undisturbed until the run finishes. **Stop after current item** lets a submitted request finish before stopping.

The whitelist uses base item IDs and is saved locally. **Coel Stern** is listed by its in-game name, **Coelacanth-class Stern**. It stays on the default list, but the plugin only acts on HQ or collectable inventory entries.

The plugin checks the game's native item data on the framework thread before calling Lower Quality. It sends one request at a time and checks that the inventory stack changed before continuing. The timeout message identifies the item and slot to check.

## Build

Build with the .NET 10 SDK and Dalamud API 15. Set the library path to your installed Dalamud API 15 directory.

```powershell
dotnet build SubMaterialNQ.csproj -c Release "-p:DalamudLibPath=C:/path/to/Dalamud/"
```
