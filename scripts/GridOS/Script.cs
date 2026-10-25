using System;

// Space Engineers DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;
using System.Threading;
using System.Linq;
using System.Data;
using System.Timers;
using Sandbox.Game;
using SpaceEngineers.Game.VoiceChat;

/*
 * Must be unique per each script project.
 * Prevents collisions of multiple `class Program` declarations.
 * Will be used to detect the ingame script region, whose name is the same.
 */
namespace GridOS
{

    /*
     * Do not change this declaration because this is the game requirement.
     */
    public sealed class Program : MyGridProgram
    {
        #region GridOS

        #region Declarations

        // ==================================================
        // GridOS Script
        // ==================================================

        const string tag = "[OS]";
        const string version = "0.1";
        int frame = 0;
        static DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Configuration and storage
        List<string> configuration;
        MyIni storage = new MyIni();
        const int MaxLogEntries = 5;

        // Menu system
        private Menu gpsList, gridList;
        private MenuManager menuManager;

        // Blocks
        private IMyShipController ShipController;
        private IMyTextSurface LcdMain, LcdOut;

        // Display and page settings
        const float LCDMainFontSize = 0.8f;
        static Color ForegroundColor = new Color(255, 173, 0); // Orange
        static Color BackgroundColor = new Color(0, 0, 0); // Black
        const int MaxLines = 13;      // Total lines on screen
        const int HeaderLines = 2;    // Lines used by header
        const int FooterLines = 2;    // Lines used by footer
        const int MaxMenuItems = MaxLines - HeaderLines - FooterLines; // Avaiable lines for menu items


        static readonly Dictionary<int, string> typeNames = new Dictionary<int, string>
        {
            { 0, "Solid body" },
            { 1, "Station" },
            { 2, "Miners" },
            { 3, "Traders" },
            { 4, "Builders" },
            { 5, "Military" },
            { 6, "Pirates" }
        };

        static readonly Dictionary<int, string> typeColors = new Dictionary<int, string>
        {
            { 0, "#FF888888" }, // Solid body - gray
            { 1, "#FF75C9F1" }, // Station - default 
            { 2, "#FFFFA500" }, // Miners - orange
            { 3, "#FF4169E1" }, // Traders - blue
            { 4, "#FFFFD700" }, // Builders - gold
            { 5, "#FF228B22" }, // Military - green
            { 6, "#FFDC143C" }  // Pirates - red
        };

        #endregion // Declarations

        #region Initialization
        // Load configuration and state

