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
using System.Linq;
using System.Text;

/*
 * Must be unique per each script project.
 * Prevents collisions of multiple `class Program` declarations.
 * Will be used to detect the ingame script region, whose name is the same.
 */
namespace GPSManager
{

    /*
     * Do not change this declaration because this is the game requirement.
     */
    public sealed class Program : MyGridProgram
    {

        /*
         * Must be same as the namespace. Will be used for automatic script export.
         * The code inside this region is the ingame script.
         */
        // Ship OS UI Skeleton for Space Engineers
        // Focus: menu navigation & multi-page UI; content actions to be filled later.

        #region Configuration

        // LCD panels or cockpits whose name contains this tag will render the OS UI
        const string DisplayTag = "[OS]";

        // Max items per page
        const int PageSize = 10;

        // GPS paging size (separate from menu so we can tune independently later)
        const int GpsPageSize = 10;

        // CustomData menu section tag
        const string MenuSectionTag = "[MENU]";

        // Highlight and palette
        readonly Color Highlight = new Color(120, 220, 255);
        readonly Color Dim = new Color(180, 180, 180);
        readonly Color Header = new Color(220, 220, 220);
        readonly Color Accent = new Color(255, 200, 80);
        readonly Color TextOnHighlight = new Color(15, 15, 15);
        const float HeaderLineHeight = 24f;
        const float RowLineHeight = 22f;
        const float TextScale = 0.8f;

        // GPS type mappings
        readonly Dictionary<int, string> typeNames = new Dictionary<int, string>
    {
        { 0, "Solid body" },
        { 1, "Station" },
        { 2, "Shipyard" },
        { 3, "Miners" },
        { 4, "Ammunition" },
        { 5, "Factory" },
        { 6, "Trader" }
    };

        readonly Dictionary<int, string> typeColors = new Dictionary<int, string>
    {
        { 0, "#808080" }, // Solid body - gray
        { 1, "#00D9FF" }, // Station - cyan
        { 2, "#FF8C00" }, // Shipyard - dark orange
        { 3, "#FFFF00" }, // Miners - yellow
        { 4, "#FF1493" }, // Ammunition - deep pink
        { 5, "#00AA00" }, // Factory - green
        { 6, "#FFAA00" }  // Trader - orange
    };

        #endregion

        #region GPS UI & Input

        void EnterGpsMode()
        {
            uiMode = UiMode.Gps;
            gpsState = new GpsState();
            SetLastInput("GPS");
        }

        void ExitGpsMode()
        {
            uiMode = UiMode.Menu;
            SetLastInput("BACK");
        }

        void HandleGpsArgumentInput(string arg)
        {
            if (arg == "UP") GpsMoveSelection(1);
            else if (arg == "DOWN") GpsMoveSelection(-1);
            else if (arg == "LEFT") GpsHandleLeft();
            else if (arg == "RIGHT") GpsHandleRight();
            else if (arg == "BACK" || arg == "Q" || arg == "CANCEL") GpsCancel();
            else if (arg == "OK" || arg == "ENTER" || arg == "E") GpsActivate();
            else if (arg == "PGUP") GpsPage(-1);
            else if (arg == "PGDN") GpsPage(1);
            else if (arg.StartsWith("GPS:")) StoreGPS(arg);
        }

        void HandleGpsControllerInput(Vector3 move, Vector2 rot, float roll)
        {
            if (move.Z < -0.05f) GpsMoveSelection(-1);
            if (move.Z > 0.05f) GpsMoveSelection(1);
            if (move.X < -0.05f) GpsHandleLeft();
            if (move.X > 0.05f) GpsHandleRight();
            if (move.Y > 0.05f) GpsHandleYPositive();
            if (move.Y < -0.05f) GpsHandleYNegative();
            if (rot.X < -0.05f) GpsMoveSelection(-1);
            if (rot.X > 0.05f) GpsMoveSelection(1);
            if (rot.Y < -0.05f) GpsHandleLeft();
            if (rot.Y > 0.05f) GpsHandleRight();
            if (roll > 0.05f) GpsActivate();
            if (roll < -0.05f) GpsCancel();
        }

        void GpsMoveSelection(int delta)
        {
            List<string> entries; List<string> factions; List<SortedEntry> sorted; int start; int end;
            GetGpsPageData(out entries, out factions, out sorted, out start, out end);
            if (gpsState.InTypeChange)
            {
                List<int> validTypes = new List<int>(typeNames.Keys); validTypes.Sort();
                int count = validTypes.Count;
                gpsState.TypeSelectionIndex = (gpsState.TypeSelectionIndex + (delta > 0 ? 1 : -1) + count) % count;
                if (delta > 0) SetLastInput("DOWN"); else if (delta < 0) SetLastInput("UP");
                return;
            }

            if (gpsState.InDetailView)
            {
                List<string> entries2 = GetEntriesSection();
                if (gpsState.DetailEntryIndex >= 0 && gpsState.DetailEntryIndex < entries2.Count)
                {
                    var info = ParseEntry(entries2[gpsState.DetailEntryIndex]);
                    bool isFaction = IsFactionIdentifier(info.Identifier);
                    int optionCount = isFaction ? 4 : 3;
                    gpsState.DetailSelection = (gpsState.DetailSelection + (delta > 0 ? 1 : -1) + optionCount) % optionCount;
                }
                if (delta > 0) SetLastInput("DOWN"); else if (delta < 0) SetLastInput("UP");
                return;
            }

            int displayed = end - start;
            if (displayed <= 0) return;

            gpsState.Selection = (gpsState.Selection + (delta > 0 ? 1 : -1) + displayed) % displayed;

            if (delta > 0) SetLastInput("DOWN");
            else if (delta < 0) SetLastInput("UP");
        }

        void GpsPage(int delta)
        {
            List<string> entries; List<string> factions; List<SortedEntry> sorted; int start; int end;
            GetGpsPageData(out entries, out factions, out sorted, out start, out end);
            int maxPage = sorted.Count == 0 ? 0 : (sorted.Count - 1) / GpsPageSize;
            gpsState.PageIndex = Math.Max(0, Math.Min(maxPage, gpsState.PageIndex + delta));
            gpsState.Selection = 0;
        }

