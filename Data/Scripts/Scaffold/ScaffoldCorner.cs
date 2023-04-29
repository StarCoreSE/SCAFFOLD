using System;
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

        // Initializes the mod script
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            // Get reference to the collector block and its sink component
            _block = (IMyCollector)Container.Entity;
            _block.Components.TryGet(out _sink);

            // Set update flags to trigger certain events
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;

            // Set event handlers for when the block is closing and appending custom info
            _block.OnClosing += OnClosing;
            _block.AppendingCustomInfo += AppendingCustomInfo;
        }

        // Called when the block is closing
        private void OnClosing(IMyEntity obj)
        {
            // Remove event handlers and stop updating
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
            // If already initialized, return
            if (_init) return;

            _init = true; // Mark as initialized
            _block = Entity as IMyCollector;

            // If not a collector block, return
            if (_block == null) return;

            // Create a separator control for the terminal
            IMyTerminalControlSeparator sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyCollector>(string.Empty);
            sep.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner"); // Set visibility condition
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(sep); // Add control to the terminal

            // Create an on/off switch control for the guide boxes
            IMyTerminalControlOnOffSwitch guideSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>("Scaffold_GuideSwitch");
            guideSwitch.Title = MyStringId.GetOrCompute("Guide Boxes"); // Set title
            guideSwitch.Tooltip = MyStringId.GetOrCompute("Toggles the guide boxes drawn around grids in the Scaffold."); // Set tooltip
            guideSwitch.OnText = MyStringId.GetOrCompute("On"); // Set "On" text
            guideSwitch.OffText = MyStringId.GetOrCompute("Off"); // Set "Off" text
            guideSwitch.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner"); // Set visibility condition
            guideSwitch.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null; // Set enabled condition
            guideSwitch.SupportsMultipleBlocks = true; // Set support for multiple blocks
            guideSwitch.Getter = GetGuideEnabled; // Set getter method
            guideSwitch.Setter = SetGuideEnabled; // Set setter method
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(guideSwitch); // Add control to the terminal
            Controls.Add(guideSwitch); // Add control to the list of controls


            // Create an on/off switch control for the "Scaffold_LockSwitch" property of the "IMyCollector" type.
            var lockSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>("Scaffold_LockSwitch");
            // Set the title and tooltip of the switch.
            lockSwitch.Title = MyStringId.GetOrCompute("Advanced Locking");
            lockSwitch.Tooltip = MyStringId.GetOrCompute("Toggles locking grids in the Scaffold when grinding or welding while moving.");
            // Set the on and off text of the switch.
            lockSwitch.OnText = MyStringId.GetOrCompute("On");
            lockSwitch.OffText = MyStringId.GetOrCompute("Off");
            // Set the switch's visibility and enabled state based on the subtype ID of the block.
            lockSwitch.Visible = b => b.BlockDefinition.SubtypeId.Equals("ScaffoldCorner_Small");
            lockSwitch.Enabled = b => b.BlockDefinition.SubtypeId.Equals("ScaffoldCorner_Small") && GetYard(b) != null;
            // Set the switch to support multiple blocks.
            lockSwitch.SupportsMultipleBlocks = true;
            // Set the getter and setter functions for the switch.
            lockSwitch.Getter = GetLockEnabled;
            lockSwitch.Setter = SetLockEnabled;
            // Add the switch to the "IMyCollector" control list.
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(lockSwitch);
            Controls.Add(lockSwitch);

            // Create three buttons for a Scaffold block in Space Engineers.
            IMyTerminalControlButton grindButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Scaffold_GrindButton");
            IMyTerminalControlButton weldButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Scaffold_WeldButton");
            IMyTerminalControlButton stopButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Scaffold_StopButton");

            // Set the title, tooltip, enabled status, visibility, and action of the grindButton.
            grindButton.Title = MyStringId.GetOrCompute("Grind");
            grindButton.Tooltip = MyStringId.GetOrCompute("Begin grinding ships in the yard.");
            grindButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b)?.YardType == ScaffoldType.Disabled;
            grindButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            grindButton.SupportsMultipleBlocks = true;
            grindButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Grind);

            // Add the grindButton to the Terminal Controls of the Scaffold block and to the Controls list.
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindButton);
            Controls.Add(grindButton);

            // Set the title and tooltip for the "weldButton" control
            weldButton.Title = MyStringId.GetOrCompute("Weld");
            weldButton.Tooltip = MyStringId.GetOrCompute("Start welding ships in the yard.");

            // Set the conditions for the "weldButton" control to be enabled and visible
            weldButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b)?.YardType == ScaffoldType.Disabled;
            weldButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");

            // Enable support for multiple blocks and set the action for the "weldButton" control
            weldButton.SupportsMultipleBlocks = true;
            weldButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Weld);

            // Add the "weldButton" control to the terminal controls and the controls list
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldButton);
            Controls.Add(weldButton);

            // Set the title and tooltip for the "stopButton" control
            stopButton.Title = MyStringId.GetOrCompute("Stop");
            stopButton.Tooltip = MyStringId.GetOrCompute("Stop the Scaffold.");

            // Set the conditions for the "stopButton" control to be enabled and visible
            stopButton.Enabled = b => {
                if (!b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner"))
                    return false;
                ScaffoldItem yard = GetYard(b);
                return yard?.YardType == ScaffoldType.Weld || yard?.YardType == ScaffoldType.Grind;
            };
            stopButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");

            // Enable support for multiple blocks and set the action for the "stopButton" control
            stopButton.SupportsMultipleBlocks = true;
            stopButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Disabled);

            // Add the "stopButton" control to the terminal controls and the controls list
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(stopButton);
            Controls.Add(stopButton);

            // Create a combobox control for selecting the build pattern of a scaffold
            IMyTerminalControlCombobox buildPattern = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyCollector>("Scaffold_BuildPattern");

            // Set the title and tooltip of the combobox control
            buildPattern.Title = MyStringId.GetOrCompute("Build Pattern");
            buildPattern.Tooltip = MyStringId.GetOrCompute("Pattern used to build projections.");

            // Fill the combobox with the available patterns
            buildPattern.ComboBoxContent = FillPatternCombo;

            // Set the combobox to be visible only on scaffold corner blocks
            buildPattern.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");

            // Set the combobox to be enabled only on scaffold corner blocks that have a disabled yard
            buildPattern.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b)?.YardType == ScaffoldType.Disabled;

            // Set the getter and setter for the combobox control
            buildPattern.Getter = GetBuildPattern;
            buildPattern.Setter = SetBuildPattern;

            // Add the combobox control to the terminal controls for collectors
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(buildPattern);

            // Add the combobox control to the list of controls for the mod script
            Controls.Add(buildPattern);

            // Create a slider control for setting the number of beams for a scaffold
            IMyTerminalControlSlider beamCountSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Scaffold_BeamCount");

            // Set the title and tooltip of the slider control
            beamCountSlider.Title = MyStringId.GetOrCompute("Beam Count");
            beamCountSlider.Tooltip = MyStringId.GetOrCompute("Number of beams this Scaffold can use per corner.");

            // Set the limits of the slider control
            beamCountSlider.SetLimits(1, 3);

            // Set the writer for the slider control to display the current beam count
            beamCountSlider.Writer = (b, result) => result.Append(GetBeamCount(b));

            // Set the slider to be visible only on scaffold corner blocks
            beamCountSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");

            // Set the slider to be enabled only on scaffold corner blocks that have a yard
            beamCountSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null;

            // Set the getter and setter for the slider control
            beamCountSlider.Getter = b => GetBeamCount(b);
            beamCountSlider.Setter = (b, v) =>
            {
                SetBeamCount(b, (int)Math.Round(v, 0, MidpointRounding.ToEven));
                beamCountSlider.UpdateVisual();
            };

            // Set the slider to support multiple blocks
            beamCountSlider.SupportsMultipleBlocks = true;

            // Add the slider control to the terminal controls for collectors
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(beamCountSlider);

            // Add the slider control to the list of controls for the mod script
            Controls.Add(beamCountSlider);

            // Define a slider control for adjusting grind speed on a Scaffold block
            IMyTerminalControlSlider grindSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Scaffold_GrindSpeed");

            // Set the title of the slider control to "Grind Speed"
            grindSpeedSlider.Title = MyStringId.GetOrCompute("Grind Speed");

            // Set the tooltip of the slider control to "How fast this Scaffold grinds grids."
            grindSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this Scaffold grinds grids.");

            // Set the minimum and maximum limits of the slider control
            grindSpeedSlider.SetLimits(0.01f, 2);

            // Define a writer function that returns the current grind speed value
            grindSpeedSlider.Writer = (b, result) => result.Append(GetGrindSpeed(b));

            // Set the slider control to only be visible on ScaffoldCorner blocks
            grindSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");

            // Set the slider control to only be enabled on ScaffoldCorner blocks that have a valid yard
            grindSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null;

            // Define a getter function that returns the current grind speed value
            grindSpeedSlider.Getter = GetGrindSpeed;

            // Define a setter function that sets the grind speed value and updates the visual representation of the slider control
            grindSpeedSlider.Setter = (b, v) =>
            {
                SetGrindSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                grindSpeedSlider.UpdateVisual();
            };

            // Allow the slider control to be used on multiple blocks at once
            grindSpeedSlider.SupportsMultipleBlocks = true;

            // Add the slider control to the terminal controls for the Scaffold block
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindSpeedSlider);

            // Add the slider control to the Controls list for the modscript
            Controls.Add(grindSpeedSlider);

            // Creates a slider control for a Scaffold block's welding speed
            IMyTerminalControlSlider weldSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Scaffold_WeldSpeed");

            // Sets the title of the slider control
            weldSpeedSlider.Title = MyStringId.GetOrCompute("Weld Speed");

            // Sets the tooltip of the slider control
            weldSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this Scaffold welds grids.");

            // Sets the minimum and maximum values of the slider control
            weldSpeedSlider.SetLimits(0.01f, 2);

            // Sets the method to write the current value of the slider control
            weldSpeedSlider.Writer = (b, result) => result.Append(GetWeldSpeed(b));

            // Sets the condition for the slider control to be visible
            weldSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");

            // Sets the condition for the slider control to be enabled
            weldSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner") && GetYard(b) != null;

            // Sets the method to get the current value of the slider control
            weldSpeedSlider.Getter = GetWeldSpeed;

            // Sets the method to set the value of the slider control
            weldSpeedSlider.Setter = (b, v) =>
            {
                // Rounds the value to two decimal places
                SetWeldSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                // Updates the visual of the slider control
                weldSpeedSlider.UpdateVisual();
            };

            // Allows the slider control to work with multiple blocks
            weldSpeedSlider.SupportsMultipleBlocks = true;

            // Adds the slider control to the Scaffold block's terminal controls
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldSpeedSlider);

            // Adds the slider control to the list of controls
            Controls.Add(weldSpeedSlider);


            // Create grind action for ScaffoldCorner blocks in Space Engineers
            IMyTerminalAction grindAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Scaffold_GrindAction");
            // Enable the grind action for blocks with subtype ScaffoldCorner
            grindAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            // Set the name and icon of the grind action
            grindAction.Name = new StringBuilder("Grind");
            grindAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            // Set the action to send a yard command to grind the block's grid entity
            grindAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Grind);
            // Add the grind action to the terminal controls for ScaffoldCorner blocks
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(grindAction);

            // Create a new terminal action for welding
            IMyTerminalAction weldAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Scaffold_WeldAction");
            // Enable the action only for blocks that have the subtype "ScaffoldCorner"
            weldAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            // Set the name of the action to "Weld"
            weldAction.Name = new StringBuilder("Weld");
            // Set the icon of the action to "Start.dds"
            weldAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            // Set the action to call the "SendYardCommand" method with the grid entity ID and "ScaffoldType.Weld" as parameters
            weldAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Weld);
            // Add the weld action to the collector block's terminal controls
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(weldAction);

            // Create a terminal action for stopping the scaffold.
            IMyTerminalAction stopAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Scaffold_StopAction");
            // Enable the stop action only for ScaffoldCorner block subtype.
            stopAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ScaffoldCorner");
            // Set the name of the stop action to "Stop".
            stopAction.Name = new StringBuilder("Stop");
            // Set the icon of the stop action to the reset icon.
            stopAction.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            // Set the action of the stop action to send a yard command to disable the scaffold.
            stopAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ScaffoldType.Disabled);
            // Add the stop action to the terminal controls of the collector block type.
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(stopAction);
        }

        private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder output)
        {
            try
            {
                // Get current and maximum power values
                float power = _power;
                float maxpower = _maxpower;

                // Check if the block is part of a scaffold
                if (GetYard(block) != null)
                {
                    // Multiply the maximum power by scaffold grind speed, scaffold weld speed, and beam count
                    maxpower *= Math.Max(block.GetValueFloat("Scaffold_GrindSpeed"), block.GetValueFloat("Scaffold_WeldSpeed"));
                    maxpower *= GetBeamCount(block);
                }

                // Create a new StringBuilder object
                var sb = new StringBuilder();

                // Append required input label and value
                sb.Append("Required Input: ");
                MyValueFormatter.AppendWorkInBestUnit(power, sb);
                sb.AppendLine();

                // Append maximum required input label and value
                sb.Append("Max required input: ");
                MyValueFormatter.AppendWorkInBestUnit(maxpower, sb);
                sb.AppendLine();

                // Append additional information
                sb.Append(_info);
                sb.AppendLine();

                // Append the StringBuilder object to the output StringBuilder
                output.Append(sb);
            }
            catch (Exception)
            {
                // If an exception is caught, do nothing
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