        public Program()
        {
            LoadConfiguration();
            LoadState();
            InitBlocks();
            CreateMenu();
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        private void LoadConfiguration()
        {
            configuration = new List<string>(Me.CustomData.Split('\n'));
        }

        private void LoadState()
        {
            storage.TryParse(Storage);

            if (!storage.ContainsSection("Entries"))
            {
                storage.AddSection("Entries");
            }

            if (!storage.ContainsSection("Factions"))
            {
                storage.AddSection("Factions");
            }

            var entryKeys = new List<MyIniKey>();
            var factionKeys = new List<MyIniKey>();

            storage.GetKeys("Entries", entryKeys);
            storage.GetKeys("Factions", factionKeys);

            DataManager.LoadFromStorage(storage);
        }

        private void InitBlocks()
        {

            // Use PB's panels for display by default
            LcdMain = Me.GetSurface(0);
            LcdOut = Me.GetSurface(1);

            // Find Ship Controller with the tag
            var controllers = new List<IMyShipController>();
            GridTerminalSystem.GetBlocksOfType<IMyShipController>(controllers);
            foreach (var controller in controllers)
            {
                if (controller.CustomName.Contains(tag))
                {
                    ShipController = controller;

                    // Disable ship controls to prevent conflicts
                    ShipController.ControlThrusters = false;
                    ShipController.ControlWheels = false;

                    // User has to disable gyros control manually
                    // ShipController.ControlGyros = false;

                    // Use controller's panels for display
                    IMyTextSurfaceProvider provider = controller as IMyTextSurfaceProvider;
                    int displays = provider.SurfaceCount;

                    // Set displays
                    if (displays > 0) LcdMain = provider.GetSurface(0);
                    if (displays > 1) LcdOut = provider.GetSurface(1);
                    SetPanels(new List<IMyTextSurface> { LcdMain, LcdOut });

                    Log($"Using [{tag}] ship controller: {controller.CustomName}");
                    break;
                }
            }
            if (ShipController == null)
            {
                Log($"Using PB for display, no [{tag}] ship controller found.");
            }
        }

        private void SetPanels(List<IMyTextSurface> panels)
        {
            foreach (var panel in panels)
            {
                if (panel == null) continue;
                panel.ContentType = ContentType.TEXT_AND_IMAGE;
                panel.Font = "Debug";
                panel.FontSize = LCDMainFontSize;
                panel.TextPadding = 2f;
                panel.Alignment = TextAlignment.LEFT;
                panel.FontColor = ForegroundColor;
                panel.BackgroundColor = BackgroundColor;
            }
        }

        private void CreateMenu()
        {

            // Build GPS menu
            var gpsMenu = new Menu { Key = "GPS", Label = "GPS Manager" };
            gpsList = new Menu { Key = "gps_list", Label = "View entries" }; // Dynamic GPS list
            gpsMenu.Add(gpsList);
            gpsMenu.Add(new ActionItem { Key = "import", Label = "Import GPS Entries", Action = "gps_import" });
            gpsMenu.Add(new ActionItem { Key = "export", Label = "Export GPS Entries", Action = "gps_export" });

            // Build Grid menu
            var gridMenu = new Menu { Key = "Grid", Label = "Grid control" };
            gridList = new Menu { Key = "grid_list", Label = "View blocks" }; // Dynamic Grid list
            gridMenu.Add(gridList);

            // Build Main menu
            var mainMenu = new Menu { Key = "Main", Label = "GridOS Main Menu" };
            mainMenu.Add(gpsMenu);
            mainMenu.Add(gridMenu);
            mainMenu.Add(new ActionItem { Key = "dump", Label = "Dump Storage data", Action = "dump" });

            // Initialize Menu Manager
            menuManager = new MenuManager
            {
                RootMenu = mainMenu,
                CurrentMenu = mainMenu
            };
        }
        #endregion // Initialization

        #region Main
        public void Main(string argument)
        {
            if (argument != "") ParseArguments(argument);
            LoadConfiguration();
            RefreshDynamicMenus();
            RenderMenu();
            HandleControls();
            PrintStatus();
        }

        private void RefreshDynamicMenus()
        {

            // Refresh GPS List
            gpsList.Items.Clear();
            Vector3D MyPosition = Me.GetPosition();
            DataManager.Entries.Sort((a, b) => a.DistanceTo(MyPosition).CompareTo(b.DistanceTo(MyPosition)));
            foreach (var entry in DataManager.Entries)
            {
                var entryMenu = new Menu { Key = entry.Name, Label = $"{entry.Name}" + (entry.IsFactionStation() ? $" {entry.GetTypeString()}" : "") + $" ({entry.DistanceTo(MyPosition) / 1000:F1} km)" };
                entryMenu.Add(new TextItem { Label = $"Distance: {entry.DistanceTo(MyPosition) / 1000:F1} km" });
                double distance;
                entryMenu.Add(new TextItem { Label = $"Closest body: {entry.ClosestBody(out distance)} ({distance / 1000:F1} km)" });
                entryMenu.Add(new TextItem { Label = $" X: {entry.GetPosition().X:F0} Y: {entry.GetPosition().Y:F0} Z: {entry.GetPosition().Z:F0}" });
                entryMenu.Add(new TextItem { Label = "" });
                entryMenu.Add(new ActionItem { Key = "goto", Label = "Set as destination", Action = $"goto" });
                entryMenu.Add(new ActionItem { Key = "export", Label = "Export to LCD", Action = $"export" });
                entryMenu.Add(new ActionItem { Key = "change", Label = "Change type", Action = $"change_type" });
                entryMenu.Add(new ActionItem { Key = "delete", Label = "Delete entry", Action = $"delete" });
                gpsList.Add(entryMenu);
            }

            // Refresh Grid List
            gridList.Items.Clear();
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                var blockMenu = new Menu { Key = block.EntityId.ToString(), Label = block.CustomName };
                blockMenu.Add(new TextItem { Label = $"Type: {block.BlockDefinition.TypeId}" });
                gridList.Add(blockMenu);
            }

            UpdateCurrentMenuReference();
        }

