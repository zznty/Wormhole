using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Wormhole.Mod
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class JumpComponent : MySessionComponentBase
    {
        private static readonly MySoundPair ChargeSound = new MySoundPair("ShipJumpDriveCharging");
        private static readonly MySoundPair AfterSound = new MySoundPair("ShipJumpDriveJumpIn");

        public static JumpComponent Instance;
        
        public readonly Dictionary<uint, GateDataMessage> Gates = new Dictionary<uint, GateDataMessage>();
        private readonly MyEntity3DSoundEmitter _soundEmitter = new MyEntity3DSoundEmitter(null);

        public readonly List<SerializableDefinitionId> JdDefinitionIds = new List<SerializableDefinitionId>();
        public bool? WorkWithAllJds;

        private readonly Dictionary<IMyCubeGrid, WarpEffect> _warpEffects = new Dictionary<IMyCubeGrid, WarpEffect>();

        private class WarpEffect : IDisposable
        {
            public static bool TryCreate(Vector3D gatePos, IMyCubeGrid cubeGrid, string name, out WarpEffect effect)
            {
                var dirToGateNorm = cubeGrid.GetPosition() - gatePos;
                dirToGateNorm.Normalize();
                
                var matrix = MatrixD.CreateFromDir(dirToGateNorm);
                var offset = dirToGateNorm * cubeGrid.WorldAABB.HalfExtents.AbsMax() * 2;
                matrix.Translation = cubeGrid.WorldAABB.Center + offset;
                var position = cubeGrid.GetPosition();
                
                MyParticleEffect particle;
                if (!MyParticlesManager.TryCreateParticleEffect(name, ref matrix, ref position,
                        cubeGrid.Render.ParentIDs[0], out particle))
                {
                    effect = null;
                    return false;
                }

                effect = new WarpEffect
                {
                    Offset = offset,
                    CubeGrid = cubeGrid,
                    Particle = particle
                };
                return true;
            }

            public IMyCubeGrid CubeGrid { get; private set; }
            public MyParticleEffect Particle { get; private set; }
            public Vector3 Offset { get; private set; }

            public void Update()
            {
                var pos = CubeGrid.WorldAABB.Center + Offset;
                Particle.SetTranslation(ref pos);
            }

            public void Dispose()
            {
                Particle.Stop();
                MyParticlesManager.RemoveParticleEffect(Particle);
                Particle = null;
            }
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            Instance = this;
            if (MyAPIGateway.Multiplayer.IsServer) return;
            base.Init(sessionComponent);
            RegisterHandlers();
        }

        public override void BeforeStart()
        {
            if (MyAPIGateway.Multiplayer.IsServer) return;
            base.BeforeStart();
            RequestGatesData();
        }

        #region Netowrking

        private const ushort JumpStatusNetId = 3456;
        private const ushort GateDataNetId = 3457;

        private void RegisterHandlers()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(JumpStatusNetId, JumpStatusHandler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(GateDataNetId, GateDataHandler);
        }

        private static void RequestGatesData()
        {
            MyAPIGateway.Multiplayer.SendMessageToServer(GateDataNetId, new byte[1]);
        }

        private void JumpStatusHandler(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            var message = MyAPIGateway.Utilities.SerializeFromBinary<JumpStatusMessage>(data);
            IMyEntity entity;
            IMyCubeGrid grid;
            GateDataMessage gate;
            if (message == null || !fromServer || !MyAPIGateway.Entities.TryGetEntityById(message.GridId, out entity) ||
                (grid = entity as IMyCubeGrid) == null || !Gates.TryGetValue(message.GateId, out gate))
                return;
            MyLog.Default?.WriteLine($"Jump status update {message.Status}");
            switch (message.Status)
            {
                case JumpStatus.Started:
                    OnJumpStarted(gate, grid);
                    break;
                case JumpStatus.Ready:
                    OnJumpReady(gate, grid);
                    break;
                case JumpStatus.Perform:
                    OnJumpPerform(gate, grid, message.Destination);
                    break;
                case JumpStatus.Succeeded:
                    OnJumpSucceeded(gate, grid);
                    break;
                default:
                    throw new Exception("Out of Range");
            }
        }

        private void GateDataHandler(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            var message = MyAPIGateway.Utilities.SerializeFromBinary<GatesMessage>(data);
            if (message == null || !fromServer)
                return;
            MyLog.Default?.WriteLine($"Loaded {message.Messages} gates");
            OnGatesData(message.Messages);
            OnJdData(message.WormholeDriveIds, message.WorkWithAllJds);
        }
        #endregion

        private void OnJumpStarted(GateDataMessage gate, IMyCubeGrid grid)
        {
            _soundEmitter.Entity = (MyEntity) grid;
            _soundEmitter.PlaySound(ChargeSound);

            WarpEffect effect;
            if (_warpEffects.TryGetValue(grid, out effect))
            {
                effect.Dispose();
                _warpEffects.Remove(grid);
            }
            
            if (WarpEffect.TryCreate(gate.Position, grid, "Warp", out effect))
                _warpEffects.Add(grid, effect);
            
            // if (MyAPIGateway.Session.ControlledObject is IMyShipController &&
            //     ((IMyShipController)MyAPIGateway.Session.ControlledObject).CubeGrid == grid)
            // {
            //     MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.SpectatorFixed,
            //         position: gate.Position + gate.Forward * (gate.Size * 2));
            // }
        }

        private void OnJumpReady(GateDataMessage gate, IMyCubeGrid grid)
        {
            WarpEffect effect;
            if (!_warpEffects.TryGetValue(grid, out effect)) 
                return;
            
            effect.Dispose();
            _warpEffects.Remove(grid);
        }
        private void OnJumpPerform(GateDataMessage gate, IMyCubeGrid grid, Vector3D destination)
        {
            if (Vector3D.IsZero(destination)) return;
            var matrix = grid.WorldMatrix;
            matrix.Translation = destination;
            grid.Teleport(matrix);
            
            // if (MyAPIGateway.Session.ControlledObject is IMyShipController &&
            //     ((IMyShipController)MyAPIGateway.Session.ControlledObject).CubeGrid == grid)
            // {
            //     MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Entity, grid);
            // }
        }
        private void OnJumpSucceeded(GateDataMessage gate, IMyCubeGrid grid)
        {
            if (MyAPIGateway.Session.ControlledObject is IMyShipController &&
                ((IMyShipController)MyAPIGateway.Session.ControlledObject).CubeGrid == grid)
            {
                _soundEmitter.PlaySound(AfterSound, true);
            }
        }

        private void OnGatesData(IEnumerable<GateDataMessage> gates)
        {
            Gates.Clear();
            foreach (var gate in gates)
            {
                Gates[gate.Id] = gate;
            }
        }

        private void OnJdData(IEnumerable<SerializableDefinitionId> definitionIds, bool workWithAllJds)
        {
            JdDefinitionIds.Clear();
            JdDefinitionIds.AddRange(definitionIds);
            WorkWithAllJds = workWithAllJds;
        }

        public override void UpdateBeforeSimulation()
        {
            base.UpdateBeforeSimulation();
            foreach (var warpEffect in _warpEffects.Values)
            {
                warpEffect.Update();
            }
        }
    }
}