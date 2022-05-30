using System;
using System.Threading.Tasks;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Torch.API;
using Torch.Managers;
using Torch.Utils;
using Wormhole.ViewModels;

namespace Wormhole.Managers
{
    public class JumpManager : Manager
    {
        [Dependency] private readonly ClientEffectsManager _effectsManager = null!;

        public JumpManager(ITorchBase torchInstance) : base(torchInstance)
        {
        }

        public async Task StartJump(GateViewModel gateViewModel, MyPlayer player, MyCubeGrid grid)
        {
            // TODO hyper jump effect
            _effectsManager.NotifyJumpStatusChanged(JumpStatus.Started, gateViewModel, grid);
            for (var i = 0; i < 10; i++)
            {
                MyVisualScriptLogicProvider.ShowNotification($"Jumping in {10 - i}...",
                    1000, playerId: player.Identity.IdentityId);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            _effectsManager.NotifyJumpStatusChanged(JumpStatus.Ready, gateViewModel, grid);
        }
    }
}