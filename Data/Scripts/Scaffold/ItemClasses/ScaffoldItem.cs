using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ScaffoldMod.Settings;
using ScaffoldMod.Utility;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using VRage.Collections;
using VRage.Game.Components;

namespace ScaffoldMod.ItemClasses
{
    public enum ScaffoldType : byte
    {
        Disabled,
        Weld,
        Grind,
        Invalid,
        Scanning
    }

    public class ScaffoldItem
    {
        private MyTuple<bool, bool> _shouldDisable;
        //public int ActiveTargets;

        //these are set when processing a grid
        //public IMyCubeGrid Grid;
        //tool, target block
        public Dictionary<long, BlockTarget[]> BlocksToProcess = new Dictionary<long, BlockTarget[]>();

        public List<LineItem> BoxLines = new List<LineItem>(12);
        public HashSet<IMyTerminalBlock> ConnectedCargo = new HashSet<IMyTerminalBlock>();

        public MyConcurrentHashSet<IMyCubeGrid> ContainsGrids = new MyConcurrentHashSet<IMyCubeGrid>();
        public HashSet<IMyCubeGrid> IntersectsGrids = new HashSet<IMyCubeGrid>();

        public LCDMenu Menu = null;

        public Dictionary<string, int> MissingComponentsDict = new Dictionary<string, int>();
        public Dictionary<long, List<BlockTarget>> ProxDict = new Dictionary<long, List<BlockTarget>>();

        public YardSettingsStruct Settings;
        public MyOrientedBoundingBoxD ScaffoldBox;
        public HashSet<BlockTarget> TargetBlocks = new HashSet<BlockTarget>();
        public IMyCubeBlock[] Tools;
        //public int TotalBlocks;
        public IMyEntity YardEntity;
        public List<IMyCubeGrid> YardGrids = new List<IMyCubeGrid>();

        public ScaffoldType YardType;
        public bool StaticYard;

        public ScaffoldItem(MyOrientedBoundingBoxD box, IMyCubeBlock[] tools, ScaffoldType yardType, IMyEntity yardEntity)
        {
            ScaffoldBox = box;
            Tools = tools;
            YardType = yardType;
            YardEntity = yardEntity;
            StaticYard = tools[0].BlockDefinition.SubtypeId == "ScaffoldCorner_Large";
        }

        public long EntityId
        {
            get { return YardEntity.EntityId; }
        }

        public void Init(ScaffoldType yardType)
        {
            if (YardType == yardType)
                return;

            Logging.Instance.WriteDebug("YardItem.Init: " + yardType);

            YardType = yardType;

            foreach (IMyCubeGrid grid in ContainsGrids)
            {
                ((MyCubeGrid)grid).OnGridSplit += OnGridSplit;
            }

            YardGrids = ContainsGrids.Where(x => !x.Closed && !x.MarkedForClose).ToList();
            ContainsGrids.Clear();
            IntersectsGrids.Clear();
            Utilities.Invoke(() =>
                             {
                                 foreach (IMyCubeBlock tool in Tools)
                                 {
                                     var myFunctionalBlock = tool as IMyFunctionalBlock;
                                     if (myFunctionalBlock != null)
                                         myFunctionalBlock.Enabled = true; //.RequestEnable(true);
                                 }
                             });

            Communication.SendYardState(this);
        }

        public void Disable(bool broadcast = true)
        {
            _shouldDisable.Item1 = true;
            _shouldDisable.Item2 = broadcast;
        }

        public void ProcessDisable()
        {
            if (!_shouldDisable.Item1)
                return;

            foreach (IMyCubeGrid grid in YardGrids)
                ((MyCubeGrid)grid).OnGridSplit -= OnGridSplit;

            YardGrids.Clear();

            foreach (IMyCubeBlock tool in Tools)
            {
                BlocksToProcess[tool.EntityId] = new BlockTarget[3];
                if (YardType == ScaffoldType.Invalid)
                {
                    Utilities.Invoke(() =>
                                     {
                                         var comp = tool.GameLogic.GetAs<ScaffoldCorner>();
                                         comp.SetPowerUse(5);
                                         comp.SetMaxPower(5);
                                         comp.Scaffold = null;
                                     });
                }
            }
            //TotalBlocks = 0;
            MissingComponentsDict.Clear();
            ContainsGrids.Clear();
            IntersectsGrids.Clear();
            ProxDict.Clear();
            TargetBlocks.Clear();
            YardType = ScaffoldType.Disabled;
            if (_shouldDisable.Item2 && MyAPIGateway.Multiplayer.IsServer)
                Communication.SendYardState(this);

            _shouldDisable.Item1 = false;
            _shouldDisable.Item2 = false;
        }