        void GpsActivate()
        {
            List<string> entries; List<string> factions; List<SortedEntry> sorted; int start; int end;
            GetGpsPageData(out entries, out factions, out sorted, out start, out end);
            int totalEntries = sorted.Count;
            int filterStart = totalEntries;
            int totalItems = totalEntries + 3;
            if (totalItems == 0) return;

            // Type change mode - apply type change
            if (gpsState.InTypeChange)
            {
                List<int> validTypes = new List<int>(typeNames.Keys); validTypes.Sort();
                int newTypeIndex = validTypes[gpsState.TypeSelectionIndex % validTypes.Count];
                if (gpsState.DetailEntryIndex >= 0) ChangeEntryType(gpsState.DetailEntryIndex, newTypeIndex);
                gpsState.InTypeChange = false;
                gpsState.TypeSelectionIndex = 0;
                return;
            }

            // Detail view - execute selected action
            if (gpsState.InDetailView)
            {
                int actualIndex = gpsState.DetailEntryIndex;
                if (actualIndex < 0 || actualIndex >= entries.Count) { ExitDetailView(); return; }

                var info = ParseEntry(entries[actualIndex]);
                bool isFaction = IsFactionIdentifier(info.Identifier);
                int optionCount = isFaction ? 4 : 3;

                if (gpsState.DetailSelection == 0) { ExportToLCD(actualIndex); ExitDetailView(); }
                else if (gpsState.DetailSelection == 1) { SetAIDestination(actualIndex); ExitDetailView(); }
                else if (gpsState.DetailSelection == 2)
                {
                    if (isFaction) { gpsState.InTypeChange = true; gpsState.TypeSelectionIndex = 0; }
                    else { DeleteEntry(actualIndex); ExitDetailView(); }
                }
                else if (gpsState.DetailSelection == 3 && isFaction)
                {
                    DeleteEntry(actualIndex); ExitDetailView();
                }
                return;
            }

            // List view - enter detail view for selected entry
            int sortedIndex = start + gpsState.Selection;
            if (sortedIndex >= 0 && sortedIndex < sorted.Count)
            {
                gpsState.InDetailView = true;
                gpsState.DetailEntryIndex = sorted[sortedIndex].ActualIndex;
                gpsState.DetailSelection = 0;
            }
        }

        void ExitDetailView()
        {
            gpsState.InDetailView = false;
            gpsState.DetailEntryIndex = -1;
            gpsState.DetailSelection = 0;
        }

        void GpsHandleLeft()
        {
            SetLastInput("LEFT");
            if (!gpsState.InDetailView && !gpsState.InTypeChange)
            {
                GpsPage(-1);
            }
        }

        void GpsHandleRight()
        {
            SetLastInput("RIGHT");
            if (!gpsState.InDetailView && !gpsState.InTypeChange)
            {
                GpsPage(1);
            }
        }

        void GpsHandleYPositive()
        {
            SetLastInput("SPACE");
            if (!gpsState.InDetailView && !gpsState.InTypeChange)
            {
                gpsState.FilterSelection = (gpsState.FilterSelection + 1) % 3;
            }
        }

        void GpsHandleYNegative()
        {
            SetLastInput("C");
            if (!gpsState.InDetailView && !gpsState.InTypeChange)
            {
                CycleFilter(gpsState.FilterSelection);
            }
        }

        void GpsCancel()
        {
            if (gpsState.InTypeChange)
            {
                gpsState.InTypeChange = false;
                gpsState.TypeSelectionIndex = 0;
            }
            else if (gpsState.InDetailView)
            {
                ExitDetailView();
            }
            else
            {
                ExitGpsMode();
            }
        }

        void GetGpsPageData(out List<string> entries, out List<string> factions, out List<SortedEntry> sorted, out int start, out int end)
        {
            entries = GetEntriesSection();
            factions = GetFactionsSection();
            sorted = GetSortedFilteredEntries(entries, factions);
            start = gpsState.PageIndex * GpsPageSize;
            if (start >= sorted.Count && sorted.Count > 0)
            {
                gpsState.PageIndex = 0;
                start = 0;
            }
            end = Math.Min(sorted.Count, start + GpsPageSize);
        }

        void ClampGpsState()
        {
            List<string> entries; List<string> factions; List<SortedEntry> sorted; int start; int end;
            GetGpsPageData(out entries, out factions, out sorted, out start, out end);
            int displayed = end - start;
            if (gpsState.Selection >= displayed) gpsState.Selection = Math.Max(0, displayed - 1);
            if (gpsState.Selection < 0) gpsState.Selection = 0;
            int maxPage = sorted.Count == 0 ? 0 : (sorted.Count - 1) / GpsPageSize;
            if (gpsState.PageIndex > maxPage) gpsState.PageIndex = maxPage;
        }

        #endregion

        #region State

        class MenuEntry
        {
            public string Id;
            public string Title;
            public string ParentId;
        }

        class MenuNode
        {
            public string Id;
            public string Title;
            public List<MenuNode> Children = new List<MenuNode>();
            public Action Action = delegate { }; // Placeholder hook to avoid null warnings until wired
        }

        class UiState
        {
            public List<int> Path = new List<int>(); // indexes into children at each depth
            public int PageIndex = 0;
            public int Selection = 0;
        }

        class GpsState
        {
            public int Selection = 0;
            public int PageIndex = 0;
            public bool InDetailView = false;
            public int DetailEntryIndex = -1; // actual index in entries list
            public bool InTypeChange = false;
            public int DetailSelection = 0; // which option is selected in detail view
            public int TypeSelectionIndex = 0;
            public string FilterFaction = "ALL";
            public int FilterType = -1; // -1 = ALL
            public int FilterRange = -1; // -1 = ALL, otherwise km
            public int FilterSelection = 0; // 0=faction, 1=type, 2=range
        }

        enum UiMode
        {
            Menu,
            Gps
        }

        MenuNode root;
        UiState state = new UiState();
        GpsState gpsState = new GpsState();
        UiMode uiMode = UiMode.Menu;
        List<MenuEntry> storedEntries = new List<MenuEntry>();
        List<IMyGyro> cachedGyros = new List<IMyGyro>();
        string lastInput = "";

        #endregion

        #region Initialization

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            EnsureMenuData();
            BuildMenu();
        }

        void BuildMenu()
        {
            storedEntries = LoadMenuEntries();
            if (storedEntries.Count == 0)
            {
                storedEntries = GetDefaultMenuEntries();
                SaveMenuEntries(storedEntries);
            }

            var nodeMap = new Dictionary<string, MenuNode>();
            foreach (var entry in storedEntries)
            {
                nodeMap[entry.Id] = new MenuNode { Id = entry.Id, Title = entry.Title };
            }

            // Ensure root exists
            if (!nodeMap.ContainsKey("root"))
            {
                var rootEntry = new MenuEntry { Id = "root", Title = "Ship OS", ParentId = "" };
                storedEntries.Insert(0, rootEntry);
                nodeMap[rootEntry.Id] = new MenuNode { Id = rootEntry.Id, Title = rootEntry.Title };
                SaveMenuEntries(storedEntries);
            }

            foreach (var entry in storedEntries)
            {
                if (entry.Id == "root") continue;
                MenuNode node;
                if (!nodeMap.TryGetValue(entry.Id, out node)) continue;

                MenuNode parent;
                if (!nodeMap.TryGetValue(string.IsNullOrEmpty(entry.ParentId) ? "root" : entry.ParentId, out parent))
                {
                    parent = nodeMap["root"];
                }
                parent.Children.Add(node);
            }

            // Wire menu actions
            if (nodeMap.ContainsKey("gps_list")) nodeMap["gps_list"].Action = () => EnterGpsMode();
            if (nodeMap.ContainsKey("gps_export")) nodeMap["gps_export"].Action = () => ExportFilteredGpsToLcd();

            root = nodeMap["root"];
        }

