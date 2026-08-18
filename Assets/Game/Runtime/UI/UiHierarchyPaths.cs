namespace Supernova.UI
{
    public static class UiHierarchyPaths
    {
        public static class MainMenu
        {
            public const string SceneRoot = "Home Main Menu";
            public const string AngledSurface = "Angled Surface";
            public const string Backdrop = "Backdrop";
            public const string Title = "Safe Area/Title";
            public const string HeaderDivider = "Safe Area/Header/Divider";
            public const string FooterDivider = "Safe Area/Footer/Divider";
            public const string Hero = "Safe Area/Hero";
            public const string ExpeditionControl = "Safe Area/Expedition Control";
            public const string Overline = ExpeditionControl + "/Overline";
            public const string ContinueGame =
                "Safe Area/Expedition Control/Main Panel/Continue Game";
            public const string ContinueGameSaveSummary =
                ContinueGame + "/Save Summary";
            public const string NewGame =
                "Safe Area/Expedition Control/Main Panel/New Game";
            public const string BeginDescent = NewGame;
            public const string BeginDescentLabel = BeginDescent + "/Label";
            public const string Tutorial =
                "Safe Area/Expedition Control/Main Panel/Tutorial";
            public const string TutorialLabel = Tutorial + "/Label";
            public const string SystemSettings =
                "Safe Area/Expedition Control/Main Panel/System Settings";
            public const string SystemSettingsLabel = SystemSettings + "/Label";
            public const string LeaveExpedition =
                "Safe Area/Expedition Control/Main Panel/Leave Expedition";
            public const string LeaveExpeditionLabel = LeaveExpedition + "/Label";
            public const string Return =
                "Safe Area/Expedition Control/Settings Panel/Return";
            public const string OverwriteConfirmation =
                ExpeditionControl + "/Overwrite Confirmation";
            public const string OverwriteConfirm =
                OverwriteConfirmation + "/Dialog/Confirm";
            public const string OverwriteCancel =
                OverwriteConfirmation + "/Dialog/Cancel";
            public const string FullscreenBackground =
                "Safe Area/Expedition Control/Settings Panel/Fullscreen/Background";
            public const string FullscreenCheckmark =
                "Safe Area/Expedition Control/Settings Panel/Fullscreen/Background/Checkmark";
            public const string MasterVolumeBackground =
                "Safe Area/Expedition Control/Settings Panel/Master Volume/Background";
            public const string MasterVolumeFill =
                "Safe Area/Expedition Control/Settings Panel/Master Volume/Fill Area/Fill";
            public const string MasterVolumeHandle =
                "Safe Area/Expedition Control/Settings Panel/Master Volume/Handle Slide Area/Handle";
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
            public const string MagnetForceName = "Magnet Force";
            public const string MagnetForce =
                RootCanvas + "/" + MagnetForceName;
            public const string HealthHeader = "Header";
            public const string HealthTrack = "Track";
            public const string HealthSegmentsName = "Segments";
            public const string HealthSegments =
                HealthPanel + "/" + HealthTrack + "/" + HealthSegmentsName;
            public const string HealthSegmentPrefix = "Segment ";
            public const string HotbarName = "Hotbar";
            public const string Hotbar = RootCanvas + "/" + HotbarName;
            public const string HotbarActionHintsName = "Hotbar Action Hints";
            public const string HotbarActionHintsLabelName = "Label";
            public const string HotbarActionHints =
                RootCanvas + "/" + HotbarActionHintsName;
            public const string HotbarActionHintsLabel =
                HotbarActionHints + "/" + HotbarActionHintsLabelName;
            public const string CrosshairCanvas = "Crosshair Canvas";
            public const string Crosshair = "Crosshair Canvas/Crosshair";
            public const string Horizontal = "Horizontal";
            public const string Vertical = "Vertical";
            public const string Item = "Item";
            public const string Icon = "Icon";
            public const string Key = "Key";
            public const string AngledSurface = "Angled Surface";
            public const string CooldownOverlay = "Cooldown Overlay";
            public const string CooldownLabel = "Cooldown Label";
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

            public static string SlotCooldownOverlay(int oneBasedIndex)
            {
                return SlotName(oneBasedIndex) + "/" + CooldownOverlay;
            }

            public static string SlotCooldownLabel(int oneBasedIndex)
            {
                return SlotName(oneBasedIndex) + "/" + CooldownLabel;
            }

            public static string HotbarSlotAngledSurface(int oneBasedIndex)
            {
                return HotbarSlot(oneBasedIndex) + "/" + AngledSurface;
            }

            public static string HotbarSlotIcon(int oneBasedIndex)
            {
                return HotbarSlot(oneBasedIndex) + "/" + Icon;
            }

            public static string HotbarSlotCooldownOverlay(int oneBasedIndex)
            {
                return HotbarSlot(oneBasedIndex) + "/" + CooldownOverlay;
            }

            public static string HotbarSlotCooldownLabel(int oneBasedIndex)
            {
                return HotbarSlot(oneBasedIndex) + "/" + CooldownLabel;
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
            public const string SystemField = "System Field";
            public const string Menu = "Menu";
            public const string FullMenu = "Pause Canvas/Pause Panel/Menu";
            public const string MainOptions = "Main Options";
            public const string FullMainOptions = FullMenu + "/" + MainOptions;
            public const string Resume = "Resume";
            public const string Settings = "Settings";
            public const string QuitToMenu = "Quit To Menu";
            public const string QuitToDesktop = "Quit To Desktop";
                        public const string InputBindingsPanel = "Input Bindings Panel";
            public const string Controls = "Controls";
public const string SettingsPanel = "Settings Panel";
            public const string Fullscreen = "Fullscreen";
            public const string MasterVolume = "Master Volume";
            public const string VolumeValue = "Value";
            public const string SettingsBack = "Back";
            public const string FullResume = FullMainOptions + "/" + Resume;
            public const string FullSettings = FullMainOptions + "/" + Settings;
            public const string FullQuitToMenu = FullMainOptions + "/" + QuitToMenu;
            public const string FullQuitToDesktop = FullMainOptions + "/" + QuitToDesktop;
                        public const string FullInputBindingsPanel = FullMenu + "/" + InputBindingsPanel;
            public const string FullControls = FullSettingsPanel + "/" + Controls;
public const string FullSettingsPanel = FullMenu + "/" + SettingsPanel;
            public const string FullFullscreen = FullSettingsPanel + "/" + Fullscreen;
            public const string FullMasterVolume = FullSettingsPanel + "/" + MasterVolume;
            public const string FullSettingsBack = FullSettingsPanel + "/" + SettingsBack;
            public const string Title = "Title";
            public const string Eyebrow = "Eyebrow";
            public const string Label = "Label";
            public const string MenuResume = Menu + "/" + MainOptions + "/" + Resume;
            public const string MenuFrame = Menu + "/" + Decoration.Frame;
        }

        public static class Equipment
        {
            public const string Canvas = "Equipment Canvas";
            public const string Panel = Canvas + "/Equipment Panel";
            public const string PortraitRegion = "Portrait Region";
            public const string Portrait = PortraitRegion + "/Character Portrait";
            public const string Configuration = "Configuration";
            public const string Slots = Configuration + "/Equipped Slots";
            public const string OwnedGrid = Configuration + "/Owned Grid";
            public const string FullSlots = Panel + "/" + Slots;
            public const string FullOwnedGrid = Panel + "/" + OwnedGrid;
            public const string Icon = "Icon";

            public static string SlotName(int oneBasedIndex)
            {
                return "Equipment Slot " + oneBasedIndex;
            }

            public static string OwnedCellName(int oneBasedIndex)
            {
                return "Owned Cell " + oneBasedIndex;
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
            public const string EarlyEvacuationPromptName =
                "Early Evacuation Prompt";
            public const string EarlyEvacuationPrompt =
                Root + "/" + EarlyEvacuationPromptName;
            public const string EarlyEvacuationProgressName =
                "Early Evacuation Progress";
            public const string EarlyEvacuationProgressFillName = "Fill";
            public const string EarlyEvacuationProgress =
                Root + "/" + EarlyEvacuationProgressName;
            public const string EarlyEvacuationProgressFill =
                EarlyEvacuationProgress
                + "/" + EarlyEvacuationProgressFillName;
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

        public static class NewGameGuide
        {
            public const string CanvasName = "New Game Guide Canvas";
            public const string BackdropName = "Backdrop";
            public const string PanelName = "Guide Panel";
            public const string HeaderName = "Header";
            public const string PageIndicatorName = "Page Indicator";
            public const string ImageName = "Guide Image";
            public const string CaptionName = "Caption";
            public const string SkipButtonName = "Skip";
            public const string NextButtonName = "Next";
            public const string LabelName = "Label";
            public const string Canvas = CanvasName;
            public const string Backdrop = Canvas + "/" + BackdropName;
            public const string Panel = Canvas + "/" + PanelName;
            public const string Header = Panel + "/" + HeaderName;
            public const string PageIndicator =
                Panel + "/" + PageIndicatorName;
            public const string Image = Panel + "/" + ImageName;
            public const string Caption = Panel + "/" + CaptionName;
            public const string SkipButton =
                Panel + "/" + SkipButtonName;
            public const string NextButton =
                Panel + "/" + NextButtonName;
        }

        public static class SpawnIndicator
        {
            public const string RuntimeRoot = "Spawn Point Indicator UI";
            public const string CanvasName = "Spawn Indicator Canvas";
            public const string MarkerName = "Marker";
            public const string RuntimeMarkerName = "Portal Marker";
            public const string ChevronName = "Chevron";
            public const string DistanceName = "Distance";
            public const string Canvas = CanvasName;
            public const string Marker = Canvas + "/" + MarkerName;
            public const string Chevron = Marker + "/" + ChevronName;
            public const string Distance = Marker + "/" + DistanceName;
        }

        public static class Crosshair
        {
            public const string Canvas = "Crosshair Info Canvas";
            public const string Panel = Canvas + "/Info Panel";
            public const string NameLabel = Panel + "/Name";
            public const string StatsLabel = Panel + "/Stats";
            public const string RuleLine = Panel + "/Rule";
        }

        public static class Debug
        {
            public const string CanvasName = "Debug Canvas";
            public const string WindowName = "FPS Window";
            public const string AccentName = "Accent";
            public const string HeaderName = "Header";
            public const string FpsValueName = "FPS Value";
            public const string Canvas = CanvasName;
            public const string Window = Canvas + "/" + WindowName;
            public const string Accent = Window + "/" + AccentName;
            public const string Header = Window + "/" + HeaderName;
            public const string FpsValue = Window + "/" + FpsValueName;
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