        public void HandleButtonPressed(int index)
        {
            Communication.SendButtonAction(YardEntity.EntityId, index);
        }


        public void UpdatePowerUse(float addedPower = 0)
        {
            addedPower /= 8;
            if (YardType == ScaffoldType.Disabled || YardType == ScaffoldType.Invalid)
            {
                Utilities.Invoke(() =>
                                 {
                                     foreach (IMyCubeBlock tool in Tools)
                                     {
                                         tool.GameLogic.GetAs<ScaffoldCorner>().SetPowerUse(5 + addedPower);
                                         Communication.SendToolPower(tool.EntityId, 5 + addedPower);
                                     }
                                 });
            }
            else
            {
                Utilities.Invoke(() =>
                                 {
                                     foreach (IMyCubeBlock tool in Tools)
                                     {
                                         float power = 5;
                                         foreach (BlockTarget blockTarget in BlocksToProcess[tool.EntityId])
                                         {
                                             if (blockTarget == null)
                                                 continue;
                                             
                                             float powerReq = 30 + (float)Math.Pow(blockTarget.ToolDist[tool.EntityId], 0.7) * 2;
                                             if (YardType == ScaffoldType.Weld)
                                                 power += powerReq * Settings.WeldMultiplier;
                                             else if (YardType == ScaffoldType.Grind)
                                                 power += powerReq * Settings.GrindMultiplier;
                                         }
                                         
                                         if (!StaticYard)
                                             power *= 2;
                                         tool.GameLogic.GetAs<ScaffoldCorner>().SetPowerUse(power);
                                         Communication.SendToolPower(tool.EntityId, power);
                                     }
                                 });
            }
        }

        public void OnGridSplit(MyCubeGrid oldGrid, MyCubeGrid newGrid)
        {
            if (YardGrids.Any(g => g.EntityId == oldGrid.EntityId))
            {
                newGrid.OnGridSplit += OnGridSplit;
                YardGrids.Add(newGrid);
            }
        }

        public void UpdatePosition()
        {
            ScaffoldBox = MathUtility.CreateOrientedBoundingBox((IMyCubeGrid)YardEntity, Tools.Select(x => x.GetPosition()).ToList(), 2.5);
        }

        /// <summary>
        /// Gives grids in the Scaffold a slight nudge to help them match velocity when the Scaffold is moving.
        /// 
        /// Code donated by Equinox
        /// </summary>
        public void NudgeGrids()
        {
        //magic value of 0.005 here was determined experimentally.
        //value is just enough to assist with matching velocity to the Scaffold, but not enough to prevent escape
            foreach (var grid in ContainsGrids)
            {
                if (grid.Physics?.IsStatic != false || YardEntity.Physics?.IsStatic != false || Vector3D.IsZero(grid.Physics.LinearVelocity - YardEntity.Physics.LinearVelocity))
                    continue;

                grid.Physics.ApplyImpulse(grid.Physics.Mass * Vector3D.ClampToSphere((YardEntity.Physics.LinearVelocity - grid.Physics.LinearVelocity), 0.01), grid.Physics.CenterOfMassWorld);
            }
            
            foreach (var grid in YardGrids)
            {
                if (grid.Physics?.IsStatic != false || YardEntity.Physics?.IsStatic != false)
                    continue;

                if (!Settings.AdvancedLocking)
                    grid.Physics.ApplyImpulse(grid.Physics.Mass * Vector3D.ClampToSphere((YardEntity.Physics.LinearVelocity - grid.Physics.LinearVelocity), 0.01), grid.Physics.CenterOfMassWorld);
                else
                {
                    double powerUse = MathUtility.MatchShipVelocity(grid, YardEntity, true);
                    if(powerUse > 0)
                    UpdatePowerUse((float)powerUse);
                }
            }
        }
    }
}