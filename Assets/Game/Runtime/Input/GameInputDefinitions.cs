using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Supernova.Inputs
{
    public static class GameInputDefinitions
    {
        public const string KeyboardMouseScheme = "KeyboardMouse";
        public const string GamepadScheme = "Gamepad";

        public const string GameplayMap = "Gameplay";
        public const string UiMap = "UI";
        public const string DebugMap = "Debug";
        public const string SpectatorMap = "Spectator";
        public const string StructureEditorMap = "StructureEditor";
        public const string ExamplesMap = "Examples";

        public static InputActionAsset CreateAsset()
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "GameInputActions";
            CreateGameplayMap(asset);
            CreateUiMap(asset);
            CreateDebugMap(asset);
            CreateSpectatorMap(asset);
            CreateStructureEditorMap(asset);
            CreateExamplesMap(asset);
            asset.AddControlScheme(KeyboardMouseScheme)
                .WithRequiredDevice("<Keyboard>")
                .WithRequiredDevice("<Mouse>");
            asset.AddControlScheme(GamepadScheme)
                .WithRequiredDevice("<Gamepad>");
            return asset;
        }

        public static string GetActionPath(GameInputActionId id)
        {
            switch (id)
            {
                case GameInputActionId.Move: return GameplayMap + "/Move";
                case GameInputActionId.Look: return GameplayMap + "/Look";
                case GameInputActionId.Jump: return GameplayMap + "/Jump";
                case GameInputActionId.Crouch: return GameplayMap + "/Crouch";
                case GameInputActionId.Sprint: return GameplayMap + "/Sprint";
                case GameInputActionId.PrimaryAction: return GameplayMap + "/PrimaryAction";
                case GameInputActionId.SecondaryAction: return GameplayMap + "/SecondaryAction";
                case GameInputActionId.Interact: return GameplayMap + "/Interact";
                case GameInputActionId.ThrowPickaxe: return GameplayMap + "/ThrowPickaxe";
                case GameInputActionId.ToggleEquipment: return GameplayMap + "/ToggleEquipment";
                case GameInputActionId.Hotbar1: return GameplayMap + "/Hotbar1";
                case GameInputActionId.Hotbar2: return GameplayMap + "/Hotbar2";
                case GameInputActionId.Hotbar3: return GameplayMap + "/Hotbar3";
                case GameInputActionId.Hotbar4: return GameplayMap + "/Hotbar4";
                case GameInputActionId.Hotbar5: return GameplayMap + "/Hotbar5";
                case GameInputActionId.HotbarScroll: return GameplayMap + "/HotbarScroll";
                case GameInputActionId.TogglePerspective: return GameplayMap + "/TogglePerspective";
                case GameInputActionId.CartRotate: return GameplayMap + "/CartRotate";
                case GameInputActionId.Pause: return UiMap + "/Pause";
                case GameInputActionId.ToggleLoadout: return UiMap + "/ToggleLoadout";
                case GameInputActionId.Navigate: return UiMap + "/Navigate";
                case GameInputActionId.Submit: return UiMap + "/Submit";
                case GameInputActionId.Cancel: return UiMap + "/Cancel";
                case GameInputActionId.Point: return UiMap + "/Point";
                case GameInputActionId.Click: return UiMap + "/Click";
                case GameInputActionId.RightClick: return UiMap + "/RightClick";
                case GameInputActionId.MiddleClick: return UiMap + "/MiddleClick";
                case GameInputActionId.ScrollWheel: return UiMap + "/ScrollWheel";
                case GameInputActionId.DebugMission: return DebugMap + "/Mission";
                case GameInputActionId.DebugHud: return DebugMap + "/Hud";
                case GameInputActionId.DebugFlyToggle: return DebugMap + "/FlyToggle";
                case GameInputActionId.DebugSmile: return DebugMap + "/Smile";
                case GameInputActionId.DebugHit: return DebugMap + "/Hit";
                case GameInputActionId.DebugDie: return DebugMap + "/Die";
                case GameInputActionId.DebugRecover: return DebugMap + "/Recover";
                case GameInputActionId.DebugFlyUp: return DebugMap + "/FlyUp";
                case GameInputActionId.DebugFlyDown: return DebugMap + "/FlyDown";
                case GameInputActionId.DebugFlyFast: return DebugMap + "/FlyFast";
                case GameInputActionId.SpectatorLookHold: return SpectatorMap + "/LookHold";
                case GameInputActionId.SpectatorOrbitHold: return SpectatorMap + "/OrbitHold";
                case GameInputActionId.SpectatorUp: return SpectatorMap + "/Up";
                case GameInputActionId.SpectatorDown: return SpectatorMap + "/Down";
                case GameInputActionId.SpectatorFast: return SpectatorMap + "/Fast";
                case GameInputActionId.StructureSave: return StructureEditorMap + "/Save";
                case GameInputActionId.StructurePaint: return StructureEditorMap + "/Paint";
                case GameInputActionId.StructureErase: return StructureEditorMap + "/Erase";
                case GameInputActionId.StructureToggleFillMode: return StructureEditorMap + "/ToggleFillMode";
                case GameInputActionId.StructureFill: return StructureEditorMap + "/Fill";
                case GameInputActionId.StructureClearFillBox: return StructureEditorMap + "/ClearFillBox";
                case GameInputActionId.PortalReset: return ExamplesMap + "/PortalReset";
                case GameInputActionId.PrototypeReset: return ExamplesMap + "/PrototypeReset";
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        private static void CreateGameplayMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(GameplayMap);
            InputAction move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            AddMoveBindings(move);
            move.AddBinding("<Gamepad>/leftStick", groups: GamepadScheme);

            AddVector2(
                map,
                "Look",
                "<Mouse>/delta",
                "<Gamepad>/rightStick",
                "ScaleVector2(x=0.1,y=0.1)");
            AddButton(map, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            AddButton(map, "Crouch", "<Keyboard>/leftCtrl", "<Gamepad>/rightStickPress", "<Keyboard>/rightCtrl");
            AddButton(map, "Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress", "<Keyboard>/rightShift");
            AddButton(map, "PrimaryAction", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            AddButton(map, "SecondaryAction", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            AddButton(map, "Interact", "<Keyboard>/e", "<Gamepad>/buttonWest");
            AddButton(map, "ThrowPickaxe", "<Keyboard>/g", "<Gamepad>/buttonNorth");
            AddButton(map, "ToggleEquipment", "<Keyboard>/v", "<Gamepad>/dpad/up");
            // The top-row digits are named "1".."5", not "digit1".."digit5".
            // Matching by displayed character also survives non-QWERTY layouts.
            AddButton(map, "Hotbar1", "<Keyboard>/#(1)", null, "<Keyboard>/numpad1");
            AddButton(map, "Hotbar2", "<Keyboard>/#(2)", null, "<Keyboard>/numpad2");
            AddButton(map, "Hotbar3", "<Keyboard>/#(3)", null, "<Keyboard>/numpad3");
            AddButton(map, "Hotbar4", "<Keyboard>/#(4)", null, "<Keyboard>/numpad4");
            AddButton(map, "Hotbar5", "<Keyboard>/#(5)", null, "<Keyboard>/numpad5");
            AddVector2(map, "HotbarScroll", "<Mouse>/scroll", null);
            AddButton(map, "TogglePerspective", "<Keyboard>/f5", "<Gamepad>/rightShoulder");
            AddButton(map, "CartRotate", "<Mouse>/middleButton", "<Gamepad>/leftShoulder");
        }

        private static void CreateUiMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(UiMap);
            AddButton(map, "Pause", "<Keyboard>/escape", "<Gamepad>/start");
            AddButton(map, "ToggleLoadout", "<Keyboard>/tab", "<Gamepad>/select");
            InputAction navigate = map.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            AddMoveBindings(navigate);
            navigate.AddBinding("<Gamepad>/leftStick", groups: GamepadScheme);
            navigate.AddBinding("<Gamepad>/dpad", groups: GamepadScheme);
            AddButton(map, "Submit", "<Keyboard>/enter", "<Gamepad>/buttonSouth", "<Keyboard>/space");
            AddButton(map, "Cancel", "<Keyboard>/escape", "<Gamepad>/buttonEast");
            AddVector2(map, "Point", "<Mouse>/position", null);
            AddPassThroughButton(map, "Click", "<Mouse>/leftButton", "<Gamepad>/buttonSouth");
            AddPassThroughButton(map, "RightClick", "<Mouse>/rightButton", null);
            AddPassThroughButton(map, "MiddleClick", "<Mouse>/middleButton", null);
            AddVector2(map, "ScrollWheel", "<Mouse>/scroll", null);
        }

        private static void CreateDebugMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(DebugMap);
            AddButton(map, "Mission", "<Keyboard>/f1");
            AddButton(map, "Hud", "<Keyboard>/f2");
            AddButton(map, "FlyToggle", "<Keyboard>/f3");
            AddButton(map, "Smile", "<Keyboard>/q");
            AddButton(map, "Hit", "<Keyboard>/k");
            AddButton(map, "Die", "<Keyboard>/l");
            AddButton(map, "Recover", "<Keyboard>/r");
            AddButton(map, "FlyUp", "<Keyboard>/space");
            AddButton(map, "FlyDown", "<Keyboard>/leftCtrl", null, "<Keyboard>/c");
            AddButton(map, "FlyFast", "<Keyboard>/leftShift", null, "<Keyboard>/rightShift");
        }

        private static void CreateSpectatorMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(SpectatorMap);
            AddButton(map, "LookHold", "<Mouse>/rightButton");
            AddButton(map, "OrbitHold", "<Mouse>/leftButton");
            AddButton(map, "Up", "<Keyboard>/e");
            AddButton(map, "Down", "<Keyboard>/q");
            AddButton(map, "Fast", "<Keyboard>/leftShift", null, "<Keyboard>/rightShift");
        }

        private static void CreateStructureEditorMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(StructureEditorMap);
            InputAction save = map.AddAction("Save", InputActionType.Button, expectedControlLayout: "Button");
            save.AddCompositeBinding("ButtonWithOneModifier")
                .With("Modifier", "<Keyboard>/ctrl", KeyboardMouseScheme)
                .With("Button", "<Keyboard>/s", KeyboardMouseScheme);
            // Paint places a cell and Erase removes one, so the pointer buttons
            // follow the authoring feel: left removes, right places.
            AddButton(map, "Paint", "<Mouse>/rightButton");
            AddButton(map, "Erase", "<Mouse>/leftButton");
            AddButton(map, "ToggleFillMode", "<Keyboard>/f5");
            InputAction fill = map.AddAction(
                "Fill",
                InputActionType.Button,
                expectedControlLayout: "Button");
            fill.AddCompositeBinding("ButtonWithOneModifier")
                .With("Modifier", "<Keyboard>/ctrl", KeyboardMouseScheme)
                .With("Button", "<Keyboard>/g", KeyboardMouseScheme);
            InputAction clearFillBox = map.AddAction(
                "ClearFillBox",
                InputActionType.Button,
                expectedControlLayout: "Button");
            clearFillBox.AddCompositeBinding("ButtonWithOneModifier")
                .With("Modifier", "<Keyboard>/ctrl", KeyboardMouseScheme)
                .With("Button", "<Keyboard>/d", KeyboardMouseScheme);
        }

        private static void CreateExamplesMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(ExamplesMap);
            AddButton(map, "PortalReset", "<Keyboard>/r");
            AddButton(map, "PrototypeReset", "<Keyboard>/r");
        }

        private static void AddMoveBindings(InputAction action)
        {
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", KeyboardMouseScheme)
                .With("Down", "<Keyboard>/s", KeyboardMouseScheme)
                .With("Left", "<Keyboard>/a", KeyboardMouseScheme)
                .With("Right", "<Keyboard>/d", KeyboardMouseScheme);
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", KeyboardMouseScheme)
                .With("Down", "<Keyboard>/downArrow", KeyboardMouseScheme)
                .With("Left", "<Keyboard>/leftArrow", KeyboardMouseScheme)
                .With("Right", "<Keyboard>/rightArrow", KeyboardMouseScheme);
        }

        private static InputAction AddButton(
            InputActionMap map,
            string name,
            string keyboardPath,
            string gamepadPath = null,
            string alternateKeyboardPath = null)
        {
            InputAction action = map.AddAction(
                name,
                InputActionType.Button,
                expectedControlLayout: "Button");
            if (!string.IsNullOrEmpty(keyboardPath))
                action.AddBinding(keyboardPath, groups: KeyboardMouseScheme);
            if (!string.IsNullOrEmpty(alternateKeyboardPath))
                action.AddBinding(alternateKeyboardPath, groups: KeyboardMouseScheme);
            if (!string.IsNullOrEmpty(gamepadPath))
                action.AddBinding(gamepadPath, groups: GamepadScheme);
            return action;
        }

        private static InputAction AddPassThroughButton(
            InputActionMap map,
            string name,
            string keyboardPath,
            string gamepadPath)
        {
            InputAction action = map.AddAction(
                name,
                InputActionType.PassThrough,
                expectedControlLayout: "Button");
            if (!string.IsNullOrEmpty(keyboardPath))
                action.AddBinding(keyboardPath, groups: KeyboardMouseScheme);
            if (!string.IsNullOrEmpty(gamepadPath))
                action.AddBinding(gamepadPath, groups: GamepadScheme);
            return action;
        }

        private static InputAction AddVector2(
            InputActionMap map,
            string name,
            string keyboardMousePath,
            string gamepadPath,
            string keyboardMouseProcessors = null)
        {
            InputAction action = map.AddAction(
                name,
                InputActionType.PassThrough,
                expectedControlLayout: "Vector2");
            if (!string.IsNullOrEmpty(keyboardMousePath))
                action.AddBinding(
                    keyboardMousePath,
                    processors: keyboardMouseProcessors,
                    groups: KeyboardMouseScheme);
            if (!string.IsNullOrEmpty(gamepadPath))
                action.AddBinding(gamepadPath, groups: GamepadScheme);
            return action;
        }
    }
}
