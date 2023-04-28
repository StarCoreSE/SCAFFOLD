using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using ScaffoldMod.ItemClasses;
using ScaffoldMod.Utility;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace ScaffoldMod.ProcessHandlers
{
    public class ProcessLCDMenu : ProcessHandlerBase
    {
        private static readonly string FullName = typeof(ProcessLCDMenu).FullName;
        private readonly Dictionary<ScaffoldItem, StatInfo> _stats = new Dictionary<ScaffoldItem, StatInfo>();

        private int _updateCount;

        public override int GetUpdateResolution()
        {
            return 200;
        }

        public override bool ServerOnly()
        {
            return true;
        }

        public override bool ClientOnly()
        {
            return false;
        }

        public override void Handle()
        {
            foreach (ScaffoldItem item in ProcessScaffoldDetection.ScaffoldsList)
            {
                if (item.Menu != null)
                {
                    if (item.Menu.Panel.Closed)
                        item.Menu = null;
                }

                if (item.Menu != null)
                    Utilities.Invoke(() => item.Menu.UpdateLCD());
                else
                {
                    if (_updateCount < 5)
                    {
                        _updateCount++;
                        return;
                    }
                    _updateCount = 0;

                    var yardGrid = (IMyCubeGrid)item.YardEntity;

                    var blocks = new List<IMySlimBlock>();
                    yardGrid.GetBlocks(blocks);
                    bool superFound = false;

                    foreach (IMySlimBlock slimBlock in blocks)
                    {
                        var panel = slimBlock.FatBlock as IMyTextPanel;

                        if (panel == null)
                            continue;

                        if (!panel.CustomName.ToLower().Contains("Scaffold"))
                            continue;

                        var bound = new BoundingSphereD(panel.GetPosition(), 5);
                        List<IMySlimBlock> nearblocks = yardGrid.GetBlocksInsideSphere(ref bound);
                        bool found = false;

                        foreach (IMySlimBlock block in nearblocks)
                        {
                            var buttons = block.FatBlock as IMyButtonPanel;

                            if (buttons == null)
                                continue;

                            superFound = true;
                            found = true;
                            Logging.Instance.WriteDebug("Found LCD pair for grid: " + item.EntityId);
                            Utilities.Invoke(() =>
                                             {
                                                 long id = item.EntityId;
                                                 item.Menu = new LCDMenu();
                                                 panel.Enabled = true;
                                                 //panel.RequestEnable(false);
                                                 //panel.RequestEnable(true);
                                                 item.Menu.BindButtonPanel(buttons);
                                                 item.Menu.BindLCD(panel);
                                                 var mainMenu = new MenuItem("", "", null, MenuDel(id));
                                                 var statusMenu = new MenuItem("", "", null, StatusDel(id));
                                                 var scanMenu = new MenuItem("", "Get details for: \r\n");
                                                 var grindStatsMenu = new MenuItem("SCANNING...", "", null, GrindStatsDel(id));
                                                 var weldStatsMenu = new MenuItem("SCANNING...", "", null, WeldStatsDel(id));
                                                 var returnMenu = new MenuItem("Return", "", ScanDel(item.Menu, mainMenu, 0));
                                                 mainMenu.root = item.Menu;
                                                 mainMenu.Add(new MenuItem("Grind", "", GrindDel(item.Menu, statusMenu)));
                                                 mainMenu.Add(new MenuItem("Weld", "", WeldDel(item.Menu, statusMenu)));
                                                 mainMenu.Add(new MenuItem("Scan", "", ScanDel(item.Menu, scanMenu)));
                                                 statusMenu.root = item.Menu;
                                                 statusMenu.Add(new MenuItem("Stop", "", StopDel(item.Menu, mainMenu)));
                                                 scanMenu.Add(new MenuItem("Grind", "", ScanDel(item.Menu, grindStatsMenu, ScaffoldType.Scanning)));
                                                 scanMenu.Add(new MenuItem("Weld", "", ScanDel(item.Menu, weldStatsMenu, ScaffoldType.Scanning)));
                                                 grindStatsMenu.Add(returnMenu);
                                                 weldStatsMenu.Add(returnMenu);
                                                 item.Menu.Root = mainMenu;
                                                 item.Menu.SetCurrentItem(mainMenu);
                                                 item.Menu.UpdateLCD();
                                                 Communication.SendNewYard(item);
                                             });

                            break;
                        }

                        if (found)
                            break;
                    }
                    Logging.Instance.WriteDebug($"SuperFind {item.EntityId}: {superFound}");
                }
            }
        }

        public string FormatStatus(ScaffoldItem item)
        {
            bool welding = false;
            var result = new StringBuilder();

            result.Append("Scaffold Status:\r\n");
            switch (item.YardType)
            {
                case ScaffoldType.Disabled:
                    result.Append("IDLE");
                    break;

                case ScaffoldType.Grind:
                    result.Append("GRINDING");
                    break;

                case ScaffoldType.Weld:
                    result.Append("WELDING");
                    welding = true;
                    break;

                default:
                    result.Append("ERROR");
                    return result.ToString();
            }

            result.Append("\r\n\r\n");

            if (welding && item.MissingComponentsDict.Count > 0)
            {
                result.Append("Missing Components:\r\n");
                foreach (KeyValuePair<string, int> component in item.MissingComponentsDict)
                {
                    result.Append($"{component.Key}: {component.Value}\r\n");
                }
                result.Append("\r\n");
            }

            float time = 0f;

            if (item.YardType == ScaffoldType.Grind)
            {
                foreach (BlockTarget target in item.TargetBlocks)
                {
                    if (target.Projector != null)
                        continue;
                    time += target.Block.Integrity / ((MyCubeBlockDefinition)target.Block.BlockDefinition).IntegrityPointsPerSec;
                }
                time /= item.Settings.GrindMultiplier;
                time = time / (item.Settings.BeamCount * 8);
            }
            else //welding
            {
                foreach (BlockTarget target in item.TargetBlocks)
                {
                    if (target.Projector != null)
                        time += target.BuildTime;
                    else
                        time += (target.Block.MaxIntegrity - target.Block.Integrity) / ((MyCubeBlockDefinition)target.Block.BlockDefinition).IntegrityPointsPerSec;
                }
                time /= item.Settings.WeldMultiplier;
                time = time / (item.Settings.BeamCount * 8);
            }

            int active = item.BlocksToProcess.Sum(entry => entry.Value.Count(target => target != null));

            result.Append("Targets: " + item.YardGrids.Count);
            result.Append("\r\n");
            result.Append($"Active beams: {active}/{item.Settings.BeamCount * 8}");
            result.Append("\r\n");
            result.Append("Blocks remaining: " + item.TargetBlocks.Count);
            result.Append("\r\n");
            result.Append("Estimated time remaining: " + TimeSpan.FromSeconds((int)time).ToString("g"));
            result.Append("\r\n\r\n");
            return result.ToString();
        }

        private string FormatGrindStats(ScaffoldItem item)
        {
            if (!_stats.ContainsKey(item))
                _stats.Add(item, new StatInfo());

            StatInfo stats = _stats[item];
            if (stats.StartTime == 0)
            {
                stats.StartTime = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
                //calculating stats can take tens or hundreds of ms, so drop it in a thread and check back when the scan animation is finished.
                MyAPIGateway.Parallel.StartBackground(() =>
                                                      {
                                                          try
                                                          {
                                                              var statBlock = Profiler.Start(FullName, nameof(FormatGrindStats));
                                                              stats.TotalComponents = new Dictionary<string, int>();
                                                              var blockList = new List<IMySlimBlock>();
                                                              var processGrids = new HashSet<IMyCubeGrid>();
                                                              processGrids.UnionWith(item.YardGrids);
                                                              processGrids.UnionWith(item.ContainsGrids);
                                                              foreach (IMyCubeGrid grid in processGrids)
                                                              {
                                                                  if (grid?.Physics == null || grid.Closed)
                                                                      continue;
                                                                  grid.GetBlocks(blockList);
                                                                  stats.BlockCount += blockList.Count;

                                                                  var missingComponents = new Dictionary<string, int>();
                                                                  foreach (IMySlimBlock block in blockList)
                                                                  {
                                                                      //var blockDef = (MyObjectBuilder_CubeBlockDefinition)block.BlockDefinition.GetObjectBuilder();
                                                                      //grindTime += Math.Max(blockDef.BuildTimeSeconds / ScaffoldSettings.Instance.GrindMultiplier, 0.5f);

                                                                      var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
                                                                      //IntegrityPointsPerSec = MaxIntegrity / BuildTimeSeconds
                                                                      //this is much, much faster than pulling the objectbuilder and getting the data from there.
                                                                      stats.GrindTime += Math.Max(block.BuildPercent() * (blockDef.MaxIntegrity / blockDef.IntegrityPointsPerSec) / MyAPIGateway.Session.GrinderSpeedMultiplier / item.Settings.GrindMultiplier, 0.5f);

                                                                      block.GetMissingComponents(missingComponents);

                                                                      foreach (MyCubeBlockDefinition.Component component in blockDef.Components)
                                                                      {
                                                                          if (stats.TotalComponents.ContainsKey(component.Definition.Id.SubtypeName))
                                                                              stats.TotalComponents[component.Definition.Id.SubtypeName] += component.Count;
                                                                          else
                                                                              stats.TotalComponents.Add(component.Definition.Id.SubtypeName, component.Count);
                                                                      }
                                                                  }

                                                                  foreach (KeyValuePair<string, int> missing in missingComponents)
                                                                  {
                                                                      if (stats.TotalComponents.ContainsKey(missing.Key))
                                                                          stats.TotalComponents[missing.Key] -= missingComponents[missing.Key];
                                                                  }

                                                                  blockList.Clear();
                                                              }

                                                              var result = new StringBuilder();
                                                              result.Append("Scan Results:\r\n\r\n");
                                                              result.Append("Targets: " + item.YardGrids.Count);
                                                              result.Append("\r\n");
                                                              result.Append("Block Count: " + stats.BlockCount);
                                                              result.Append("\r\n");
                                                              float grindTime = stats.GrindTime / (item.Settings.BeamCount * 8);
                                                              if (grindTime >= 7200)
                                                                  result.Append("Estimated Deconstruct Time: " + (grindTime / 3600).ToString("0.00") + " hours");
                                                              else if (grindTime >= 120)
                                                                  result.Append("Estimated Deconstruct Time: " + (grindTime / 60).ToString("0.00") + " min");
                                                              else
                                                                  result.Append("Estimated Deconstruct Time: " + grindTime.ToString("0.00") + "s");
                                                              result.Append("\r\n");
                                                              result.Append("Estimated Component Gain:\r\n\r\n");
                                                              double multiplier = Math.Max(item.ScaffoldBox.HalfExtent.LengthSquared() / 200000, 1);
                                                              foreach (KeyValuePair<string, int> component in stats.TotalComponents)
                                                              {
                                                                  if (component.Value != 0)
                                                                      result.Append($"{component.Key}: {component.Value / multiplier}\r\n");
                                                              }

                                                              stats.Output = result.ToString();
                                                              statBlock.End();
                                                          }
                                                          catch (Exception ex)
                                                          {
                                                              Logging.Instance.WriteLine(ex.ToString());
                                                          }
                                                      });
            }

            if (!string.IsNullOrEmpty(stats.Output) && MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - stats.StartTime > 6000)
                return stats.Output;
            return "SCANNING...\r\n\r\n";
        }

        private string FormatWeldStats(ScaffoldItem item)
        {
            if (!_stats.ContainsKey(item))
                _stats.Add(item, new StatInfo());

            StatInfo stats = _stats[item];
            if (stats.StartTime == 0)
            {
                stats.StartTime = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
                //calculating stats can take tens or hundreds of ms, so drop it in a thread and check back when the scan animation is finished.
                MyAPIGateway.Parallel.StartBackground(() =>
                                                      {
                                                          try
                                                          {
                                                              var statBlock = Profiler.Start(FullName, nameof(FormatWeldStats));
                                                              stats.TotalComponents = new Dictionary<string, int>();
                                                              var blockList = new List<IMySlimBlock>();
                                                              var processGrids = new HashSet<IMyCubeGrid>();
                                                              processGrids.UnionWith(item.YardGrids);
                                                              processGrids.UnionWith(item.ContainsGrids);

                                                              var projections = new HashSet<IMyCubeGrid>();

                                                              foreach (IMyCubeGrid grid in processGrids)
                                                              {
                                                                  if (grid.MarkedForClose || grid.Closed)
                                                                      continue;

                                                                  grid.GetBlocks(blockList);

                                                                  foreach (IMySlimBlock block in blockList)
                                                                  {
                                                                      var projector = block?.FatBlock as IMyProjector;
                                                                      if (projector == null)
                                                                          continue;
                                                                      if (projector.IsProjecting && projector.ProjectedGrid != null)
                                                                          projections.Add(projector.ProjectedGrid);
                                                                  }

                                                                  blockList.Clear();
                                                              }

                                                              processGrids.UnionWith(projections);

                                                              foreach (IMyCubeGrid grid in processGrids)
                                                              {
                                                                  if (grid.MarkedForClose || grid.Closed)
                                                                      continue;

                                                                  grid.GetBlocks(blockList);
                                                                  stats.BlockCount += blockList.Count;

                                                                  var missingComponents = new Dictionary<string, int>();
                                                                  foreach (IMySlimBlock block in blockList)
                                                                  {
                                                                      //var blockDef = (MyObjectBuilder_CubeBlockDefinition)block.BlockDefinition.GetObjectBuilder();
                                                                      //grindTime += Math.Max(blockDef.BuildTimeSeconds / ScaffoldSettings.Instance.GrindMultiplier, 0.5f);

                                                                      var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
                                                                      //IntegrityPointsPerSec = MaxIntegrity / BuildTimeSeconds
                                                                      //this is much, much faster than pulling the objectbuilder and getting the data from there.
                                                                      if (grid.Physics != null)
                                                                          stats.GrindTime += Math.Max((1 - block.BuildPercent()) * (blockDef.MaxIntegrity / blockDef.IntegrityPointsPerSec) / MyAPIGateway.Session.WelderSpeedMultiplier / item.Settings.WeldMultiplier, 0.5f);
                                                                      else
                                                                          stats.GrindTime += Math.Max(blockDef.MaxIntegrity / blockDef.IntegrityPointsPerSec / MyAPIGateway.Session.WelderSpeedMultiplier / item.Settings.WeldMultiplier, 0.5f);

                                                                      block.GetMissingComponents(missingComponents);

                                                                      if (grid.Physics != null)
                                                                      {
                                                                          foreach (KeyValuePair<string, int> missing in missingComponents)
                                                                          {
                                                                              if (!stats.TotalComponents.ContainsKey(missing.Key))
                                                                                  stats.TotalComponents.Add(missing.Key, missing.Value);
                                                                              else
                                                                                  stats.TotalComponents[missing.Key] += missing.Value;
                                                                          }
                                                                      }
                                                                      else
                                                                      {
                                                                          //projections will always consume the fully component count
                                                                          foreach (MyCubeBlockDefinition.Component component in blockDef.Components)
                                                                          {
                                                                              if (stats.TotalComponents.ContainsKey(component.Definition.Id.SubtypeName))
                                                                                  stats.TotalComponents[component.Definition.Id.SubtypeName] += component.Count;
                                                                              else
                                                                                  stats.TotalComponents.Add(component.Definition.Id.SubtypeName, component.Count);
                                                                          }
                                                                      }
                                                                      missingComponents.Clear();
                                                                  }

                                                                  blockList.Clear();
                                                              }

                                                              var result = new StringBuilder();
                                                              result.Append("Scan Results:\r\n\r\n");
                                                              result.Append("Targets: " + item.YardGrids.Count);
                                                              result.Append("\r\n");
                                                              result.Append("Block Count: " + stats.BlockCount);
                                                              result.Append("\r\n");
                                                              float grindTime = stats.GrindTime / (item.Settings.BeamCount * 8);
                                                              if (grindTime >= 7200)
                                                                  result.Append("Estimated Construct Time: " + (grindTime / 3600).ToString("0.00") + " hours");
                                                              else if (grindTime >= 120)
                                                                  result.Append("Estimated Construct Time: " + (grindTime / 60).ToString("0.00") + " min");
                                                              else
                                                                  result.Append("Estimated Construct Time: " + grindTime.ToString("0.00") + "s");
                                                              result.Append("\r\n");
                                                              if (stats.TotalComponents.Any())
                                                              {
                                                                  result.Append("Estimated Components Used:\r\n\r\n");
                                                                  double multiplier = Math.Max(item.ScaffoldBox.HalfExtent.LengthSquared() / 200000, 1);
                                                                  foreach (KeyValuePair<string, int> component in stats.TotalComponents)
                                                                  {
                                                                      if (component.Value != 0)
                                                                          result.Append($"{component.Key}: {component.Value / multiplier}\r\n");
                                                                  }
                                                              }

                                                              stats.Output = result.ToString();
                                                              statBlock.End();
                                                          }
                                                          catch (Exception ex)
                                                          {
                                                              Logging.Instance.WriteLine(ex.ToString());
                                                          }
                                                      });
            }

            if (!string.IsNullOrEmpty(stats.Output) && MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - stats.StartTime > 6000)
                return stats.Output;
            return "SCANNING...";
        }

        private string FormatMainMenu(ScaffoldItem item)
        {
            var result = new StringBuilder();
            result.Append("Automated Scaffold Main Menu\r\n\r\n");
            result.Append("Current targets: " + item.ContainsGrids.Count);
            result.Append("\r\n\r\n");

            result.Append(". Exit : Up :. Down :: Select");
            result.Append("\r\n\r\n");

            return result.ToString();
        }

        private MenuItem.MenuAction WeldDel(LCDMenu root, MenuItem item)
        {
            MenuItem.MenuAction handler = () =>
                                          {
                                              long id = root.Panel.CubeGrid.EntityId;

                                              root.SetCurrentItem(item);
                                              Communication.SendYardCommand(id, ScaffoldType.Weld);
                                          };
            return handler;
        }

        private MenuItem.MenuAction GrindDel(LCDMenu root, MenuItem item)
        {
            MenuItem.MenuAction handler = () =>
                                          {
                                              long id = root.Panel.CubeGrid.EntityId;

                                              root.SetCurrentItem(item);
                                              Communication.SendYardCommand(id, ScaffoldType.Grind);
                                          };
            return handler;
        }

        private MenuItem.MenuAction ScanDel(LCDMenu root, MenuItem item, ScaffoldType? itemType = null)
        {
            MenuItem.MenuAction handler = () =>
                                          {
                                              long id = root.Panel.CubeGrid.EntityId;

                                              root.SetCurrentItem(item);
                                              if (itemType.HasValue)
                                                  Communication.SendYardCommand(id, itemType.Value);
                                              if (itemType == 0)
                                                  foreach (ScaffoldItem yard in ProcessScaffoldDetection.ScaffoldsList)
                                                  {
                                                      if (yard.EntityId == id)
                                                      {
                                                          _stats.Remove(yard);
                                                          break;
                                                      }
                                                  }
                                          };
            return handler;
        }

        private MenuItem.MenuAction StopDel(LCDMenu root, MenuItem item)
        {
            MenuItem.MenuAction handler = () =>
                                          {
                                              long id = root.Panel.CubeGrid.EntityId;

                                              root.SetCurrentItem(item);
                                              Communication.SendYardCommand(id, ScaffoldType.Disabled);
                                          };
            return handler;
        }

        private MenuItem.DescriptionAction StatusDel(long id)
        {
            MenuItem.DescriptionAction handler = () =>
                                                 {
                                                     foreach (ScaffoldItem item in ProcessScaffoldDetection.ScaffoldsList)
                                                     {
                                                         if (item.EntityId == id)
                                                             return FormatStatus(item);
                                                     }
                                                     return "";
                                                 };
            return handler;
        }

        private MenuItem.DescriptionAction GrindStatsDel(long id)
        {
            MenuItem.DescriptionAction handler = () =>
                                                 {
                                                     foreach (ScaffoldItem item in ProcessScaffoldDetection.ScaffoldsList)
                                                     {
                                                         if (item.EntityId == id)
                                                             return FormatGrindStats(item);
                                                     }
                                                     return "";
                                                 };
            return handler;
        }

        private MenuItem.DescriptionAction WeldStatsDel(long id)
        {
            MenuItem.DescriptionAction handler = () =>
                                                 {
                                                     foreach (ScaffoldItem item in ProcessScaffoldDetection.ScaffoldsList)
                                                     {
                                                         if (item.EntityId == id)
                                                             return FormatWeldStats(item);
                                                     }
                                                     return "";
                                                 };
            return handler;
        }

        private MenuItem.DescriptionAction MenuDel(long id)
        {
            MenuItem.DescriptionAction handler = () =>
                                                 {
                                                     foreach (ScaffoldItem item in ProcessScaffoldDetection.ScaffoldsList)
                                                     {
                                                         if (item.EntityId == id)
                                                             return FormatMainMenu(item);
                                                     }
                                                     return "";
                                                 };
            return handler;
        }

        private class StatInfo
        {
            public int BlockCount;
            public float GrindTime;
            public string Output;
            public double StartTime;
            public Dictionary<string, int> TotalComponents;
        }
    }
}