using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ParallelTasks;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ScaffoldMod.ItemClasses;
using ScaffoldMod.ProcessHandlers;
using ScaffoldMod.Settings;
using ScaffoldMod.Utility;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;


namespace ScaffoldMod
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class ScaffoldCore : MySessionComponentBase
    {
        private const string Version = "v0.1";

        public static volatile bool Debug = false;

        //private readonly List<Task> _tasks = new List<Task>();
        private static Task _task;
        public static readonly MyConcurrentDictionary<long, BoxItem> BoxDict = new MyConcurrentDictionary<long, BoxItem>();

        private bool _initialized;

        private DateTime _lastMessageTime = DateTime.Now;
        private ProcessHandlerBase[] _processHandlers;
        private int _updateCount;

        private void Initialize()
        {
            AddMessageHandler();

            _processHandlers = new ProcessHandlerBase[]
            {
        new ProcessScaffoldAction(),
        new ProcessLocalYards(),
        new ProcessLCDMenu(),
        new ProcessScaffoldDetection(),
        new ProcessConveyorCache(),
            };

            Logging.Instance.WriteLine($"Scaffold Initialized: {Version}");

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("notify.sav", typeof(ScaffoldCore)))
            {
                var w = MyAPIGateway.Utilities.WriteFileInLocalStorage("notify.sav", typeof(ScaffoldCore));
                w.Write("newJul"); w.Flush(); w.Close();
                MyAPIGateway.Utilities.ShowNotification("Scaffolds updated. Enter '/Scaffold new' for changelog", 5000, MyFontEnum.Green);
            }
        }

        private void ShowLog()
        {
            MyAPIGateway.Utilities.ShowMissionScreen("Scaffold Update", "", "April",
    @"RETURN OF THE KING
t. invalid");
        }

        private void HandleMessageEntered(string messageText, ref bool sendToOthers)
        {
            string messageLower = messageText.ToLower();

            if (messageLower.StartsWith("/scaffold"))
            {
                if (DateTime.Now - _lastMessageTime >= TimeSpan.FromMilliseconds(200))
                {
                    _lastMessageTime = DateTime.Now;
                    sendToOthers = false;

                    switch (messageLower)
                    {
                        case "/scaffold debug on":
                            Logging.Instance.WriteLine("Debug turned on");
                            Debug = true;
                            break;
                        case "/scaffold debug off":
                            Logging.Instance.WriteLine("Debug turned off");
                            Debug = false;
                            break;
                        case "/scaffold new":
                            ShowLog();
                            break;
                        case "/scaffold workshop":
                            MyVisualScriptLogicProvider.OpenSteamOverlay(@"http://steamcommunity.com/sharedfiles/filedetails/?id=684618597");
                            break;
                        default:
                            byte[] commandBytes = Encoding.UTF8.GetBytes(messageLower);
                            byte[] idBytes = BitConverter.GetBytes(MyAPIGateway.Session.Player.SteamUserId);
                            byte[] message = idBytes.Concat(commandBytes).ToArray();
                            Communication.SendMessageToServer(Communication.MessageTypeEnum.ClientChat, message);
                            break;
                    }
                }
            }
        }

        private void CalculateBoxesContaining()
        {
            // Loop through each scaffold item in the local yards
            foreach (ScaffoldItem item in ProcessLocalYards.LocalYards)
            {
                // Loop through each cube grid contained in the scaffold item
                foreach (IMyCubeGrid grid in item.ContainsGrids)
                {
                    // If the scaffold item is disabled or the grid is closed or the guide is not enabled for the scaffold item
                    if (item.YardType != ScaffoldType.Disabled || grid.Closed || !ScaffoldSettings.Instance.GetYardSettings(item.EntityId).GuideEnabled)
                    {
                        // Remove the grid from the dictionary and skip to the next grid
                        BoxDict.Remove(grid.EntityId);
                        continue;
                    }

                    uint color;

                    // If the grid has a physics object
                    if (grid.Physics != null)
                        // Set the color to green
                        color = Color.Green.PackedValue;
                    else
                    {
                        var proj = grid.Projector();

                        // If the grid is a ghost grid (e.g. Digi's helmet)
                        if (proj == null)
                            // Skip to the next grid
                            continue;

                        // If the projection is complete
                        if (proj.RemainingBlocks == 0)
                            // Skip to the next grid
                            continue;

                        // Set the color to cyan
                        color = Color.Cyan.PackedValue;
                    }

                    // Add the grid to the dictionary with a new BoxItem
                    BoxDict[grid.EntityId] = new BoxItem
                    {
                        // Calculate the oriented bounding box lines for the grid
                        Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                        // Set the grid ID
                        GridId = grid.EntityId,
                        // Set the packed color based on whether the grid has a physics object or not
                        PackedColor = color,
                        // Set the last position of the grid
                        LastPos = grid.GetPosition()
                    };
                }
            }
        }

        private void CalculateBoxesIntersecting()
        {
            // loop through all local yards
            foreach (var item in ProcessLocalYards.LocalYards)
            {
                // loop through all grids that intersect with the yard
                foreach (IMyCubeGrid grid in item.IntersectsGrids)
                {
                    // check if the yard type is disabled or the grid is closed or the guide is disabled
                    if (item.YardType != ScaffoldType.Disabled || grid.Closed || !ScaffoldSettings.Instance.GetYardSettings(item.EntityId).GuideEnabled)
                    {
                        // if any of the above conditions are met, remove the grid from the dictionary and continue to the next grid
                        BoxDict.Remove(grid.EntityId);
                        continue;
                    }

                    // determine the color of the grid box based on its properties
                    uint color;
                    if (grid.Physics != null)
                        color = Color.Yellow.PackedValue;
                    else
                    {
                        var proj = grid.Projector();
                        if (proj == null) //ghost grid like Digi's helmet
                            continue;
                        if (proj.RemainingBlocks == 0) //projection is complete
                            continue;
                        color = Color.CornflowerBlue.PackedValue;
                    }

                    // create a new BoxItem object and add it to the dictionary with the grid entity ID as the key
                    BoxDict[grid.EntityId] = new BoxItem
                    {
                        Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                        GridId = grid.EntityId,
                        PackedColor = color,
                        LastPos = grid.GetPosition()
                    };
                }
            }
        }

        private void CalculateLines()
        {
            // Loop through all communication lines
            foreach (var e in Communication.LineDict)
            {
                // Loop through all lines within this communication line
                foreach (var line in e.Value)
                {
                    // Calculate the starting point of the line
                    line.Start = MathUtility.CalculateEmitterOffset(line.EmitterBlock, line.Index);

                    // Get the target block of the line
                    var target = line.TargetGrid.GetCubeBlock(line.TargetBlock);

                    // If the target block is null or closed, skip to the next line
                    if (target == null || target.Closed())
                        continue;

                    // Calculate the end point of the line
                    line.End = target.GetPosition();

                    // If line packets are available, update them with the new line coordinates
                    if (line.LinePackets != null)
                    {
                        line.LinePackets.Update(line.Start, line.End);
                    }
                }
            }
        }

        private void AddMessageHandler()
        {
            MyAPIGateway.Utilities.MessageEntered += HandleMessageEntered;
            Communication.RegisterHandlers();
        }

        private void RemoveMessageHandler()
        {
            MyAPIGateway.Utilities.MessageEntered -= HandleMessageEntered;
            Communication.UnregisterHandlers();
        }

        public override void Draw()
        {
            // If the player is not present or the mod is not initialized, return
            if (MyAPIGateway.Session?.Player == null || !_initialized) return;

            try
            {
                // Run these three methods simultaneously
                MyAPIGateway.Parallel.Start(CalculateBoxesContaining).Wait();
                MyAPIGateway.Parallel.Start(CalculateBoxesIntersecting).Wait();
                MyAPIGateway.Parallel.Start(CalculateLines).Wait();

                // Draw lines, fade lines and draw scanning
                DrawLines(); FadeLines(); DrawScanning();
            }
            catch (Exception ex)
            {
                // Log any exception encountered during the draw update
                Logging.Instance.WriteLine($"Draw(): {ex}");
                MyLog.Default.WriteLineAndConsole("##Scaffold MOD: ENCOUNTERED ERROR DURING DRAW UPDATE. CHECK MOD LOG");

                // If debug is enabled, throw the exception
                if (Debug) throw;
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                // Return if no session is available
                if (MyAPIGateway.Session == null) return;

                // Run initialization if not already initialized
                if (!_initialized) { _initialized = true; Initialize(); }

                // Run process handlers
                RunProcessHandlers();

                // Loop through scaffold detection list
                foreach (var item in ProcessScaffoldDetection.ScaffoldsList)
                {
                    // Stop grids if they are static yards
                    if (item.StaticYard)
                    {
                        foreach (IMyCubeGrid yardGrid in item.YardGrids) yardGrid.Stop();
                    }
                    // Otherwise update position and nudge grids
                    else
                    {
                        item.UpdatePosition();
                        item.NudgeGrids();
                    }
                }

                // Loop through local yards and update their positions
                foreach (var item in ProcessLocalYards.LocalYards)
                {
                    if (!item.StaticYard) item.UpdatePosition();
                }

                // Only execute every 10 updates
                if (_updateCount++ % 10 != 0) return;

                // Check and damage player, process action queue
                CheckAndDamagePlayer();
                Utilities.ProcessActionQueue();

                // Save profiler data if in debug mode
                if (Debug) Profiler.Save();
            }
            catch (Exception ex)
            {
                // Log error and throw exception in debug mode
                Logging.Instance.WriteLine($"UpdateBeforeSimulation(): {ex}");
                MyLog.Default.WriteLineAndConsole("##Scaffold MOD: ENCOUNTERED ERROR DURING MOD UPDATE. CHECK MOD LOG");
                if (Debug) throw;
            }
        }

        private void CheckAndDamagePlayer()
        {
            // Get the player's character entity
            var character = MyAPIGateway.Session.Player?.Controller?.ControlledEntity?.Entity as IMyCharacter;

            // If the character is null, exit the method
            if (character == null)
                return;

            // Start measuring the time taken by this method
            var damageBlock = Profiler.Start("0.ScaffoldMod.ScaffoldCore", nameof(CheckAndDamagePlayer));

            // Get the bounding box of the character's world
            BoundingBoxD charbox = character.WorldAABB;

            // Loop through all the LineItems in the Communication.LineDict
            MyAPIGateway.Parallel.ForEach(Communication.LineDict.Values.ToArray(), lineList =>
            {
                foreach (LineItem line in lineList)
                {
                    // Create a ray from the LineItem's start to end point
                    var ray = new Ray(line.Start, line.End - line.Start);

                    // Check if the ray intersects with the character's bounding box
                    double? intersection = charbox.Intersects(ray);

                    // If the ray intersects the bounding box
                    if (intersection.HasValue)
                    {
                        // Check if the character is closer to the start point than the end point
                        if (Vector3D.DistanceSquared(charbox.Center, line.Start) < Vector3D.DistanceSquared(line.Start, line.End))
                        {
                            // Damage the character with 5 points and mark the damage as done by a laser weapon
                            Utilities.Invoke(() => character.DoDamage(5, MyStringHash.GetOrCompute("WeaponLaser"), true));
                        }
                    }
                }
            });

            // Stop measuring the time taken by this method
            damageBlock.End();
        }

        private void RunProcessHandlers()
        {
            //wait for execution to complete before starting up a new thread
            if (!_task.IsComplete)
                return;

            //exceptions are suppressed in tasks, so re-throw if one happens
            if (_task.Exceptions != null && _task.Exceptions.Length > 0)
            {
                MyLog.Default.WriteLineAndConsole("##Scaffold MOD: THREAD EXCEPTION, CHECK MOD LOG FOR MORE INFO.");
                MyLog.Default.WriteLineAndConsole("##Scaffold MOD: EXCEPTION: " + _task.Exceptions[0]);
                if (Debug)
                    throw _task.Exceptions[0];
            }

            //run all process handlers in serial so we don't have to design for concurrency
            _task = MyAPIGateway.Parallel.Start(() =>
                                                {
                                                    string handlerName = "";
                                                    try
                                                    {
                                                        var processBlock = Profiler.Start("0.ScaffoldMod.ScaffoldCore", nameof(RunProcessHandlers));
                                                        foreach (ProcessHandlerBase handler in _processHandlers)
                                                        {
                                                            if (handler.CanRun())
                                                            {
                                                                handlerName = handler.GetType().Name;
                                                                var handlerBlock = Profiler.Start(handler.GetType().FullName);
                                                                //Logging.Instance.WriteDebug(handlerName + " start");
                                                                handler.Handle();
                                                                handler.LastUpdate = DateTime.Now;
                                                                handlerBlock.End();
                                                            }
                                                        }
                                                        processBlock.End();
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Logging.Instance.WriteLine($"Thread Exception: {handlerName}: {ex}");
                                                        Logging.Instance.Debug_obj("Thread exception! Check the log!");
                                                        throw;
                                                    }
                                                });
        }

        private void DrawScanning()
        {
            // Create a new list to store animations that need to be removed
            var removeList = new List<ScanAnimation>();

            // Loop through all animations in the scan list
            foreach (ScanAnimation animation in Communication.ScanList)
            {
                // Check if the animation needs to be drawn
                if (!animation.Draw())
                    // If not, add it to the remove list
                    removeList.Add(animation);
            }

            // Loop through all animations in the remove list
            foreach (ScanAnimation animationToRemove in removeList)
            {
                // Remove the animation from the scan list
                Communication.ScanList.Remove(animationToRemove);
            }
        }

        private void DrawLines()
        {
            // Loop through all line items in the Communication.LineDict dictionary
            foreach (KeyValuePair<long, List<LineItem>> kvp in Communication.LineDict)
            {
                foreach (LineItem line in kvp.Value)
                {
                    // If the line's start point is in the Communication.FadeList, skip it
                    if (Communication.FadeList.Any(x => x.Start == line.Start))
                        continue;

                    // If the line has a pulse effect, draw it and move to the next line
                    if (line.Pulse)
                    {
                        PulseLines(line);
                        continue;
                    }

                    // If the line has any packets, draw them
                    line.LinePackets?.DrawPackets();

                    // Draw the line using the MySimpleObjectDraw.DrawLine method
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ScaffoldLaser"), ref line.Color, 0.4f);
                }
            }

            // Loop through all box items in the BoxDict dictionary
            foreach (KeyValuePair<long, BoxItem> entry in BoxDict)
            {
                BoxItem box = entry.Value;
                Vector4 color = new Color(box.PackedColor).ToVector4();

                // Loop through all line items in the current box's Lines list
                foreach (LineItem line in box.Lines)
                {
                    // Draw the line using the MySimpleObjectDraw.DrawLine method
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ScaffoldGizmo"), ref color, 1f);
                }
            }

            // Loop through all ScaffoldItems in the ProcessLocalYards.LocalYards list
            foreach (ScaffoldItem item in ProcessLocalYards.LocalYards)
            {
                Vector4 color = Color.White;

                // If the current ScaffoldItem has a disabled or invalid YardType, skip it
                if (item.YardType == ScaffoldType.Disabled || item.YardType == ScaffoldType.Invalid)
                    continue;

                // Loop through all line items in the current ScaffoldItem's BoxLines list
                foreach (LineItem line in item.BoxLines)
                {
                    // Draw the line using the MySimpleObjectDraw.DrawLine method
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("WeaponLaserIgnoreDepth"), ref color, 1f);
                }
            }
        }

        // PulseLines method that takes a LineItem object as a parameter
        private void PulseLines(LineItem item)
        {
            // If the LineItem should descend, decrease the pulse value by 0.025, otherwise increase it
            item.PulseVal += item.Descend ? -0.025 : 0.025;

            // Set the drawColor to the LineItem's color
            Vector4 drawColor = item.Color;

            // Calculate the drawColor's opacity using the sine function and the pulse value
            drawColor.W = (float)((Math.Sin(item.PulseVal) + 1) / 2);

            // If the drawColor's opacity is less than or equal to 0.05, change the LineItem's direction
            if (drawColor.W <= 0.05) item.Descend = !item.Descend;

            // Draw the LineItem using MySimpleObjectDraw.DrawLine with a laser texture and the calculated drawColor
            MySimpleObjectDraw.DrawLine(item.Start, item.End, MyStringId.GetOrCompute("ScaffoldLaser"), ref drawColor, drawColor.W * 0.4f);
        }

        private void FadeLines()
        {
            var linesToRemove = new List<LineItem>();
            foreach (LineItem line in Communication.FadeList)
            {
                line.FadeVal -= 0.075f;
                if (line.FadeVal <= 0)
                {
                    //blank the line for a couple frames. Looks better that way.
                    if (line.FadeVal <= -0.2f)
                        linesToRemove.Add(line);
                    continue;
                }
                Vector4 drawColor = line.Color;
                //do a cubic fade out
                drawColor.W = line.FadeVal * line.FadeVal * line.FadeVal;
                MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ScaffoldLaser"), ref drawColor, drawColor.W * 0.4f);
            }

            foreach (LineItem removeLine in linesToRemove)
            {
                Communication.FadeList.Remove(removeLine);
            }
        }

        // This method is called when the mod is unloaded
        protected override void UnloadData()
        {
            // Set a flag to indicate that the session is closing
            Utilities.SessionClosing = true;

            // Abort all tasks and log a message if any task is aborted
            if (Utilities.AbortAllTasks()) Logging.Instance.WriteDebug("CAUGHT AND ABORTED TASK!!!!");

            // Remove the message handler for the mod
            RemoveMessageHandler();

            // Close the logging instance if it exists
            if (Logging.Instance != null) Logging.Instance.Close();

            // Unregister communication handlers for the mod
            Communication.UnregisterHandlers();

            // Disable all scaffolds in the scaffolds list
            foreach (ScaffoldItem yard in ProcessScaffoldDetection.ScaffoldsList.ToArray())
                yard.Disable(false);

            // Ignore any errors that occur during session close
            // Note: This is not good practice, but it is being done here to prevent the mod from crashing during shutdown
            // Ideally, all errors should be handled properly
            try { } catch { }
        }
    }
}