        private void UpdateCurrentMenuReference()
        {
            var topMenus = new List<Menu> { gpsList, gridList };
            if (menuManager == null || menuManager.CurrentMenu == null)
                return;

            if (topMenus.Contains(menuManager.CurrentMenu))
                return;

            foreach (var list in topMenus)
            {
                var refreshedMenu = list.Items.FirstOrDefault(m => m.Key == menuManager.CurrentMenu.Key) as Menu;
                if (refreshedMenu != null)
                {
                    menuManager.CurrentMenu = refreshedMenu;
                    break;
                }
            }
        }

        private void RenderMenu()
        {
            if (menuManager?.CurrentMenu == null) return;
            string output = "";

            int totalItems = menuManager.CurrentMenu.Items.Count;
            int selected = menuManager.CurrentIndex;

            // Header 2 lines
            output += $"=== {menuManager.CurrentMenu.Label} [{totalItems}] ===\n";
            output += $"\n";

            int start = selected - MaxMenuItems / 2;
            if (start < 0) start = 0;
            if (start > totalItems - MaxMenuItems) start = totalItems - MaxMenuItems;
            if (start < 0) start = 0;

            int end = start + MaxMenuItems;
            if (end > totalItems) end = totalItems;


            for (int i = start; i < end; i++)
            {
                var item = menuManager.CurrentMenu.Items[i];
                string prefix = (i == menuManager.CurrentIndex) ? "> " : item.IsSelectable ? "- " : "";
                output += $"{prefix}{item.Label}\n";
            }

            // Fill empty lines
            for (int i = end; i < start + MaxMenuItems; i++) { output += "\n"; }

            // Footer
            output += $"\n";
            output += $"Q: Back";

            // Print to Main display
            LcdMain.WriteText(output);
        }

        private void HandleControls()
        {
            if (ShipController == null) return; // Check Ship Controller inputs if available
            if (ShipController.MoveIndicator.Z < 0) MoveForward(); // W
            else if (ShipController.MoveIndicator.Z > 0) MoveBackward(); // S
            if (ShipController.MoveIndicator.X < 0) MoveLeft(); // A
            else if (ShipController.MoveIndicator.X > 0) MoveRight(); // D
            if (ShipController.MoveIndicator.Y > 0) MoveUp(); // SPACE
            else if (ShipController.MoveIndicator.Y < 0) MoveDown(); // C
            if (ShipController.RollIndicator < 0) RollLeft(); // Q
            else if (ShipController.RollIndicator > 0) RollRight(); // E
        }

        private void PrintStatus()
        {
            string output = "";
            string bar = $"{new string('=', frame % 10)}-{new string('=', 9 - (frame % 10))}";
            output += $"{bar} GridOS v{version} {bar}\n\n";
            output += $"CustomData: {configuration.Count.ToString()} lines\n";
            output += $"Storage: {storage.ToString().Split('\n').Length} lines\n\n";
            output += "Last actions:\n";
            for (var i = DataManager.Log.Count - 1; i >= 0; i--)
            {
                output += $"({DataManager.Log[i].Elapsed()}) {DataManager.Log[i].Message}\n";
            }
            Echo(output);
            frame++;
        }

        #endregion // Main

