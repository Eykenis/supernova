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
            public const string HealthPanel = "HUD Canvas/Health Panel";
            public const string HealthFill = "HUD Canvas/Health Panel/Track/Fill";
            public const string HealthTitle = "HUD Canvas/Health Panel/Header/Title";
            public const string HealthValue = "HUD Canvas/Health Panel/Header/Value";
            public const string HealthTrack = "Track";
            public const string Hotbar = "HUD Canvas/Hotbar";
            public const string CrosshairCanvas = "Crosshair Canvas";
            public const string Crosshair = "Crosshair Canvas/Crosshair";
            public const string Horizontal = "Horizontal";
            public const string Vertical = "Vertical";
            public const string Item = "Item";
            public const string HealthHeaderTitle = "Header/Title";
            public const string HealthHeaderValue = "Header/Value";
            public const string HealthFrame = HealthPanel + "/" + Decoration.Frame;
            public const string CrosshairHorizontal = Crosshair + "/Horizontal";
            public const string CrosshairVertical = Crosshair + "/Vertical";

            public static string HotbarSlot(int oneBasedIndex)
            {
                return Hotbar + "/Slot " + oneBasedIndex;
            }

            public static string SlotItem(int oneBasedIndex)
            {
                return "Slot " + oneBasedIndex + "/" + Item;
            }

            public static string SlotKey(int oneBasedIndex)
            {
                return "Slot " + oneBasedIndex + "/Key";
            }

            public static string SlotFrame(int oneBasedIndex)
            {
                return "Slot " + oneBasedIndex + "/" + Decoration.Frame;
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
            public const string Core = "Core";
        }

        public static class Decoration
        {
            public const string Frame = "__SciFi Frame";
            public const string Telemetry = "__SciFi Telemetry";
            public const string Center = "__SciFi Center";
        }
    }
}
