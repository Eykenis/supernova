using Supernova.Gameplay;

namespace Supernova.UI
{
    public static class UiHierarchyPaths
    {
        public static class MainMenu
        {
            public const string Backdrop = "Backdrop";
            public const string Hero = "Safe Area/Hero";
            public const string ExpeditionControl = "Safe Area/Expedition Control";
            public const string BeginDescent =
                "Safe Area/Expedition Control/Main Panel/Begin Descent";
            public const string SystemSettings =
                "Safe Area/Expedition Control/Main Panel/System Settings";
            public const string LeaveExpedition =
                "Safe Area/Expedition Control/Main Panel/Leave Expedition";
            public const string Return =
                "Safe Area/Expedition Control/Settings Panel/Return";
            public const string FullscreenBackground =
                "Safe Area/Expedition Control/Settings Panel/Fullscreen/Background";
            public const string MasterVolumeBackground =
                "Safe Area/Expedition Control/Settings Panel/Master Volume/Background";
            public const string ExpeditionFrame =
                ExpeditionControl + "/" + Decoration.Frame;

        }

        public static class Hud
        {
            public const string RootCanvas = "HUD Canvas";
            public const string CompassName = "Compass";
            public const string CompassViewportName = "Viewport";
            public const string CompassTicksName = "Ticks";
            public const string CompassHeadingName = "Current Heading";
            public const string CompassBearingMarkerName = "Bearing Marker";
            public const string CompassBearingRuleName = "Bearing Rule";
            public const string Compass = RootCanvas + "/" + CompassName;
            public const string CompassViewport =
                Compass + "/" + CompassViewportName;
            public const string CompassTicks =
                CompassViewport + "/" + CompassTicksName;
            public const string CompassHeading =
                Compass + "/" + CompassHeadingName;
            public const string CompassBearingMarker =
                Compass + "/" + CompassBearingMarkerName;
            public const string CompassBearingRule =
                Compass + "/" + CompassBearingRuleName;
            public const string CompassTickLine = "Line";
            public const string CompassTickLabel = "Label";
            public const string CompassTickPrefix = "Tick ";
            public const string HealthPanel = "HUD Canvas/Health Panel";
            public const string HealthFill = "HUD Canvas/Health Panel/Track/Fill";
            public const string HealthTitle = "HUD Canvas/Health Panel/Header/Title";
            public const string HealthValue = "HUD Canvas/Health Panel/Header/Value";
            public const string HealthHeader = "Header";
            public const string HealthTrack = "Track";
            public const string HealthSegmentsName = "Segments";
            public const string HealthSegments =
                HealthPanel + "/" + HealthTrack + "/" + HealthSegmentsName;
            public const string HealthSegmentPrefix = "Segment ";
            public const string Hotbar = "HUD Canvas/Hotbar";
            public const string CrosshairCanvas = "Crosshair Canvas";
            public const string Crosshair = "Crosshair Canvas/Crosshair";
            public const string Horizontal = "Horizontal";
            public const string Vertical = "Vertical";
            public const string Item = "Item";
            public const string Key = "Key";
            public const string AngledSurface = "Angled Surface";
            public const string HealthHeaderTitle = "Header/Title";
            public const string HealthHeaderValue = "Header/Value";
            public const string HealthFrame = HealthPanel + "/" + Decoration.Frame;
            public const string CrosshairHorizontal = Crosshair + "/Horizontal";
            public const string CrosshairVertical = Crosshair + "/Vertical";

            public static string HotbarSlot(int oneBasedIndex)
            {
                return Hotbar + "/" + SlotName(oneBasedIndex);
            }

            public static string SlotName(int oneBasedIndex)
            {
                return "Slot " + oneBasedIndex;
            }

            public static string SlotItem(int oneBasedIndex)
            {
                return SlotName(oneBasedIndex) + "/" + Item;
            }

            public static string SlotKey(int oneBasedIndex)
            {
                return SlotName(oneBasedIndex) + "/" + Key;
            }

            public static string SlotFrame(int oneBasedIndex)
            {
                return SlotName(oneBasedIndex) + "/" + Decoration.Frame;
            }

            public static string SlotAngledSurface(int oneBasedIndex)
            {
                return SlotName(oneBasedIndex) + "/" + AngledSurface;
            }

            public static string HotbarSlotAngledSurface(int oneBasedIndex)
            {
                return HotbarSlot(oneBasedIndex) + "/" + AngledSurface;
            }

            public static string HealthSegment(int oneBasedIndex)
            {
                return HealthSegments + "/" + HealthSegmentPrefix + oneBasedIndex;
            }