        #region Commands
        private void ParseArguments(string argument)
        {

            // GPS Entry
            if (argument.StartsWith("GPS:"))
            {
                GPSEntry entry = ParseGPS(argument);
                if (entry != null) StoreEntry(entry);
                else Log("Invalid GPS format.");
                return;
            }

            string[] _args = argument.Split(' ');
            string _command = _args[0].ToLower();

            // Handle commands
            switch (_command)
            {

                // Menu navigation
                case "forward": MoveForward(); break; // W
                case "backward": MoveBackward(); break; // S
                case "left": MoveLeft(); break; // A
                case "right": MoveRight(); break; // D
                case "back": RollLeft(); break; // Q
                case "ok": RollRight(); break;  // E
                case "up": MoveUp(); break; // SPACE
                case "down": MoveDown(); break;  // C
                // GPS management
                case "import": ImportGPS(); break; // Import GPS entries
                case "export": ExportGPS(); break; // Export GPS entries
                // Debugging
                case "dump": Dump(); break; // Dump Storage to screen
                default: Log($"Unknown command: {_command}"); break;
            }
        }

        private void ImportGPS() { } // Import GPS entries
        private void ExportGPS() { } // Export GPS entries

        #endregion // Commands

        #region Handlers

        private void MoveForward() { menuManager?.MoveUp(); } // Handle W key press
        private void MoveBackward() { menuManager?.MoveDown(); } // Handle S key press
        private void MoveLeft() { menuManager?.MoveUp(MaxMenuItems); } // Handle A key press
        private void MoveRight() { menuManager?.MoveDown(MaxMenuItems); } // Handle D key press
        private void MoveUp() { } // Handle SPACE key press
        private void MoveDown() { } // Handle C key press
        private void RollLeft() { menuManager?.Back(); } // Handle Q key press
        private void RollRight() // Handle E key press
        {
            if (menuManager?.CurrentMenu == null) return;
            string action;
            if (menuManager.Select(out action) && !string.IsNullOrEmpty(action))
                ParseArguments(action);
        }

        #endregion // Handlers

        #region Save

        // Save state and configuration
        public void Save()
        {
            SaveConfiguration();
            SaveState();
        }

        private void SaveConfiguration()
        {
            Me.CustomData = string.Join("\n", configuration);
        }

        private void SaveState()
        {
            DataManager.SaveToStorage(storage);
            Storage = storage.ToString();
        }
        #endregion // Save

        #region Data Structures

        public class GPSEntry
        {
            public string Name;
            public string Location;
            public Vector3D GetPosition()
            {
                string[] coords = Location.Split(',');
                if (coords.Length != 3) return Vector3D.Zero;
                double x, y, z;
                double.TryParse(coords[0], out x);
                double.TryParse(coords[1], out y);
                double.TryParse(coords[2], out z);
                return new Vector3D(x, y, z);
            }

            public double DistanceTo(Vector3D coordinates)
            {
                return Vector3D.Distance(GetPosition(), coordinates);
            }

            public bool IsFactionStation()
            {
                return DataManager.Factions.ContainsKey(Name);
            }

            public string GetTypeString()
            {
                int type = IsFactionStation() ? DataManager.Factions[Name] : 0;
                return typeNames[type];
            }

            public string ClosestBody(out double distance)
            {
                // Find closest solid body from DataManager entries
                double minDistance = double.MaxValue;
                string closestBody = "None";

                foreach (var entry in DataManager.Entries)
                {
                    if (entry == this) continue; // Skip self
                    if (entry.GetTypeString() != "Solid body") continue; // Only solid bodies
                    double dist = DistanceTo(entry.GetPosition());
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestBody = entry.Name;
                    }
                }
                distance = (minDistance == double.MaxValue) ? -1 : minDistance;
                return closestBody;
            }


        }

