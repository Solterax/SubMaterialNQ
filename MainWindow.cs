using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SubMaterialNQ;

internal sealed class MainWindow(Plugin plugin) : Window("SubMaterialNQ")
{
    private string search = "";
    private KeyValuePair<uint, string>[] suggestions = [];

    public override void PreDraw()
    {
        ImGui.SetNextWindowSize(new Vector2(550, 570), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(400, 310), new Vector2(float.MaxValue, float.MaxValue));
    }

    public override void Draw()
    {
        ImGui.BeginDisabled(plugin.Running);
        if (ImGui.Button("Lower Quality"))
            plugin.Start();
        ImGui.EndDisabled();

        if (plugin.Running)
        {
            ImGui.SameLine();
            if (ImGui.Button("Stop after current item"))
                plugin.Stop();
        }

        ImGui.TextWrapped(plugin.Status);
        ImGui.TextDisabled("Personal inventory only. Enable auto-confirm for Lower Quality.");
        ImGui.Spacing();
        ImGui.Separator();

        ImGui.BeginDisabled(plugin.Running);
        ImGui.TextUnformatted($"Whitelist ({plugin.Config.Whitelist.Count})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Restore defaults"))
            ImGui.OpenPopup("Restore default whitelist?");
        if (ImGui.BeginPopupModal("Restore default whitelist?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Replace the current whitelist with the 76 default items?");
            if (ImGui.Button("Restore"))
            {
                plugin.Reset();
                RefreshSuggestions();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##AddItem", "Search by item name or ID to add", ref search, 120))
            RefreshSuggestions();
        if (!string.IsNullOrWhiteSpace(search))
        {
            ImGui.BeginChild("Search results", new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 5), true);
            if (suggestions.Length == 0)
                ImGui.TextDisabled("No matching items to add.");
            foreach (var item in suggestions)
            {
                if (ImGui.Selectable($"{item.Value} (#{item.Key})##Add{item.Key}"))
                {
                    plugin.Add(item.Key);
                    search = "";
                    RefreshSuggestions();
                    break;
                }
            }
            ImGui.EndChild();
        }

        DrawWhitelist();
        ImGui.EndDisabled();
    }

    private void RefreshSuggestions()
    {
        var query = search.Trim();
        suggestions = query.Length == 0 ? [] : plugin.Search(query).ToArray();
    }

    private void DrawWhitelist()
    {
        if (!ImGui.BeginTable("Whitelist", 2, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Remove").X + 18);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        uint? remove = null;
        foreach (var id in plugin.Config.Whitelist.OrderBy(plugin.ItemName, StringComparer.CurrentCultureIgnoreCase).ThenBy(id => id))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plugin.ItemName(id));
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"Remove##{id}"))
                remove = id;
        }
        ImGui.EndTable();
        if (remove is { } removedId)
        {
            plugin.Remove(removedId);
            RefreshSuggestions();
        }
    }
}