            public static string CompassTickName(int oneBasedIndex)
            {
                return CompassTickPrefix + oneBasedIndex.ToString("00");
            }

            public static string HotbarSlotFrame(int oneBasedIndex)
            {
                return HotbarSlot(oneBasedIndex) + "/" + Decoration.Frame;
            }

        }

        public static class Pause
        {
            public const string Canvas = "Pause Canvas";
            public const string Panel = "Pause Canvas/Pause Panel";
            public const string Menu = "Menu";
            public const string FullMenu = "Pause Canvas/Pause Panel/Menu";
            public const string Resume = "Resume";
            public const string FullResume = "Pause Canvas/Pause Panel/Menu/Resume";
            public const string BackSlot = "Back Slot";
            public const string LoadoutHeader = "Loadout Header";
            public const string QuickSlots = "Quick Slots";
            public const string BackpackHeader = "Backpack Header";
            public const string Backpack = "Backpack";
            public const string ClearSlot = "Clear Slot";
            public const string SlotItem = "Item";
            public const string FullQuickSlots =
                "Pause Canvas/Pause Panel/Menu/Quick Slots";
            public const string FullBackpack =
                "Pause Canvas/Pause Panel/Menu/Backpack";
            public const string FullBackSlot =
                "Pause Canvas/Pause Panel/Menu/Back Slot";
            public const string Title = "Title";
            public const string Label = "Label";
            public const string EquipmentName = "Equipment Name";
            public const string State = "State";
            public const string Hint = "Hint";
            public const string SlotName = "Slot Name";
            public const string MenuResume = Menu + "/" + Resume;
            public const string MenuBackSlot = Menu + "/" + BackSlot;
            public const string MenuFrame = Menu + "/" + Decoration.Frame;

            public static string QuickSlotName(int oneBasedIndex)
            {
                return "Quick Slot " + oneBasedIndex;
            }

            public static string BackpackItemName(PlayerInventoryItem item)
            {
                return "Backpack Item " + (int)item;
            }
        }

        public static class Loading
        {
            public const string Canvas = "Loading Canvas";
            public const string Panel = "Loading Canvas/Loading Panel";
            public const string Content = "Loading Canvas/Loading Panel/Content";
            public const string Spinner =
                "Loading Canvas/Loading Panel/Content/Spinner";
            public const string ProgressTrack =
                "Loading Canvas/Loading Panel/Content/Progress Track";
            public const string ProgressFill =
                "Loading Canvas/Loading Panel/Content/Progress Track/Fill";
            public const string Status =
                "Loading Canvas/Loading Panel/Content/Status";
            public const string Progress =
                "Loading Canvas/Loading Panel/Content/Progress";
            public const string Brand = "Content/Brand";
            public const string Title = "Content/Title";
            public const string LocalStatus = "Content/Status";
            public const string LocalProgress = "Content/Progress";
            public const string Hint = "Content/Hint";
            public const string LocalSpinner = "Content/Spinner";
            public const string LocalProgressTrack = "Content/Progress Track";
            public const string LocalProgressFill = "Content/Progress Track/Fill";
            public const string Core = "Core";
        }

        public static class Mission
        {
            public const string Root = "HUD Canvas/Mission";
            public const string Objective = Root + "/Objective";
            public const string Prompt = Root + "/Prompt";
            public const string Timer = Root + "/Mission Timer";
            public const string TimerCaption = Timer + "/Caption";
            public const string TimerValue = Timer + "/Value";
            public const string TimerRule = Timer + "/Rule";
            public const string OverlayCanvas = "Mission Overlay Canvas";
            public const string ResultPanel =
                OverlayCanvas + "/Mission Result";
            public const string ResultText =
                ResultPanel + "/Result Text";
            public const string SceneFade =
                OverlayCanvas + "/Scene Fade";
        }

        public static class SpawnIndicator
        {
            public const string RuntimeRoot = "Spawn Point Indicator UI";
            public const string CanvasName = "Spawn Indicator Canvas";
            public const string MarkerName = "Marker";
            public const string ArrowName = "Arrow";
            public const string DistanceName = "Distance";
            public const string Canvas = CanvasName;
            public const string Marker = Canvas + "/" + MarkerName;
            public const string Arrow = Marker + "/" + ArrowName;
            public const string Distance = Marker + "/" + DistanceName;
        }

        public static class Decoration
        {
            public const string Frame = "__SciFi Frame";
            public const string Telemetry = "__SciFi Telemetry";
            public const string Center = "__SciFi Center";
        }
    }

    public static class UiLayerNames
    {
        public const string PausePortrait = "Pause Portrait";
    }
}
