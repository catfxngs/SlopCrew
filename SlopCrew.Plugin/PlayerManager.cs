using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Microsoft.Extensions.Hosting;
using Reptile;
using SlopCrew.Common.Proto;

namespace SlopCrew.Plugin;

public class PlayerManager : IHostedService {
    public Dictionary<uint, AssociatedPlayer> Players = new();
    public List<AssociatedPlayer> AssociatedPlayers => this.Players.Values.ToList();

    public bool SettingVisual;
    public bool PlayingAnimation;

    private ManualLogSource logger;
    private ConnectionManager connectionManager;
    private Config config;

    public PlayerManager(ConnectionManager connectionManager, ManualLogSource logger, Config config) {
        this.connectionManager = connectionManager;
        this.logger = logger;
        this.config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        StageManager.OnStageInitialized += this.StageInit;
        this.connectionManager.Disconnected += this.Disconnected;
        this.connectionManager.MessageReceived += this.MessageReceived;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        StageManager.OnStageInitialized -= this.StageInit;
        this.connectionManager.Disconnected -= this.Disconnected;
        this.connectionManager.MessageReceived -= this.MessageReceived;
        this.CleanupPlayers();
        return Task.CompletedTask;
    }

    public AssociatedPlayer? GetAssociatedPlayer(Reptile.Player reptilePlayer) {
        foreach (var associatedPlayer in this.Players.Values) {
            if (associatedPlayer.ReptilePlayer == reptilePlayer) return associatedPlayer;
        }

        return null;
    }

    private void MessageReceived(ClientboundMessage packet) {
        switch (packet.MessageCase) {
            case ClientboundMessage.MessageOneofCase.PlayersUpdate: {
                foreach (var player in packet.PlayersUpdate.Players) {
                    if (this.Players.TryGetValue(player.Id, out var associatedPlayer)) {
                        associatedPlayer.UpdateIfDifferent(player);
                    } else {
                        // New player
                        this.Players.Add(player.Id, new AssociatedPlayer(
                                             this,
                                             this.connectionManager,
                                             this.config,
                                             player));
                    }
                }

                // Remove players that are no longer here
                var packetPlayers = packet.PlayersUpdate.Players.Select(p => p.Id).ToList();
                foreach (var id in this.Players.Keys.ToList()) {
                    if (!packetPlayers.Contains(id) && this.Players.TryGetValue(id, out var associatedPlayer)) {
                        this.logger.LogDebug("supposed to be removing player " + id);
                        associatedPlayer.Dispose();
                        this.Players.Remove(id);
                    }
                }

                break;
            }

            case ClientboundMessage.MessageOneofCase.PositionUpdate: {
                foreach (var update in packet.PositionUpdate.Updates) {
                    if (this.Players.TryGetValue(update.PlayerId, out var player)) {
                        player.QueuePositionUpdate(update);
                    }
                }

                break;
            }

            case ClientboundMessage.MessageOneofCase.VisualUpdate: {
                foreach (var update in packet.VisualUpdate.Updates) {
                    if (this.Players.TryGetValue(update.PlayerId, out var player)) {
                        player.HandleVisualUpdate(update);
                    }
                }

                break;
            }

            case ClientboundMessage.MessageOneofCase.AnimationUpdate: {
                foreach (var update in packet.AnimationUpdate.Updates) {
                    if (this.Players.TryGetValue(update.PlayerId, out var player)) {
                        player.HandleAnimationUpdate(update);
                    }
                }

                break;
            }
        }
    }

    private void Disconnected() => this.CleanupPlayers();
    private void StageInit() => this.CleanupPlayers();

    private void CleanupPlayers() {
        foreach (var player in this.Players.Values) player.Dispose();
        this.Players.Clear();
    }
}