        #endregion

        #region Main Loop

        public void Main(string argument)
        {
            HandleArgumentInput(argument);
            bool usingController = HandleControllerInput();
            ApplyGyroLock(usingController);
            ClampState();
            ClampGpsState();
            // Silent auto-import of GPS strings from tagged LCDs
            ImportFromInputLCDs(true);
            DrawDisplays();
        }

        #endregion

        #region Input

        // CONTROL SCHEME (in [OS]-tagged cockpit):
        // W/S or Up/Down or mouse/arrow pitch: Move selection up/down (W/Up = up, S/Down = down)
        // A/D or Left/Right or mouse/arrow yaw: Left / Right (reserved handlers)
        // C (Y-) and SPACE (Y+): Reserved vertical handlers
        // Q: Back | E: OK
        // Mouse is read for input but no cursor is drawn

        void HandleArgumentInput(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return;
            arg = arg.Trim().ToUpper();

            if (uiMode == UiMode.Gps)
            {
                HandleGpsArgumentInput(arg);
                return;
            }

            if (arg == "UP") MoveSelection(1);
            else if (arg == "DOWN") MoveSelection(-1);
            else if (arg == "LEFT") HandleLeftInput();
            else if (arg == "RIGHT") HandleRightInput();
            else if (arg == "BACK" || arg == "Q") NavigateUp();
            else if (arg == "OK" || arg == "ENTER" || arg == "E") Activate();
            else if (arg == "PGUP") Page(-1);
            else if (arg == "PGDN") Page(1);
        }

        void SetLastInput(string label)
        {
            lastInput = label;
        }

        bool HandleControllerInput()
        {
            // Use tagged controller as input source for UI
            var controller = FindMainController();
            if (controller == null || !controller.IsUnderControl) return false;

            // While in OS-tagged controller, disable ship motion so inputs are for UI
            controller.ControlThrusters = false;
            controller.ControlWheels = false;
            controller.HandBrake = true;

            Vector3 move = controller.MoveIndicator; // X=strafe, Y=up/down, Z=forward/back
            Vector2 rot = controller.RotationIndicator; // mouse/arrow rotation
            float roll = controller.RollIndicator;

            if (uiMode == UiMode.Gps)
            {
                HandleGpsControllerInput(move, rot, roll);
                return true;
            }

            if (move.Z < -0.05f) MoveSelection(-1); // W
            if (move.Z > 0.05f) MoveSelection(1); // S
            if (move.X < -0.05f) HandleLeftInput();  // A
            if (move.X > 0.05f) HandleRightInput();  // D
            if (move.Y > 0.05f) HandleYPositiveInput(); // SPACE
            if (move.Y < -0.05f) HandleYNegativeInput(); // C
            if (rot.X < -0.05f) MoveSelection(-1); // pitch up
            if (rot.X > 0.05f) MoveSelection(1);  // pitch down
            if (rot.Y < -0.05f) HandleLeftInput();
            if (rot.Y > 0.05f) HandleRightInput();
            if (roll > 0.05f) Activate(); // E
            if (roll < -0.05f) NavigateUp(); // Q

            return true;
        }

        void MoveSelection(int delta)
        {
            var items = GetCurrentItems();
            if (items.Count == 0) return;

            if (delta > 0) SetLastInput("DOWN");
            else if (delta < 0) SetLastInput("UP");

            state.Selection = Math.Max(0, Math.Min(items.Count - 1, state.Selection + delta));
            // Auto-adjust page if selection falls outside current page
            int page = state.Selection / PageSize;
            if (page != state.PageIndex) state.PageIndex = page;
        }

        // Placeholder hooks for left/right actions (extend later)
        void HandleLeftInput()
        {
            SetLastInput("LEFT");
        }

        void HandleRightInput()
        {
            SetLastInput("RIGHT");
        }

        // Placeholder hooks for vertical actions (extend later)
        void HandleYPositiveInput()
        {
            SetLastInput("SPACE");
        }

        void HandleYNegativeInput()
        {
            SetLastInput("C");
        }

        void Page(int delta)
        {
            var items = GetCurrentItems();
            int maxPage = Math.Max(0, (items.Count - 1) / PageSize);
            state.PageIndex = Math.Max(0, Math.Min(maxPage, state.PageIndex + delta));
            state.Selection = Math.Max(state.PageIndex * PageSize, Math.Min(items.Count - 1, state.Selection));
        }

        void NavigateUp()
        {
            if (state.Path.Count == 0) return;
            state.Path.RemoveAt(state.Path.Count - 1);
            state.Selection = 0;
            state.PageIndex = 0;
        }

        void Activate()
        {
            var items = GetCurrentItems();
            if (items.Count == 0) return;
            int idx = state.Selection;
            if (idx < 0 || idx >= items.Count) return;

            var node = items[idx];
            if (node.Children != null && node.Children.Count > 0)
            {
                state.Path.Add(idx);
                state.Selection = 0;
                state.PageIndex = 0;
            }
            else
            {
                node.Action?.Invoke();
            }
        }

        #endregion

        #region Helpers

        MenuNode GetCurrentNode()
        {
            MenuNode node = root;
            foreach (var idx in state.Path)
            {
                if (node.Children == null || idx < 0 || idx >= node.Children.Count) break;
                node = node.Children[idx];
            }
            return node;
        }

        List<MenuNode> GetCurrentItems()
        {
            var node = GetCurrentNode();
            return node.Children ?? new List<MenuNode>();
        }

        #endregion

        #region GPS Backend

        Color HexToColor(string hex)
        {
            hex = hex.TrimStart('#');
            byte a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            byte r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
            return new Color(r, g, b, a);
        }

        List<string> GetFactionsSection()
        {
            return GetSectionLines("[FACTIONS]");
        }

        List<string> GetEntriesSection()
        {
            return GetSectionLines("[ENTRIES]");
        }

        void SaveGpsData(List<string> factions, List<string> entries)
        {
            SetSectionLines("[FACTIONS]", factions);
            SetSectionLines("[ENTRIES]", entries);
        }