        public class DataManager
        {
            public static List<LogEntry> Log { get; private set; } = new List<LogEntry>();
            public static List<GPSEntry> Entries { get; private set; } = new List<GPSEntry>();
            public static Dictionary<string, int> Factions { get; private set; } = new Dictionary<string, int>();
            public static void LoadFromStorage(MyIni storage)
            {
                Entries.Clear();
                Factions.Clear();
                Log.Clear();

                // Load log entries
                var logKeys = new List<MyIniKey>();
                storage.GetKeys("Log", logKeys);

                foreach (var key in logKeys)
                {
                    string[] parts = storage.Get("Log", key.Name).ToString().Split(' ');
                    long timestamp = long.Parse(parts[0]);
                    string message = string.Join(" ", parts.Skip(1));
                    Log.Add(new LogEntry() { Timestamp = timestamp, Message = message });
                }

                // Load Factions
                var factionKeys = new List<MyIniKey>();
                storage.GetKeys("Factions", factionKeys);

                foreach (var key in factionKeys)
                {
                    string value = storage.Get("Factions", key.Name).ToString();
                    int type;
                    if (int.TryParse(value, out type))
                    {
                        Factions[key.Name] = type;
                    }
                }

                // Load Entries
                var entryKeys = new List<MyIniKey>();
                storage.GetKeys("Entries", entryKeys);

                foreach (var key in entryKeys)
                {
                    string value = storage.Get("Entries", key.Name).ToString();
                    string[] parts = value.Split('|');

                    if (parts.Length == 2)
                    {
                        Entries.Add(new GPSEntry
                        {
                            Name = parts[0],
                            Location = parts[1]
                        });
                    }
                }
            }

            public static void SaveToStorage(MyIni storage)
            {
                // Clear existing data
                storage.Clear();

                //Add sections back
                storage.AddSection("Factions");
                storage.AddSection("Entries");
                storage.AddSection("Log");

                // Save log entries
                for (int i = 0; i < Math.Min(Log.Count, MaxLogEntries); i++)
                {
                    storage.Set("Log", $"log_{i}", $"{Log[i].Timestamp} {Log[i].Message}");
                }

                // Save Factions
                foreach (var faction in Factions)
                {
                    storage.Set("Factions", faction.Key, faction.Value.ToString());
                }

                // Save Entries
                int index = 0;
                foreach (var entry in Entries)
                {
                    storage.Set("Entries", $"entry_{index}", $"{entry.Name}|{entry.Location}");
                    index++;
                }
            }
        }

        public class LogEntry
        {
            public long Timestamp;
            public string Message;
            public string Elapsed()
            {
                long t = (long)(DateTime.UtcNow - epoch).TotalSeconds - Timestamp;
                return t < 60 ? $"{t}s" : t < 3600 ? $"{t / 60}m" : t < 86400 ? $"{t / 3600}h" : $"{t / 86400}d";
            }
        }

        public abstract class MenuNode
        {
            public string Key;
            public string Label;
            public virtual bool IsSelectable { get { return true; } }
            public virtual bool OnSelect(MenuManager menuManager, out string action)
            {
                action = null;
                return false;
            }
        }

        public class Menu : MenuNode
        {
            public List<MenuNode> Items = new List<MenuNode>();
            public Menu Add(MenuNode item)
            {
                Items.Add(item);
                return this;
            }
            public override bool OnSelect(MenuManager menuManager, out string action)
            {
                action = null;
                menuManager.NavigateTo(this);
                return false;
            }
        }

        public class ActionItem : MenuNode
        {
            public string Action;
            public override bool OnSelect(MenuManager menuManager, out string action)
            {
                action = Action;
                return true;
            }
        }

        public class ToggleItem : MenuNode
        {
            public bool IsOn;
            public override bool OnSelect(MenuManager menuManager, out string action)
            {
                action = null;
                IsOn = !IsOn;
                return false;
            }
        }
        public class SliderItem : MenuNode
        {
            public int Value;
            public int Min;
            public int Max;
        }

        public class TextItem : MenuNode
        {
            public string Text;
            public override bool IsSelectable { get { return false; } }
        }

