using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // Per-command spam guard. The Rust+ team chat echoes messages back via the polling history
    // path, so without this a single typed "!pop" can trigger the handler twice. 2s mirrors the
    // upstream blocker (a4586de).
    private readonly Dictionary<string, DateTime> _lastChatCmdAt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ChatCommandCooldown = TimeSpan.FromSeconds(2);

    private bool ShouldThrottleChatCommand(string cmdKey)
    {
        var now = DateTime.UtcNow;
        if (_lastChatCmdAt.TryGetValue(cmdKey, out var last) && (now - last) < ChatCommandCooldown)
            return true;
        _lastChatCmdAt[cmdKey] = now;
        return false;
    }

    // Routed from Real_TeamChatReceived after the dedup gate. Fire-and-forget; failures are logged
    // and never raise to the chat handler (which would prevent the message from rendering inline).
    private async Task ProcessChatCommands(TeamChatMessage m)
    {
        try
        {
            var profile = _vm?.Selected;
            if (profile == null || !profile.ChatCommandsEnabled) return;

            var raw = (m.Text ?? "").Trim();
            if (string.IsNullOrEmpty(raw) || !raw.StartsWith("!")) return;
            var cmd = raw.Substring(1).ToLowerInvariant();
            if (string.IsNullOrEmpty(cmd)) return;

            if (_rust is not RustPlusClientReal real) return;

            // ── !pop ──
            if (cmd == (profile.CmdPop ?? "").ToLowerInvariant() && !string.IsNullOrEmpty(profile.CmdPop))
            {
                if (ShouldThrottleChatCommand("pop")) return;
                string queueText = (_vm.ServerQueue != "0" && _vm.ServerQueue != "-")
                    ? $" Queue: {_vm.ServerQueue} players."
                    : "";
                await SendTeamChatSafeAsync($"{_vm.ServerPlayers} players currently online.{queueText}");
                AppendLog($"[ChatCommand] !pop by {m.Author}");
                return;
            }

            // ── !time ──
            if (cmd == (profile.CmdTime ?? "").ToLowerInvariant() && !string.IsNullOrEmpty(profile.CmdTime))
            {
                if (ShouldThrottleChatCommand("time")) return;
                string msg = $"Current in-game time: {_vm.ServerTime}.";
                if (!string.IsNullOrWhiteSpace(_vm.TimeUntilNextPhase))
                    msg += $" ({_vm.TimeUntilNextPhase})";
                await SendTeamChatSafeAsync(msg);
                AppendLog($"[ChatCommand] !time by {m.Author}");
                return;
            }

            // ── !leader / !promote ──
            if (cmd == (profile.CmdPromote ?? "").ToLowerInvariant() && !string.IsNullOrEmpty(profile.CmdPromote))
            {
                if (ShouldThrottleChatCommand("promote")) return;
                if (m.SteamId == 0)
                {
                    AppendLog("[ChatCommand] !promote skipped — no SteamId on message.");
                    return;
                }
                _ = real.PromoteToLeaderAsync(m.SteamId);
                await SendTeamChatSafeAsync($"{m.Author} was promoted to leader.");
                AppendLog($"[ChatCommand] !promote by {m.Author} ({m.SteamId})");
                return;
            }

            // ── !cargo ──
            if (cmd == (profile.CmdCargo ?? "").ToLowerInvariant() && !string.IsNullOrEmpty(profile.CmdCargo))
            {
                if (ShouldThrottleChatCommand("cargo")) return;
                string msg;
                if (_cargoDockStates.Count > 0)
                {
                    // Pick the cargo with the most info to report on. The fork doesn't have learned
                    // full-life data, so we surface "how long visible" instead of "time until leave".
                    var c = _cargoDockStates.Values.FirstOrDefault(x => x.FirstSeen.HasValue) ?? _cargoDockStates.Values.First();
                    if (c.FirstSeen.HasValue)
                    {
                        var visibleMin = (int)Math.Max(0, (DateTime.UtcNow - c.FirstSeen.Value).TotalMinutes);
                        msg = $"Cargo Ship active. Visible for {visibleMin} min.";
                    }
                    else
                    {
                        msg = "Cargo Ship active.";
                    }
                }
                else
                {
                    msg = "Cargo Ship not currently active.";
                }
                await SendTeamChatSafeAsync(msg);
                AppendLog($"[ChatCommand] !cargo by {m.Author}");
                return;
            }

            // ── !switch1 / !switch2 ──
            if (cmd == (profile.CmdSwitch1 ?? "").ToLowerInvariant() && profile.BoundSwitchId1.HasValue)
            {
                if (ShouldThrottleChatCommand("switch1")) return;
                await ToggleBoundCommandSwitch(real, profile, profile.BoundSwitchId1.Value, m.Author);
                return;
            }
            if (cmd == (profile.CmdSwitch2 ?? "").ToLowerInvariant() && profile.BoundSwitchId2.HasValue)
            {
                if (ShouldThrottleChatCommand("switch2")) return;
                await ToggleBoundCommandSwitch(real, profile, profile.BoundSwitchId2.Value, m.Author);
                return;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ChatCommand] error: {ex.Message}");
        }
    }

    private async Task ToggleBoundCommandSwitch(RustPlusClientReal real, ServerProfile profile, uint entityId, string author)
    {
        var dev = FindSwitchInProfile(profile, entityId);
        if (dev == null)
        {
            await SendTeamChatSafeAsync("Bound Smart Switch not found or not paired.");
            return;
        }
        bool newState = !(dev.IsOn ?? false);
        try
        {
            await real.ToggleSmartSwitchAsync(entityId, newState);
            dev.IsOn = newState;
            await SendTeamChatSafeAsync($"{dev.Name ?? dev.Alias ?? "Switch"} turned {(newState ? "ON" : "OFF")}.");
            AppendLog($"[ChatCommand] switch #{entityId} -> {(newState ? "ON" : "OFF")} by {author}");
        }
        catch (Exception ex)
        {
            AppendLog($"[ChatCommand] toggle #{entityId} failed: {ex.Message}");
        }
    }

    // Devices in this fork can be hierarchical (groups with children) — walk the tree until we
    // find a SmartSwitch with the requested EntityId.
    private static SmartDevice? FindSwitchInProfile(ServerProfile profile, uint entityId)
    {
        if (profile?.Devices == null) return null;
        foreach (var d in profile.Devices)
        {
            var hit = FindSwitchRecursive(d, entityId);
            if (hit != null) return hit;
        }
        return null;
    }

    private static SmartDevice? FindSwitchRecursive(SmartDevice d, uint entityId)
    {
        if (d == null) return null;
        if (d.EntityId == entityId &&
            string.Equals(d.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase))
            return d;
        if (d.Children != null)
        {
            foreach (var c in d.Children)
            {
                var hit = FindSwitchRecursive(c, entityId);
                if (hit != null) return hit;
            }
        }
        return null;
    }
}
