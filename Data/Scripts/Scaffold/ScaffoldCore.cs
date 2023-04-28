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

        //TODO
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
            MyAPIGateway.Utilities.ShowMissionScreen("Scaffold Update", "", "July 2017",
    @"Greetings engineers!

The Scaffold mod has received a major update, fixing many bugs and bringing some exciting new features!

I've fixed welding projections that are connected to the shiypard grid, fractional components, strange power use, graphical issues, various multithreading issues, and more.

Along with the projection fixes, there's a new option in the terminal which allows you to select a build pattern for projections. This is mostly a visual thing, but the results are interesting enough that it's worth having the option.

Power use has been rebalanced and reduced drastically. Component efficiency hasn't changed. As another balancing requirement, Scaffold corners now require a Targeting Computer component, which is built with Platinum.

Most notably I've added a mobile version of the shiypard. It works the same as the normal fixed version, but requires much more power, and has a much lower component efficiency.

Mobile Scaffolds include a slight tractor beam effect, helping your fighters match speed to the Scaffold so you can make repairs while under way, and also include an advanced locking feature.
This feature will lock grids inside mobile Scaffolds, but will consume a lot of power to maintain if you are doing hard maneuvers.

I hope you enjoy this update. Please visit the workshop and rate, subscribe, upvote, downvote, sing a jaunty tune, whatever it is that kids do these days.
You can open the workshop page by sending '/Scaffold workshop' in chat.

Happy engineering!

<3 Rexxar");
        }

        private void HandleMessageEntered(string messageText, ref bool sendToOthers)
        {
            string messageLower = messageText.ToLower();
            
            if (!messageLower.StartsWith("/Scaffold"))
                return;

            if (DateTime.Now - _lastMessageTime < TimeSpan.FromMilliseconds(200))
                return;

            if (messageLower.Equals("/Scaffold debug on"))
            {
                Logging.Instance.WriteLine("Debug turned on");
                Debug = true;
            }
            else if (messageLower.Equals("/Scaffold debug off"))
            {
                Logging.Instance.WriteLine("Debug turned off");
                Debug = false;
            }
            else if (messageLower.Equals("/Scaffold new"))
            {
                sendToOthers = false;
                ShowLog();
                return;
            }
            else if (messageLower.Equals("/Scaffold workshop"))
            {
                MyVisualScriptLogicProvider.OpenSteamOverlay(@"http://steamcommunity.com/sharedfiles/filedetails/?id=684618597");
                sendToOthers = false;
                return;
            }
            _lastMessageTime = DateTime.Now;

            sendToOthers = false;

            byte[] commandBytes = Encoding.UTF8.GetBytes(messageLower);
            byte[] idBytes = BitConverter.GetBytes(MyAPIGateway.Session.Player.SteamUserId);

            var message = new byte[commandBytes.Length + sizeof(ulong)];

            idBytes.CopyTo(message, 0);
            commandBytes.CopyTo(message, idBytes.Length);

            Communication.SendMessageToServer(Communication.MessageTypeEnum.ClientChat, message);
        }

        private void CalculateBoxesContaining()
        {
            foreach (ScaffoldItem item in ProcessLocalYards.LocalYards)
            {
                foreach (IMyCubeGrid grid in item.ContainsGrids)
                {
                    if (item.YardType != ScaffoldType.Disabled || grid.Closed || !ScaffoldSettings.Instance.GetYardSettings(item.EntityId).GuideEnabled)
                    {
                        BoxDict.Remove(grid.EntityId);
                        continue;
                    }
                    //if (BoxDict.ContainsKey(grid.EntityId) && Vector3D.DistanceSquared(BoxDict[grid.EntityId].LastPos, grid.GetPosition()) < 0.01)
                    //    continue;

                    uint color;

                    if (grid.Physics != null)
                        color = Color.Green.PackedValue;
                    else
                    {
                        var proj = grid.Projector();

                        if (proj == null) //ghost grid like Digi's helmet
                            continue;

                        if (proj.RemainingBlocks == 0) //projection is complete
                            continue;

                        color = Color.Cyan.PackedValue;
                    }

                    BoxDict[grid.EntityId] = new BoxItem
                                             {
                                                 Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                                                 GridId = grid.EntityId,
                                                 //PackedColor = grid.Physics == null ? Color.Cyan.PackedValue : Color.Green.PackedValue,
                                                 PackedColor = color,
                                                 LastPos = grid.GetPosition()
                                             };
                }
            }
        }

        private void CalculateBoxesIntersecting()
        {
            foreach (var item in ProcessLocalYards.LocalYards)
            {
                foreach (IMyCubeGrid grid in item.IntersectsGrids)
                {
                    if (item.YardType != ScaffoldType.Disabled || grid.Closed || !ScaffoldSettings.Instance.GetYardSettings(item.EntityId).GuideEnabled)
                    {
                        BoxDict.Remove(grid.EntityId);
                        continue;
                    }
                    //if (BoxDict.ContainsKey(grid.EntityId) && Vector3D.DistanceSquared(BoxDict[grid.EntityId].LastPos, grid.GetPosition()) < 0.01)
                    //    continue;

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

                    BoxDict[grid.EntityId] = new BoxItem
                                             {
                                                 Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                                                 GridId = grid.EntityId,
                                                 //PackedColor = grid.Physics == null ? Color.CornflowerBlue.PackedValue : Color.Yellow.PackedValue,
                                                 PackedColor = color,
                                                 LastPos = grid.GetPosition()
                                             };
                }
            }
        }

        private void CalculateLines()
        {
            foreach (var e in Communication.LineDict)
            {
                foreach (var line in e.Value)
                {
                    line.Start = MathUtility.CalculateEmitterOffset(line.EmitterBlock, line.Index);
                    var target = line.TargetGrid.GetCubeBlock(line.TargetBlock);
                    if (target == null || target.Closed())
                        continue;

                    line.End = target.GetPosition();

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
            if (MyAPIGateway.Session?.Player == null || !_initialized)
                return;

            try
            {
                //these tasks are too simple to use Parallel.ForEach or similar in the body, but
                //can all safely be run simultaneously, so do that.
                var t1 = MyAPIGateway.Parallel.Start(CalculateBoxesContaining);
                var t2 = MyAPIGateway.Parallel.Start(CalculateBoxesIntersecting);
                var t3 = MyAPIGateway.Parallel.Start(CalculateLines);
                //wait for all three to finish
                t1.Wait();
                t2.Wait();
                t3.Wait();
                DrawLines();
                FadeLines();
                DrawScanning();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"Draw(): {ex}");
                MyLog.Default.WriteLineAndConsole("##Scaffold MOD: ENCOUNTERED ERROR DURING DRAW UPDATE. CHECK MOD LOG");
                if (Debug)
                    throw;
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (MyAPIGateway.Session == null)
                    return;

                if (!_initialized)
                {
                    _initialized = true;
                    Initialize();
                }
                
                RunProcessHandlers();

                foreach (var item in ProcessScaffoldDetection.ScaffoldsList)
                {
                    if (item.StaticYard)
                    {
                        foreach (IMyCubeGrid yardGrid in item.YardGrids)
                            yardGrid.Stop();
                    }
                    else
                    {
                        item.UpdatePosition();
                        item.NudgeGrids();
                    }
                }

                foreach (var item in ProcessLocalYards.LocalYards)
                {
                    if (!item.StaticYard)
                        item.UpdatePosition();
                }

                if (_updateCount++ % 10 != 0)
                    return;

                CheckAndDamagePlayer();
                Utilities.ProcessActionQueue();

                if (Debug)
                    Profiler.Save();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"UpdateBeforeSimulation(): {ex}");
                MyLog.Default.WriteLineAndConsole("##Scaffold MOD: ENCOUNTERED ERROR DURING MOD UPDATE. CHECK MOD LOG");
                if (Debug)
                    throw;
            }
        }

        private void CheckAndDamagePlayer()
        {
            var character = MyAPIGateway.Session.Player?.Controller?.ControlledEntity?.Entity as IMyCharacter;

            if (character == null)
                return;

            var damageBlock = Profiler.Start("0.ScaffoldMod.ScaffoldCore", nameof(CheckAndDamagePlayer));
            BoundingBoxD charbox = character.WorldAABB;

            MyAPIGateway.Parallel.ForEach(Communication.LineDict.Values.ToArray(), lineList =>
                                                                                   {
                                                                                       foreach (LineItem line in lineList)
                                                                                       {
                                                                                           var ray = new Ray(line.Start, line.End - line.Start);
                                                                                           double? intersection = charbox.Intersects(ray);
                                                                                           if (intersection.HasValue)
                                                                                           {
                                                                                               if (Vector3D.DistanceSquared(charbox.Center, line.Start) < Vector3D.DistanceSquared(line.Start, line.End))
                                                                                               {
                                                                                                   Utilities.Invoke(() => character.DoDamage(5, MyStringHash.GetOrCompute("WeaponLaser"), true));
                                                                                               }
                                                                                           }
                                                                                       }
                                                                                   });
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
            var toRemove = new List<ScanAnimation>();
            foreach (ScanAnimation animation in Communication.ScanList)
            {
                if (!animation.Draw())
                    toRemove.Add(animation);
            }

            foreach (ScanAnimation removeAnim in toRemove)
                Communication.ScanList.Remove(removeAnim);
        }

        private void DrawLines()
        {
            foreach (KeyValuePair<long, List<LineItem>> kvp in Communication.LineDict)
            {
                foreach (LineItem line in kvp.Value)
                {
                    if (Communication.FadeList.Any(x => x.Start == line.Start))
                        continue;

                    if (line.Pulse)
                    {
                        PulseLines(line);
                        continue;
                    }

                    line.LinePackets?.DrawPackets();

                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ScaffoldLaser"), ref line.Color, 0.4f);
                }
            }

            foreach (KeyValuePair<long, BoxItem> entry in BoxDict)
            {
                BoxItem box = entry.Value;
                Vector4 color = new Color(box.PackedColor).ToVector4();
                foreach (LineItem line in box.Lines)
                {
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ScaffoldGizmo"), ref color, 1f);
                }
            }

            foreach (ScaffoldItem item in ProcessLocalYards.LocalYards)
            {
                Vector4 color = Color.White;
                if (item.YardType == ScaffoldType.Disabled || item.YardType == ScaffoldType.Invalid)
                    continue;

                foreach (LineItem line in item.BoxLines)
                {
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("WeaponLaserIgnoreDepth"), ref color, 1f);
                }
            }
        }

        private void PulseLines(LineItem item)
        {
            if (item.Descend)
                item.PulseVal -= 0.025;
            else
                item.PulseVal += 0.025;

            Vector4 drawColor = item.Color;
            drawColor.W = (float)((Math.Sin(item.PulseVal) + 1) / 2);
            if (drawColor.W <= 0.05)
                item.Descend = !item.Descend;
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
        
        protected override void UnloadData()
        {
            try
            {
                Utilities.SessionClosing = true;

                if (Utilities.AbortAllTasks())
                    Logging.Instance.WriteDebug("CAUGHT AND ABORTED TASK!!!!");

                RemoveMessageHandler();

                if (Logging.Instance != null)
                    Logging.Instance.Close();

                Communication.UnregisterHandlers();

                foreach (ScaffoldItem yard in ProcessScaffoldDetection.ScaffoldsList.ToArray())
                    yard.Disable(false);
            }
            catch
            {
                //ignore errors on session close
            }
        }
    }
}