        GPSData ParseGPS(string gpsString)
        {
            // GPS format: GPS:Name:X:Y:Z:
            string[] parts = gpsString.Split(':');
            if (parts.Length < 6 || parts[0] != "GPS") return null;
            try
            {
                return new GPSData
                {
                    Name = parts[1],
                    X = double.Parse(parts[2]),
                    Y = double.Parse(parts[3]),
                    Z = double.Parse(parts[4])
                };
            }
            catch { return null; }
        }

        bool TryStoreGPS(string gpsString, bool echoResult)
        {
            var gpsData = ParseGPS(gpsString);
            if (gpsData == null)
            {
                if (echoResult) Echo("Invalid GPS format.");
                return false;
            }

            string location = $"{gpsData.X},{gpsData.Y},{gpsData.Z}";
            string roundedLocation = RoundCoordinates(location);
            var entries = GetEntriesSection();

            foreach (string entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                EntryInfo info = ParseEntry(entry);
                string roundedExisting = RoundCoordinates(info.Location);
                if (roundedExisting == roundedLocation)
                {
                    if (echoResult) Echo("Entry with this location already exists.");
                    return false;
                }
            }

            string identifier = gpsData.Name;
            bool isFaction = false;
            if (gpsData.Name.Contains(" - "))
            {
                string[] nameParts = gpsData.Name.Split(new[] { " - " }, StringSplitOptions.None);
                if (nameParts.Length >= 2)
                {
                    string potentialFaction = nameParts[0].Trim();
                    if (potentialFaction.Length >= 2 && potentialFaction.Length <= 4 && potentialFaction.All(c => char.IsUpper(c) || char.IsDigit(c)))
                    {
                        identifier = potentialFaction;
                        isFaction = true;
                    }
                }
            }

            entries.Add($"{identifier}|{roundedLocation}");

            if (isFaction)
            {
                var factions = GetFactionsSection();
                bool exists = false;
                for (int i = 0; i < factions.Count; i++)
                {
                    if (factions[i].StartsWith(identifier + "|")) { exists = true; break; }
                }
                if (!exists)
                {
                    factions.Add($"{identifier}|1"); // default Shipyard
                }
                SaveGpsData(factions, entries);
            }
            else
            {
                SaveGpsData(GetFactionsSection(), entries);
            }

            if (echoResult) Echo($"GPS stored: {identifier}");
            return true;
        }

        void StoreGPS(string gpsString)
        {
            TryStoreGPS(gpsString, true);
        }

        EntryInfo ParseEntry(string entry)
        {
            string[] fields = entry.Split('|');
            if (fields.Length == 2)
            {
                return new EntryInfo { Id = 0, Identifier = fields[0].Trim(), Location = fields[1].Trim() };
            }
            if (fields.Length >= 3)
            {
                int id;
                int.TryParse(fields[0], out id);
                return new EntryInfo { Id = id, Identifier = fields[1].Trim(), Location = fields[2].Trim() };
            }
            return new EntryInfo { Id = 0, Identifier = "", Location = "" };
        }

        bool IsFactionIdentifier(string identifier)
        {
            return identifier.Length >= 2 && identifier.Length <= 4 && identifier.All(c => char.IsUpper(c) || char.IsDigit(c));
        }

        string RoundCoordinates(string location)
        {
            string[] parts = location.Split(',');
            if (parts.Length != 3) return location;
            double x, y, z;
            if (!double.TryParse(parts[0], out x) || !double.TryParse(parts[1], out y) || !double.TryParse(parts[2], out z))
                return location;
            return $"{Math.Round(x, 2)},{Math.Round(y, 2)},{Math.Round(z, 2)}";
        }

        int ResolveTypeIndex(EntryInfo entryInfo, List<string> factions, bool isFaction)
        {
            if (!isFaction) return 0;
            foreach (string faction in factions)
            {
                string[] facFields = faction.Split('|');
                if (facFields.Length >= 2 && facFields[0] == entryInfo.Identifier)
                {
                    int typeIndex;
                    if (int.TryParse(facFields[1], out typeIndex)) return typeIndex;
                }
            }
            return 1;
        }

        Color ResolveTypeColor(int typeIndex)
        {
            if (typeColors.ContainsKey(typeIndex)) return HexToColor(typeColors[typeIndex]);
            return Color.Gray;
        }

        GPSData ParseGPSLocation(string location)
        {
            string[] parts = location.Split(',');
            if (parts.Length != 3) return new GPSData();
            double x, y, z;
            if (!double.TryParse(parts[0], out x) || !double.TryParse(parts[1], out y) || !double.TryParse(parts[2], out z)) return new GPSData();
            return new GPSData { X = x, Y = y, Z = z };
        }

        double GetDistanceTo(double x, double y, double z)
        {
            Vector3D target = new Vector3D(x, y, z);
            Vector3D myPos = Me.GetPosition();
            return Vector3D.Distance(myPos, target);
        }

        List<SortedEntry> GetSortedFilteredEntries(List<string> entries, List<string> factions)
        {
            Vector3D myPosition = Me.GetPosition();
            var result = new List<SortedEntry>();
            for (int actualIndex = 0; actualIndex < entries.Count; actualIndex++)
            {
                var entry = entries[actualIndex];
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var info = ParseEntry(entry);
                if (info.Identifier == "") continue;

                bool isFaction = IsFactionIdentifier(info.Identifier);
                int typeIndex = ResolveTypeIndex(info, factions, isFaction);

                double distance = 0;
                var gps = ParseGPSLocation(info.Location);
                distance = Vector3D.Distance(myPosition, new Vector3D(gps.X, gps.Y, gps.Z));

                bool pass = true;
                if (gpsState.FilterFaction != "ALL")
                {
                    if (gpsState.FilterFaction == "None")
                    {
                        if (isFaction) pass = false;
                    }
                    else
                    {
                        if (!isFaction || info.Identifier != gpsState.FilterFaction) pass = false;
                    }
                }

                if (gpsState.FilterType != -1)
                {
                    if (gpsState.FilterType == 0)
                    {
                        if (isFaction) pass = false;
                    }
                    else if (!isFaction || typeIndex != gpsState.FilterType)
                    {
                        pass = false;
                    }
                }

                if (gpsState.FilterRange != -1 && distance / 1000 > gpsState.FilterRange) pass = false;

                if (pass)
                {
                    result.Add(new SortedEntry { ActualIndex = actualIndex, Distance = distance, Identifier = info.Identifier });
                }
            }

            result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return result;
        }

