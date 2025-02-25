﻿using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Reptile;
using SlopCrew.API;
using SlopCrew.Common;
using SlopCrew.Common.Proto;
using Player = SlopCrew.Common.Proto.Player;

namespace SlopCrew.Plugin;

public class LocalPlayerManager(
    Config config,
    ConnectionManager connectionManager,
    CharacterInfoManager characterInfoManager
) : IHostedService {
    public bool HelloRefreshQueued;
    public bool VisualRefreshQueued;
    public int CurrentOutfit;

    private Transform? lastTransform;
    private int? lastAnimation;

    public Task StartAsync(CancellationToken cancellationToken) {
        connectionManager.Tick += this.Tick;
        connectionManager.MessageReceived += this.MessageReceived;
        StageManager.OnStagePostInitialization += this.StagePostInit;
        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken cancellationToken) {
        connectionManager.Tick -= this.Tick;
        connectionManager.MessageReceived -= this.MessageReceived;
        StageManager.OnStagePostInitialization -= this.StagePostInit;
        return Task.CompletedTask;
    }

    private void MessageReceived(ClientboundMessage message) {
        if (message.MessageCase is ClientboundMessage.MessageOneofCase.Hello) {
            this.HelloRefreshQueued = true;
        }
    }

    private void Tick() {
        var worldHandler = WorldHandler.instance;
        if (worldHandler == null) return;
        var me = worldHandler.GetCurrentPlayer();
        if (me == null) return;

        this.HandleRefreshes(me);
        this.HandleMovement(me);
    }

    private void HandleRefreshes(Reptile.Player me) {
        if (this.HelloRefreshQueued) {
            var key = config.Server.Key.Value ?? string.Empty;
            var character = (int) me.character;
            var outfit = this.CurrentOutfit;
            var infos = characterInfoManager.GetCharacterInfo(character);

            var characterOverride = config.General.CharacterOverride.Value;
            var outfitOverride = config.General.OutfitOverride.Value;
            if (characterOverride != CharacterOverride.None) {
                character = (int) characterOverride;
                if (outfitOverride is >= 0 and <= 3) outfit = outfitOverride;
            }

            connectionManager.SendMessage(new ServerboundMessage {
                Hello = new ServerboundHello {
                    Player = new Player {
                        Name = config.General.Username.Value,

                        Transform = new Transform {
                            Position = new(me.tf.position.FromMentalDeficiency()),
                            Rotation = new(me.tf.rotation.FromMentalDeficiency()),
                            Velocity = new(me.motor.velocity.FromMentalDeficiency())
                        },

                        CharacterInfo = new CharacterInfo {
                            Character = character,
                            Outfit = outfit,
                            MoveStyle = (int) me.moveStyle
                        },

                        CustomCharacterInfo = {infos}
                    },

                    Stage = APIManager.API!.StageOverride ?? (int) Core.instance.baseModule.CurrentStage,
                    Key = key
                }
            });

            this.HelloRefreshQueued = false;
        }

        if (this.VisualRefreshQueued) {
            connectionManager.SendMessage(new ServerboundMessage {
                VisualUpdate = new ServerboundVisualUpdate {
                    Update = new VisualUpdate {
                        Boostpack = (int) me.characterVisual.boostpackEffectMode,
                        Friction = (int) me.characterVisual.frictionEffectMode,
                        Spraycan = me.characterVisual.VFX.spraycan.activeSelf,
                        Phone = me.characterVisual.VFX.phone.activeSelf,
                        SpraycanState = (int) me.spraycanState
                    }
                }
            });

            this.VisualRefreshQueued = false;
        }
    }


    private void HandleMovement(Reptile.Player me) {
        var transform = me.tf;
        var newPos = transform.position.FromMentalDeficiency();
        var newRot = transform.rotation.FromMentalDeficiency();
        var newVel = me.motor.velocity.FromMentalDeficiency();

        const float minDistance = 0.01f;

        if (this.lastTransform == null
            || (this.lastTransform.Position - newPos).LengthSquared() > minDistance
            || (this.lastTransform.Rotation - newRot).LengthSquared() > minDistance
           ) {
            var newTransform = new Transform {
                Position = new(newPos),
                Rotation = new(newRot),
                Velocity = new(newVel)
            };

            connectionManager.SendMessage(new ServerboundMessage {
                PositionUpdate = new ServerboundPositionUpdate {
                    Update = new PositionUpdate {
                        Transform = newTransform,
                        Tick = connectionManager.ServerTick,
                        Latency = connectionManager.Latency
                    }
                }
            }, flags: SendFlags.Unreliable);

            this.lastTransform = newTransform;
        }
    }


    private void StagePostInit() {
        this.HelloRefreshQueued = true;
    }

    public void PlayAnim(int anim, bool forceOverwrite, bool instant, float atTime) {
        // Sometimes the game spams animation
        if (this.lastAnimation == anim) return;
        this.lastAnimation = anim;

        connectionManager.SendMessage(new ServerboundMessage {
            AnimationUpdate = new ServerboundAnimationUpdate {
                Update = new AnimationUpdate {
                    Animation = anim,
                    ForceOverwrite = forceOverwrite,
                    Instant = instant,
                    Time = atTime
                }
            }
        });
    }
}
