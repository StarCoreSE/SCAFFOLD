﻿using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using ScaffoldMod.ItemClasses;
using ScaffoldMod.Settings;
using ScaffoldMod.Utility;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace ScaffoldMod
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "ScaffoldCorner_Large", "ScaffoldCorner_Small")]
    public class ScaffoldCorner : MyGameLogicComponent
    {
        private static bool _init;
        private static readonly MyDefinitionId PowerDef = MyResourceDistributorComponent.ElectricityId;
        private static readonly List<IMyTerminalControl> Controls = new List<IMyTerminalControl>();
        private IMyCollector _block;
        private float _maxpower;
        private float _power;
        private string _info = String.Empty;

        private MyResourceSinkComponent _sink = new MyResourceSinkComponent();

        public ScaffoldItem Scaffold = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _block = (IMyCollector)Container.Entity;
            _block.Components.TryGet(out _sink);

            // Set update flags
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;

            // Set event handlers
            _block.OnClosing += OnClosing;
            _block.AppendingCustomInfo += AppendingCustomInfo;
        }


        private void OnClosing(IMyEntity obj)
        {
            _block.OnClosing -= OnClosing;
            _block.AppendingCustomInfo -= AppendingCustomInfo;
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public override void Close()
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_init)
                return;

            _init = true;
            _block = Entity as IMyCollector;

            if (_block == null)
                return;

            //create terminal controls
            IMyTerminalControlSeparator sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyCollector>(string.Empty);
            sep.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(sep);

            IMyTerminalControlOnOffSwitch guideSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>("Scaffold_GuideSwitch");
            guideSwitch.Title = MyStringId.GetOrCompute("Guide Boxes");
            guideSwitch.Tooltip = MyStringId.GetOrCompute("Toggles the guide boxes drawn around grids in the Scaffold.");
            guideSwitch.OnText = MyStringId.GetOrCompute("On");
            guideSwitch.OffText = MyStringId.GetOrCompute("Off");
            guideSwitch.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            guideSwitch.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null;
            guideSwitch.SupportsMultipleBlocks = true;
            guideSwitch.Getter = GetGuideEnabled;
            guideSwitch.Setter = SetGuideEnabled;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(guideSwitch);
            Controls.Add(guideSwitch);

            var lockSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>("Scaffold_LockSwitch");
            lockSwitch.Title = MyStringId.GetOrCompute("Advanced Locking");
            lockSwitch.Tooltip = MyStringId.GetOrCompute("Toggles locking grids in the Scaffold when grinding or welding while moving.");
            lockSwitch.OnText=MyStringId.GetOrCompute("On");
            lockSwitch.OffText = MyStringId.GetOrCompute("Off");
            lockSwitch.Visible = b => b.BlockDefinition.SubtypeId.Equals("ScaffoldCorner_Small");
            lockSwitch.Enabled = b => b.BlockDefinition.SubtypeId.Equals("ScaffoldCorner_Small") && GetYard(b) != null;
            lockSwitch.SupportsMultipleBlocks = true;
            lockSwitch.Getter = GetLockEnabled;
            lockSwitch.Setter = SetLockEnabled;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(lockSwitch);
            Controls.Add(lockSwitch);

            IMyTerminalControlButton grindButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Scaffold_GrindButton");
            IMyTerminalControlButton weldButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Scaffold_WeldButton");
            IMyTerminalControlButton stopButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Scaffold_StopButton");

            grindButton.Title = MyStringId.GetOrCompute("Grind");
            grindButton.Tooltip = MyStringId.GetOrCompute("Begins grinding ships in the yard.");
            grindButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b)?.YardType == ScaffoldType.Disabled;
            grindButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            grindButton.SupportsMultipleBlocks = true;
            grindButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Grind);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindButton);
            Controls.Add(grindButton);

            weldButton.Title = MyStringId.GetOrCompute("Weld");
            weldButton.Tooltip = MyStringId.GetOrCompute("Begins welding ships in the yard.");
            weldButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b)?.YardType == ScaffoldType.Disabled;
            weldButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            weldButton.SupportsMultipleBlocks = true;
            weldButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Weld);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldButton);
            Controls.Add(weldButton);

            stopButton.Title = MyStringId.GetOrCompute("Stop");
            stopButton.Tooltip = MyStringId.GetOrCompute("Stops the Scaffold.");
            stopButton.Enabled = b =>
                                 {
                                     if (!b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner"))
                                         return false;

                                     ScaffoldItem yard = GetYard(b);

                                     return yard?.YardType == ScaffoldType.Weld || yard?.YardType == ScaffoldType.Grind;
                                 };
            stopButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            stopButton.SupportsMultipleBlocks = true;
            stopButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Disabled);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(stopButton);
            Controls.Add(stopButton);

            IMyTerminalControlCombobox buildPattern = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyCollector>("Scaffold_BuildPattern");
            buildPattern.Title = MyStringId.GetOrCompute("Build Pattern");
            buildPattern.Tooltip= MyStringId.GetOrCompute("Pattern used to build projections.");
            buildPattern.ComboBoxContent = FillPatternCombo;
            buildPattern.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            buildPattern.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b)?.YardType == ScaffoldType.Disabled;
            buildPattern.Getter = GetBuildPattern;
            buildPattern.Setter = SetBuildPattern;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(buildPattern);
            Controls.Add(buildPattern);

            IMyTerminalControlSlider beamCountSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Scaffold_BeamCount");
            beamCountSlider.Title = MyStringId.GetOrCompute("Beam Count");

            beamCountSlider.Tooltip = MyStringId.GetOrCompute("Number of beams this Scaffold can use per corner.");
            beamCountSlider.SetLimits(1, 3);
            beamCountSlider.Writer = (b, result) => result.Append(GetBeamCount(b));
            beamCountSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            beamCountSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null;
            beamCountSlider.Getter = b => GetBeamCount(b);
            beamCountSlider.Setter = (b, v) =>
                                     {
                                         SetBeamCount(b, (int)Math.Round(v, 0, MidpointRounding.ToEven));
                                         beamCountSlider.UpdateVisual();
                                     };
            beamCountSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(beamCountSlider);
            Controls.Add(beamCountSlider);

            IMyTerminalControlSlider grindSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Scaffold_GrindSpeed");
            grindSpeedSlider.Title = MyStringId.GetOrCompute("Grind Speed");

            grindSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this Scaffold grinds grids.");
            grindSpeedSlider.SetLimits(0.01f, 2);
            grindSpeedSlider.Writer = (b, result) => result.Append(GetGrindSpeed(b));
            grindSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            grindSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null;
            grindSpeedSlider.Getter = GetGrindSpeed;
            grindSpeedSlider.Setter = (b, v) =>
                                      {
                                          SetGrindSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                                          grindSpeedSlider.UpdateVisual();
                                      };
            grindSpeedSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindSpeedSlider);
            Controls.Add(grindSpeedSlider);

            IMyTerminalControlSlider weldSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Scaffold_WeldSpeed");
            weldSpeedSlider.Title = MyStringId.GetOrCompute("Weld Speed");

            weldSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this Scaffold welds grids.");
            weldSpeedSlider.SetLimits(0.01f, 2);
            weldSpeedSlider.Writer = (b, result) => result.Append(GetWeldSpeed(b));
            weldSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            weldSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null;
            weldSpeedSlider.Getter = GetWeldSpeed;
            weldSpeedSlider.Setter = (b, v) =>
                                     {
                                         SetWeldSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                                         weldSpeedSlider.UpdateVisual();
                                     };
            weldSpeedSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldSpeedSlider);
            Controls.Add(weldSpeedSlider);

            IMyTerminalAction grindAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Scaffold_GrindAction");
            grindAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            grindAction.Name = new StringBuilder("Grind");
            grindAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            grindAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Grind);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(grindAction);

            IMyTerminalAction weldAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Scaffold_WeldAction");
            weldAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            weldAction.Name = new StringBuilder("Weld");
            weldAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            weldAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Weld);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(weldAction);

            IMyTerminalAction stopAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Scaffold_StopAction");
            stopAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            stopAction.Name = new StringBuilder("Stop");
            stopAction.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            stopAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Disabled);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(stopAction);
        }

        private void AppendingCustomInfo(IMyTerminalBlock b, StringBuilder arg2)
        {
            try
            {
                float power = _power;
                float maxpower = _maxpower;
                if (GetYard(b) != null)
                {
                    maxpower *= Math.Max(b.GetValueFloat("Scaffold_GrindSpeed"), b.GetValueFloat("Scaffold_WeldSpeed"));
                    maxpower *= GetBeamCount(b);
                }
                var sb = new StringBuilder();
                sb.Append("Required Input: ");
                MyValueFormatter.AppendWorkInBestUnit(power, sb);
                sb.AppendLine();
                sb.Append("Max required input: ");
                MyValueFormatter.AppendWorkInBestUnit(maxpower, sb);
                sb.AppendLine();
                sb.Append(_info);
                sb.AppendLine();

                arg2.Append(sb);
            }
            catch (Exception)
            {
                //don't really care, just don't crash
            }
        }

        private int GetBeamCount(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 3;

            return ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).BeamCount;
        }

        private void SetBeamCount(IMyCubeBlock b, int value)
        {
            if (GetYard(b) == null)
                return;

            //this value check stops infinite loops of sending the setting to server and immediately getting the same value back
            if (value == GetBeamCount(b))
                return;

            YardSettingsStruct settings = ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.BeamCount = value;

            ScaffoldSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendScaffoldSettings(b.CubeGrid.EntityId, settings);
        }

        private bool GetGuideEnabled(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return true;

            return ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).GuideEnabled;
        }

        private void SetGuideEnabled(IMyCubeBlock b, bool value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetGuideEnabled(b))
                return;

            YardSettingsStruct settings = ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.GuideEnabled = value;

            ScaffoldSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendScaffoldSettings(b.CubeGrid.EntityId, settings);
        }

        private bool GetLockEnabled(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return false;

            return ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).AdvancedLocking;
        }

        private void SetLockEnabled(IMyCubeBlock b, bool value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetLockEnabled(b))
                return;
            
            YardSettingsStruct settings = ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.AdvancedLocking = value;

            ScaffoldSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendScaffoldSettings(b.CubeGrid.EntityId, settings);
        }

        private float GetGrindSpeed(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 0.1f;

            return ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).GrindMultiplier;
        }

        private void SetGrindSpeed(IMyCubeBlock b, float value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetGrindSpeed(b))
                return;

            YardSettingsStruct settings = ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.GrindMultiplier = value;

            ScaffoldSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendScaffoldSettings(b.CubeGrid.EntityId, settings);
        }

        private float GetWeldSpeed(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 0.1f;

            return ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).WeldMultiplier;
        }

        private void SetWeldSpeed(IMyCubeBlock b, float value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetWeldSpeed(b))
                return;
            
            YardSettingsStruct settings = ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.WeldMultiplier = value;

            ScaffoldSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendScaffoldSettings(b.CubeGrid.EntityId, settings);
        }

        private long GetBuildPattern(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 0;

            return (long)ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).BuildPattern;
        }

        private void SetBuildPattern(IMyCubeBlock b, long value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetBuildPattern(b))
                return;

            YardSettingsStruct settings = ScaffoldSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.BuildPattern = (BuildPatternEnum)value;

            ScaffoldSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendScaffoldSettings(b.CubeGrid.EntityId, settings);
        }

        private ScaffoldItem GetYard(IMyCubeBlock b)
        {
            return b.GameLogic.GetAs<ScaffoldCorner>()?.Scaffold;
        }

        public void SetPowerUse(float req)
        {
            _power = req;
        }

        public void SetMaxPower(float req)
        {
            _maxpower = req;
        }

        public void SetInfo(string info)
        {
            _info = info;
        }

        public void UpdateVisuals()
        {
            foreach (IMyTerminalControl control in Controls)
                control.UpdateVisual();
        }

        public override void UpdateBeforeSimulation()
        {
            if (!((IMyCollector)Container.Entity).Enabled)
                _power = 0f;
            _sink.SetMaxRequiredInputByType(PowerDef, _power);
            _sink.SetRequiredInputByType(PowerDef, _power);
            //sink.Update();
        }

        public override void UpdateBeforeSimulation10()
        {
            ((IMyTerminalBlock)Container.Entity).RefreshCustomInfo();
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
        }

        private static void FillPatternCombo(List<MyTerminalControlComboBoxItem> list)
        {
            var names = Enum.GetNames(typeof(BuildPatternEnum));
            for(int i = 0; i < names.Length; i++)
                list.Add(new MyTerminalControlComboBoxItem() {Key = i, Value = MyStringId.GetOrCompute(names[i])});
        }
    }
}