        bool TryGetClosestSolidBody(List<string> entries, List<string> factions, Vector3D referencePos, out EntryInfo closest, out double distanceKm)
        {
            closest = new EntryInfo { Id = 0, Identifier = "", Location = "" };
            distanceKm = 0;
            bool found = false;
            double best = double.MaxValue;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var info = ParseEntry(entry);
                if (info.Identifier == "" || info.Location == "") continue;

                bool isFaction = IsFactionIdentifier(info.Identifier);
                int typeIndex = ResolveTypeIndex(info, factions, isFaction);
                if (isFaction || typeIndex != 0) continue;

                var gps = ParseGPSLocation(info.Location);
                Vector3D pos = new Vector3D(gps.X, gps.Y, gps.Z);
                if (pos == referencePos) continue;
                double dist = Vector3D.Distance(referencePos, pos);
                if (dist < best)
                {
                    best = dist;
                    closest = info;
                    found = true;
                }
            }

            if (found) distanceKm = best / 1000.0;
            return found;
        }

        void DeleteEntry(int actualIndex)
        {
            var entries = GetEntriesSection();
            if (actualIndex < 0 || actualIndex >= entries.Count) return;
            entries.RemoveAt(actualIndex);
            SaveGpsData(GetFactionsSection(), entries);
            gpsState.Selection = 0;
            Echo("Entry deleted.");
        }

        void ChangeEntryType(int actualIndex, int newTypeIndex)
        {
            var entries = GetEntriesSection();
            var factions = GetFactionsSection();
            if (actualIndex < 0 || actualIndex >= entries.Count) return;

            var entry = entries[actualIndex];
            var info = ParseEntry(entry);
            if (info.Identifier == "") return;
            if (!IsFactionIdentifier(info.Identifier))
            {
                Echo("Cannot change type: not a faction station.");
                return;
            }

            bool updated = false;
            for (int i = 0; i < factions.Count; i++)
            {
                var parts = factions[i].Split('|');
                if (parts.Length >= 2 && parts[0] == info.Identifier)
                {
                    factions[i] = $"{info.Identifier}|{newTypeIndex}";
                    updated = true;
                    break;
                }
            }
            if (!updated)
            {
                factions.Add($"{info.Identifier}|{newTypeIndex}");
            }

            SaveGpsData(factions, entries);
            Echo($"Type changed to {typeNames[newTypeIndex]} for all {info.Identifier} stations.");
        }

        void ExportToLCD(int actualIndex)
        {
            var entries = GetEntriesSection();
            var factions = GetFactionsSection();
            if (actualIndex < 0 || actualIndex >= entries.Count) return;

            var info = ParseEntry(entries[actualIndex]);
            if (info.Identifier == "" || info.Location == "") return;
            string[] coords = info.Location.Split(',');
            if (coords.Length != 3) return;

            string displayName = GetFullDisplayName(info, factions);
            string colorHex = GetColorForEntry(info, factions);
            string gpsString = $"GPS:{displayName}:{coords[0]}:{coords[1]}:{coords[2]}:{colorHex}:";

            var lcds = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType(lcds, lcd => lcd.CustomName.Contains("[GPS-EXPORT]"));
            if (lcds.Count > 0)
            {
                lcds[0].WriteText(gpsString);
                Echo($"GPS exported to LCD: {displayName}");
            }
            else
            {
                Echo("No LCD with [GPS-EXPORT] tag found.");
            }
        }

        void ExportFilteredGpsToLcd()
        {
            var entries = GetEntriesSection();
            var factions = GetFactionsSection();
            var sorted = GetSortedFilteredEntries(entries, factions);

            if (sorted.Count == 0)
            {
                Echo("No GPS entries to export with current filters.");
                return;
            }

            var lcds = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType(lcds, lcd => lcd.CustomName.Contains("[GPS-EXPORT]"));
            if (lcds.Count == 0)
            {
                Echo("No LCD with [GPS-EXPORT] tag found.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var entry in sorted)
            {
                var info = ParseEntry(entries[entry.ActualIndex]);
                if (info.Identifier == "" || info.Location == "") continue;
                string[] coords = info.Location.Split(',');
                if (coords.Length != 3) continue;

                string displayName = GetFullDisplayName(info, factions);
                string colorHex = GetColorForEntry(info, factions);
                string gpsString = $"GPS:{displayName}:{coords[0]}:{coords[1]}:{coords[2]}:{colorHex}:";
                sb.AppendLine(gpsString);
            }

            lcds[0].WriteText(sb.ToString());
            Echo($"Exported {sorted.Count} GPS entries to [GPS-EXPORT] LCD.");
        }

        void ImportFromInputLCDs(bool silent)
        {
            var lcds = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType(lcds, lcd => lcd.CustomName.Contains("[GPS-IMPORT]"));
            if (lcds.Count == 0)
            {
                if (!silent) Echo("No LCD with [GPS-IMPORT] tag found.");
                return;
            }

            int added = 0;
            int parsed = 0;
            foreach (var lcd in lcds)
            {
                string text = lcd.GetText();
                if (string.IsNullOrWhiteSpace(text)) continue;
                string[] lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    int idx = 0;
                    while (idx < line.Length)
                    {
                        int gpsStart = line.IndexOf("GPS:", idx);
                        if (gpsStart < 0) break;

                        // Find the end of this GPS entry (ends with :)
                        int gpsEnd = gpsStart + 4; // start after "GPS:"
                        int colonCount = 1; // already found first colon after GPS
                        while (gpsEnd < line.Length && colonCount < 6)
                        {
                            if (line[gpsEnd] == ':') colonCount++;
                            gpsEnd++;
                        }

                        // Extract GPS string (include trailing colon if present)
                        string candidate = line.Substring(gpsStart, gpsEnd - gpsStart);
                        parsed++;
                        if (TryStoreGPS(candidate, false)) added++;

                        idx = gpsEnd;
                    }
                }
                lcd.WriteText("");
            }

            if (!silent || parsed > 0) Echo($"Imported {added}/{parsed} GPS from input LCDs.");
        }

        void SetAIDestination(int actualIndex)
        {
            var entries = GetEntriesSection();
            var factions = GetFactionsSection();
            if (actualIndex < 0 || actualIndex >= entries.Count) return;
            var info = ParseEntry(entries[actualIndex]);
            if (info.Identifier == "" || info.Location == "") return;

            string[] coords = info.Location.Split(',');
            if (coords.Length != 3) return;
            double x, y, z;
            if (!double.TryParse(coords[0], out x) || !double.TryParse(coords[1], out y) || !double.TryParse(coords[2], out z))
            {
                Echo("Invalid GPS coordinates.");
                return;
            }

            string displayName = GetFullDisplayName(info, factions);
            Vector3D destination = new Vector3D(x, y, z);

            var rcBlocks = new List<IMyRemoteControl>();
            GridTerminalSystem.GetBlocksOfType(rcBlocks);
            if (rcBlocks.Count > 0)
            {
                rcBlocks[0].ClearWaypoints();
                rcBlocks[0].AddWaypoint(destination, displayName);
                rcBlocks[0].SetAutoPilotEnabled(true);
                Echo($"Destination set: {displayName}");
            }
            else
            {
                Echo("No Remote Control found.");
            }
        }

        string GetFullDisplayName(EntryInfo entryInfo, List<string> factions)
        {
            string identifier = entryInfo.Identifier;
            bool isFaction = IsFactionIdentifier(identifier);
            if (isFaction)
            {
                int typeIndex = ResolveTypeIndex(entryInfo, factions, isFaction);
                string typeName = typeNames.ContainsKey(typeIndex) ? typeNames[typeIndex] : "Station";
                return identifier + " " + typeName;
            }
            return identifier;
        }

        string GetColorForEntry(EntryInfo entryInfo, List<string> factions)
        {
            string identifier = entryInfo.Identifier;
            bool isFaction = IsFactionIdentifier(identifier);
            int typeIndex = isFaction ? ResolveTypeIndex(entryInfo, factions, true) : 0;
            string colorHex;
            if (!typeColors.TryGetValue(typeIndex, out colorHex)) colorHex = "#FFFFFFFF";
            return colorHex;
        }

        void CycleFilter(int filterIndex)
        {
            var factions = GetFactionsSection();

            if (filterIndex == 0)
            {
                var factionOptions = new List<string> { "ALL" };
                foreach (string faction in factions)
                {
                    string[] fields = faction.Split('|');
                    if (fields.Length > 0 && !factionOptions.Contains(fields[0])) factionOptions.Add(fields[0]);
                }
                factionOptions.Add("None");
                int current = factionOptions.IndexOf(gpsState.FilterFaction);
                current = (current + 1) % factionOptions.Count;
                gpsState.FilterFaction = factionOptions[current];
                gpsState.PageIndex = 0;
            }
            else if (filterIndex == 1)
            {
                var typeOptions = new List<int> { -1 };
                typeOptions.AddRange(typeNames.Keys);
                int current = typeOptions.IndexOf(gpsState.FilterType);
                current = (current + 1) % typeOptions.Count;
                gpsState.FilterType = typeOptions[current];
                gpsState.PageIndex = 0;
            }
            else if (filterIndex == 2)
            {
                var rangeOptions = new List<int> { -1, 500, 1000, 2000, 4000, 8000 };
                int current = rangeOptions.IndexOf(gpsState.FilterRange);
                current = (current + 1) % rangeOptions.Count;
                gpsState.FilterRange = rangeOptions[current];
                gpsState.PageIndex = 0;
            }
        }

        #endregion

        #region CustomData & Section Management

        Dictionary<string, List<string>> ParseSections(string data)
        {
            var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string current = "";
            sections[current] = new List<string>();

            var lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = line;
                    if (!sections.ContainsKey(current)) sections[current] = new List<string>();
                    continue;
                }
                if (!sections.ContainsKey(current)) sections[current] = new List<string>();
                sections[current].Add(line);
            }
            return sections;
        }

        string BuildSections(Dictionary<string, List<string>> sections)
        {
            var sb = new System.Text.StringBuilder();
            // Always write unnamed section first if it has content
            if (sections.ContainsKey("") && sections[""] != null)
            {
                foreach (var line in sections[""]) sb.AppendLine(line);
                if (sections.Count > 1) sb.AppendLine();
            }

            foreach (var kvp in sections)
            {
                if (kvp.Key == "") continue;
                sb.AppendLine(kvp.Key);
                var list = kvp.Value ?? new List<string>();
                foreach (var line in list) sb.AppendLine(line);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        List<string> GetSectionLines(string tag)
        {
            var sections = ParseSections(Me.CustomData ?? "");
            List<string> lines;
            if (!sections.TryGetValue(tag, out lines)) lines = new List<string>();
            return lines;
        }

        void SetSectionLines(string tag, List<string> lines)
        {
            var sections = ParseSections(Me.CustomData ?? "");
            sections[tag] = lines ?? new List<string>();
            Me.CustomData = BuildSections(sections);
        }

        List<MenuEntry> LoadMenuEntries()
        {
            var entries = new List<MenuEntry>();
            var section = GetSectionLines(MenuSectionTag);
            foreach (var line in section)
            {
                var parts = line.Split('|');
                if (parts.Length < 3) continue;
                entries.Add(new MenuEntry
                {
                    Id = parts[0].Trim(),
                    Title = parts[1].Trim(),
                    ParentId = parts[2].Trim()
                });
            }
            return entries;
        }

        void SaveMenuEntries(List<MenuEntry> entries)
        {
            var lines = new List<string>();
            foreach (var e in entries)
            {
                lines.Add($"{e.Id}|{e.Title}|{e.ParentId}");
            }
            SetSectionLines(MenuSectionTag, lines);
        }

        #endregion

        #region Menu Data Management

        void EnsureMenuData()
        {
            var entries = LoadMenuEntries();
            if (entries.Count == 0)
            {
                entries = GetDefaultMenuEntries();
                SaveMenuEntries(entries);
            }
        }

        List<MenuEntry> GetDefaultMenuEntries()
        {
            return new List<MenuEntry>
    {
        new MenuEntry { Id = "root", Title = "Ship OS", ParentId = "" },
        new MenuEntry { Id = "gps", Title = "GPS Manager", ParentId = "root" },
        new MenuEntry { Id = "gps_list", Title = "View list", ParentId = "gps" },
        new MenuEntry { Id = "gps_export", Title = "Export list", ParentId = "gps" }
    };
        }

        #endregion

        #region Gyro & Controller Utilities

        void ClampState()
        {
            var items = GetCurrentItems();
            if (state.Selection >= items.Count) state.Selection = Math.Max(0, items.Count - 1);
            int maxPage = items.Count == 0 ? 0 : (items.Count - 1) / PageSize;
            if (state.PageIndex > maxPage) state.PageIndex = maxPage;
        }

        IMyShipController FindMainController()
        {
            var controllers = new List<IMyShipController>();
            GridTerminalSystem.GetBlocksOfType(controllers, c => c.IsSameConstructAs(Me));
            if (controllers.Count == 0) return null;

            // Require a tagged controller for OS input; otherwise no controller control
            var tagged = controllers.Find(c => c.CustomName.Contains(DisplayTag));
            return tagged; // may be null if none tagged
        }

        void ApplyGyroLock(bool engage)
        {
            EnsureGyros();
            for (int i = 0; i < cachedGyros.Count; i++)
            {
                var g = cachedGyros[i];
                g.GyroOverride = engage;
                if (engage)
                {
                    g.Pitch = 0f;
                    g.Yaw = 0f;
                    g.Roll = 0f;
                }
            }
        }

        void EnsureGyros()
        {
            // Refresh if empty or blocks removed
            if (cachedGyros.Count == 0)
            {
                GridTerminalSystem.GetBlocksOfType(cachedGyros, g => g.IsSameConstructAs(Me));
            }
        }

        #endregion

        #region Rendering

        void DrawDisplays()
        {
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, b => b.CustomName.Contains(DisplayTag));

            foreach (var block in blocks)
            {
                var surf = block as IMyTextSurface;
                if (surf != null)
                {
                    DrawSurface(surf);
                    continue;
                }

                var cockpit = block as IMyCockpit;
                if (cockpit != null && cockpit.SurfaceCount > 0)
                {
                    DrawSurface(cockpit.GetSurface(0));
                }
            }
        }

        void DrawSurface(IMyTextSurface surface)
        {
            surface.ContentType = ContentType.SCRIPT;
            surface.Script = "";
            var viewport = new RectangleF((surface.TextureSize - surface.SurfaceSize) / 2f, surface.SurfaceSize);
            var frame = surface.DrawFrame();

            if (uiMode == UiMode.Gps)
            {
                DrawGps(frame, viewport, surface);
            }
            else
            {
                DrawMain(frame, viewport, surface);
            }

            frame.Dispose();
        }

        void DrawMain(MySpriteDrawFrame frame, RectangleF vp, IMyTextSurface surface)
        {
            var items = GetCurrentItems();
            int start = state.PageIndex * PageSize;
            int end = Math.Min(items.Count, start + PageSize);

            // Measure text height once for alignment (SpriteType.TEXT uses top-left origin)
            float measuredLineHeight = surface.MeasureStringInPixels(new StringBuilder("Ag"), "White", TextScale).Y;
            float headerHeight = measuredLineHeight;
            float rowHeight = measuredLineHeight;

            float y = vp.Y + 22f;
            float headerBaseline = y; // text is drawn on this baseline
            DrawBar(frame, vp, headerBaseline + headerHeight * 0.5f, headerHeight, Highlight);
            DrawText(frame, vp, BuildBreadcrumb(), headerBaseline, TextScale, TextOnHighlight, TextAlignment.CENTER);
            y += 26f; // header line height + gap

            for (int i = start; i < end; i++)
            {
                bool sel = (i == state.Selection);
                float lineY = y + (i - start) * 22f;
                var node = items[i];
                string suffix = (node.Children != null && node.Children.Count > 0) ? " ..." : "";
                if (sel)
                {
                    DrawBar(frame, vp, lineY + rowHeight * 0.5f, rowHeight, Highlight);
                }
                DrawText(frame, vp, node.Title + suffix, lineY, TextScale, sel ? TextOnHighlight : Dim, TextAlignment.LEFT, 8f);
            }

            // Footer: page indicator
            y = vp.Y + vp.Height - 28f;
            string pageInfo = items.Count == 0 ? "0/0" : ($"{state.PageIndex + 1}/{Math.Max(1, (items.Count + PageSize - 1) / PageSize)}");
            DrawText(frame, vp, pageInfo, y, TextScale, Accent, TextAlignment.RIGHT, -12f);

            // Debug: last input label (left side)
            if (!string.IsNullOrEmpty(lastInput))
            {
                DrawText(frame, vp, lastInput, y, TextScale, Dim, TextAlignment.LEFT, 10f);
            }

        }

        void DrawGps(MySpriteDrawFrame frame, RectangleF vp, IMyTextSurface surface)
        {
            List<string> entries; List<string> factions; List<SortedEntry> sorted; int start; int end;
            GetGpsPageData(out entries, out factions, out sorted, out start, out end);

            float measuredLineHeight = surface.MeasureStringInPixels(new StringBuilder("Ag"), "White", TextScale).Y;
            float rowHeight = measuredLineHeight;
            float footerY = vp.Y + vp.Height - 28f;

            float y = vp.Y + 22f;

            // Type change overlay
            if (gpsState.InTypeChange)
            {
                DrawBar(frame, vp, y + rowHeight * 0.5f, rowHeight, Highlight);
                DrawText(frame, vp, "Select Type", y, TextScale, TextOnHighlight, TextAlignment.CENTER);
                y += 26f;

                List<int> validTypes = new List<int>(typeNames.Keys); validTypes.Sort();
                for (int i = 0; i < validTypes.Count; i++)
                {
                    bool sel = i == gpsState.TypeSelectionIndex;
                    int typeIndex = validTypes[i];
                    Color c = ResolveTypeColor(typeIndex);
                    float lineY = y + i * 22f;
                    if (sel) DrawBar(frame, vp, lineY + rowHeight * 0.5f, rowHeight, c);
                    DrawText(frame, vp, typeNames[typeIndex], lineY, TextScale, sel ? TextOnHighlight : c, TextAlignment.LEFT, 8f);
                }
                return;
            }

            // Detail view - show GPS info and actions
            if (gpsState.InDetailView)
            {
                if (gpsState.DetailEntryIndex < 0 || gpsState.DetailEntryIndex >= entries.Count) { ExitDetailView(); return; }

                var info = ParseEntry(entries[gpsState.DetailEntryIndex]);
                bool isFaction = IsFactionIdentifier(info.Identifier);
                int typeIndex = ResolveTypeIndex(info, factions, isFaction);
                Color typeColor = ResolveTypeColor(typeIndex);
                string typeName = typeNames[typeIndex];
                var gpsLoc = ParseGPSLocation(info.Location);
                double distance = Vector3D.Distance(Me.GetPosition(), new Vector3D(gpsLoc.X, gpsLoc.Y, gpsLoc.Z));

                // Header
                DrawBar(frame, vp, y + rowHeight * 0.5f, rowHeight, typeColor);
                DrawText(frame, vp, info.Identifier, y, TextScale, TextOnHighlight, TextAlignment.CENTER);
                y += 26f;

                // GPS Info
                DrawText(frame, vp, $"Type: {typeName}", y, TextScale, Header, TextAlignment.LEFT, 8f); y += 22f;
                DrawText(frame, vp, $"Distance: {distance / 1000:F1} km", y, TextScale, Header, TextAlignment.LEFT, 8f); y += 22f;
                EntryInfo closestBody;
                double closestBodyKm;
                Vector3D entryPos = new Vector3D(gpsLoc.X, gpsLoc.Y, gpsLoc.Z);
                if (TryGetClosestSolidBody(entries, factions, entryPos, out closestBody, out closestBodyKm))
                {
                    DrawText(frame, vp, $"Closest solid body: {closestBody.Identifier} ({closestBodyKm:F1} km)", y, TextScale, Header, TextAlignment.LEFT, 8f); y += 22f;
                }
                else
                {
                    DrawText(frame, vp, "Closest solid body: N/A", y, TextScale, Header, TextAlignment.LEFT, 8f); y += 22f;
                }
                DrawText(frame, vp, $"X: {gpsLoc.X:F0} Y: {gpsLoc.Y:F0} Z: {gpsLoc.Z:F0}", y, TextScale, Dim, TextAlignment.LEFT, 8f); y += 26f;


                // Actions menu
                DrawText(frame, vp, "Actions:", y, TextScale, Header, TextAlignment.LEFT, 8f); y += 22f;
                int optionIndex = 0;
                bool sel0 = gpsState.DetailSelection == optionIndex;
                if (sel0) DrawBar(frame, vp, y + rowHeight * 0.5f, rowHeight, Highlight);
                DrawText(frame, vp, "Export to LCD", y, TextScale, sel0 ? TextOnHighlight : Color.White, TextAlignment.LEFT, 8f); y += 22f; optionIndex++;

                bool sel1 = gpsState.DetailSelection == optionIndex;
                if (sel1) DrawBar(frame, vp, y + rowHeight * 0.5f, rowHeight, Highlight);
                DrawText(frame, vp, "Set AI Destination", y, TextScale, sel1 ? TextOnHighlight : Color.White, TextAlignment.LEFT, 8f); y += 22f; optionIndex++;

                if (isFaction)
                {
                    bool sel2 = gpsState.DetailSelection == optionIndex;
                    if (sel2) DrawBar(frame, vp, y + rowHeight * 0.5f, rowHeight, Highlight);
                    DrawText(frame, vp, "Change Type", y, TextScale, sel2 ? TextOnHighlight : Color.White, TextAlignment.LEFT, 8f); y += 22f; optionIndex++;
                }

                bool selDel = gpsState.DetailSelection == optionIndex;
                if (selDel) DrawBar(frame, vp, y + rowHeight * 0.5f, rowHeight, new Color(255, 100, 100));
                DrawText(frame, vp, "Delete Entry", y, TextScale, selDel ? TextOnHighlight : new Color(255, 150, 150), TextAlignment.LEFT, 8f);

                // Footer
                DrawText(frame, vp, "Q: Back", footerY, TextScale, Accent, TextAlignment.LEFT, 10f);
                if (!string.IsNullOrEmpty(lastInput)) DrawText(frame, vp, lastInput, footerY, TextScale, Dim, TextAlignment.RIGHT, -12f);
                return;
            }

            // List view - show GPS entries
            DrawBar(frame, vp, y + rowHeight * 0.5f, rowHeight, Highlight);
            DrawText(frame, vp, $"GPS [{sorted.Count}]", y, TextScale, TextOnHighlight, TextAlignment.CENTER);
            y += 26f;

            int displayed = end - start;
            for (int i = start; i < end; i++)
            {
                int local = i - start;
                bool sel = (local == gpsState.Selection);
                float lineY = y + local * 22f;

                var info = ParseEntry(entries[sorted[i].ActualIndex]);
                bool isFaction = IsFactionIdentifier(info.Identifier);
                int typeIndex = ResolveTypeIndex(info, factions, isFaction);
                Color barColor = ResolveTypeColor(typeIndex);
                string suffix = isFaction ? $" ({typeNames[typeIndex]})" : "";
                string text = $"{info.Identifier} - {sorted[i].Distance / 1000:F1} km{suffix}";

                if (sel) DrawBar(frame, vp, lineY + rowHeight * 0.5f, rowHeight, barColor);
                DrawText(frame, vp, text, lineY, TextScale, sel ? TextOnHighlight : barColor, TextAlignment.LEFT, 8f);
            }

            // Footer - highlighted bar with inline filters (selected filter in brackets)
            DrawBar(frame, vp, footerY + rowHeight * 0.5f, rowHeight, Highlight);
            string typeText = gpsState.FilterType == -1 ? "ALL" : typeNames[gpsState.FilterType];
            string rangeText = gpsState.FilterRange == -1 ? "ALL" : (gpsState.FilterRange + "km");

            string factionPart = gpsState.FilterSelection == 0 ? $"[Faction:{gpsState.FilterFaction}]" : $"Faction:{gpsState.FilterFaction}";
            string typePart = gpsState.FilterSelection == 1 ? $"[Type:{typeText}]" : $"Type:{typeText}";
            string rangePart = gpsState.FilterSelection == 2 ? $"[Range:{rangeText}]" : $"Range:{rangeText}";

            string filterText = $"{factionPart} | {typePart} | {rangePart}";
            DrawText(frame, vp, filterText, footerY, TextScale, TextOnHighlight, TextAlignment.LEFT, 8f);

            string pageInfo = sorted.Count == 0 ? "0/0" : ($"{gpsState.PageIndex + 1}/{Math.Max(1, (sorted.Count + GpsPageSize - 1) / GpsPageSize)}");
            DrawText(frame, vp, pageInfo, footerY, TextScale, TextOnHighlight, TextAlignment.RIGHT, -8f);
        }

        string BuildBreadcrumb()
        {
            var node = root;
            var parts = new List<string> { node.Title };
            foreach (var idx in state.Path)
            {
                if (node.Children == null || idx < 0 || idx >= node.Children.Count) break;
                node = node.Children[idx];
                parts.Add(node.Title);
            }
            return string.Join(" / ", parts);
        }

        void DrawText(MySpriteDrawFrame frame, RectangleF vp, string text, float y, float scale, Color color, TextAlignment align, float xOffset = 0f)
        {
            Vector2 pos;
            if (align == TextAlignment.CENTER)
            {
                pos = new Vector2(vp.X + vp.Width / 2 + xOffset, y);
            }
            else if (align == TextAlignment.RIGHT)
            {
                pos = new Vector2(vp.X + vp.Width - 10 + xOffset, y);
            }
            else
            {
                pos = new Vector2(vp.X + 10 + xOffset, y);
            }

            var sprite = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = pos,
                RotationOrScale = scale,
                Color = color,
                Alignment = align,
                FontId = "White"
            };
            frame.Add(sprite);
        }

        void DrawBar(MySpriteDrawFrame frame, RectangleF vp, float y, float height, Color color)
        {
            var sprite = new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(vp.X + vp.Width / 2f, y),
                Size = new Vector2(vp.Width - 8f, height),
                Color = color,
                Alignment = TextAlignment.CENTER
            };
            frame.Add(sprite);
        }

        float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        #endregion

        #region Data Structures

        class GPSData
        {
            public string Name { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        struct EntryInfo
        {
            public int Id { get; set; }
            public string Identifier { get; set; }
            public string Location { get; set; }
        }

        class SortedEntry
        {
            public int ActualIndex { get; set; }
            public double Distance { get; set; }
            public string Identifier { get; set; }
        }

        #endregion

    }
}