        public class MenuManager
        {
            public Menu RootMenu;
            public Menu CurrentMenu;
            public int CurrentIndex = 0;
            public Stack<MenuHistoryEntry> History = new Stack<MenuHistoryEntry>();
            public void MoveUp(int amount = 1)
            {
                for (int i = 0; i < amount; i++)
                {
                    do
                    {
                        if (CurrentIndex > 0 && CurrentIndex > GetFirstSelectableIndex(CurrentMenu))
                            CurrentIndex--;
                        else
                            break;

                    } while (!CurrentMenu.Items[CurrentIndex].IsSelectable);
                }
            }
            public void MoveDown(int amount = 1)
            {
                for (int i = 0; i < amount; i++)
                {
                    do
                    {
                        if (CurrentMenu.Items.Count > 0 && CurrentIndex < CurrentMenu.Items.Count - 1)
                            CurrentIndex++;
                        else
                            break;

                    } while (!CurrentMenu.Items[CurrentIndex].IsSelectable);
                }
            }
            public void NavigateTo(Menu menu)
            {
                History.Push(new MenuHistoryEntry(CurrentMenu, CurrentIndex));
                CurrentMenu = menu;
                CurrentIndex = GetFirstSelectableIndex(CurrentMenu);
            }
            public void Back()
            {
                if (History.Count > 0)
                {
                    var entry = History.Pop();
                    CurrentMenu = entry.Menu;
                    CurrentIndex = entry.Index;
                }
            }
            public bool Select(out string action)
            {
                action = null;
                if (CurrentMenu.Items == null || CurrentMenu.Items.Count == 0)
                    return false;

                var node = CurrentMenu.Items[CurrentIndex];
                return node.OnSelect(this, out action);
            }
        }

        public class MenuHistoryEntry
        {
            public Menu Menu;
            public int Index;
            public MenuHistoryEntry(Menu menu, int index)
            {
                Menu = menu;
                Index = index;
            }
        }

        #endregion // Data Structures

        #region Utilities

        private GPSEntry ParseGPS(string gpsString)
        {
            string[] parts = gpsString.Split(':');

            if (parts.Length < 6 || parts[0] != "GPS")
                return null;

            try
            {
                double x = Math.Round(double.Parse(parts[2]), 2);
                double y = Math.Round(double.Parse(parts[3]), 2);
                double z = Math.Round(double.Parse(parts[4]), 2);

                return new GPSEntry
                {
                    Name = parts[1],
                    Location = $"{x},{y},{z}"
                };
            }
            catch
            {
                return null;
            }
        }

        private void StoreEntry(GPSEntry entry)
        {
            // Avoid duplicates based on location
            if (LocationExists(entry.Location))
            {
                Log("Entry with this location already exists.");
                return;
            }

            // Check for faction tag in name
            string name = entry.Name.Split(' ')[0];
            if (name.Length >= 3 && name.Length <= 5 && name.All(c => char.IsUpper(c)))
            {
                entry.Name = name;

                // Store faction if not already present
                if (!DataManager.Factions.ContainsKey(name))
                {
                    DataManager.Factions[name] = 1;
                }
            }

            // Store GPS entry
            DataManager.Entries.Add(entry);
            Log($"Stored GPS: {entry.Name}");
            SaveState();
        }

        private bool LocationExists(string location)
        {
            foreach (var e in DataManager.Entries)
            {
                if (e.Location == location)
                {
                    return true;
                }
            }
            return false;
        }

        private void Log(string message)
        {
            // Get current timestamp in total seconds
            long timestamp = (long)(DateTime.UtcNow - epoch).TotalSeconds;

            DataManager.Log.Add(new LogEntry() { Timestamp = timestamp, Message = message });

            // Clamp log size
            if (DataManager.Log.Count > MaxLogEntries)
            {
                DataManager.Log.RemoveAt(0);
            }
        }

        private void Dump()
        {
            SaveState();

            string output = "=== Storage Dump ===\n";
            output += storage.ToString();

            // Print to Secondary display
            LcdOut.WriteText(output);

            Log("Storage dumped to screen.");
        }

        private static int GetFirstSelectableIndex(Menu menu)
        {
            for (int i = 0; i < menu.Items.Count; i++)
            {
                if (menu.Items[i].IsSelectable)
                    return i;
            }
            return 0;
        }

        #endregion // Utilities

        #endregion // GridOS
    }
}