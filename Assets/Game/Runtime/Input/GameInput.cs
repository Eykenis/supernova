using System;
using System.Collections.Generic;
using System.Text;
using Supernova.Infrastructure;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Supernova.Inputs
{
    public static class GameInput
    {
        private const string BindingOverridesPreferenceKey =
            "input.binding-overrides";

        private static readonly Dictionary<GameInputActionId, InputAction>
            actionCache = new Dictionary<GameInputActionId, InputAction>();
        private static InputActionAsset actions;
        private static InputActionRebindingExtensions.RebindingOperation
            activeRebind;
        private static bool initializing;
        private static string activeBindingGroup =
            GameInputDefinitions.KeyboardMouseScheme;
        private static int bindingRevision;

        private static readonly Dictionary<string, string> ActionDisplayNames =
            new Dictionary<string, string>
            {
                ["Move"] = "移动角色",
                ["Look"] = "移动视角",
                ["Jump"] = "跳跃",
                ["Crouch"] = "蹲下",
                ["Sprint"] = "冲刺",
                ["PrimaryAction"] = "主交互",
                ["SecondaryAction"] = "副交互",
                ["Interact"] = "交互",
                ["ThrowPickaxe"] = "投掷探险镐",
                ["ToggleEquipment"] = "切换喷气背包",
                ["Hotbar1"] = "装备栏1",
                ["Hotbar2"] = "装备栏2",
                ["Hotbar3"] = "装备栏3",
                ["Hotbar4"] = "装备栏4",
                ["Hotbar5"] = "装备栏5",
                ["HotbarScroll"] = "滚动切换装备栏",
                ["TogglePerspective"] = "切换视角",
                ["MagnetRotate"] = "旋转磁力抓取物",
                ["Pause"] = "暂停",
                ["ToggleLoadout"] = "切换负载",
                ["Navigate"] = "导航",
                ["Submit"] = "提交",
                ["Cancel"] = "取消",
                ["Point"] = "点数",
                ["Click"] = "点击",
                ["RightClick"] = "鼠标右键",
                ["MiddleClick"] = "鼠标中键",
                ["ScrollWheel"] = "滚轮",
                ["Mission"] = "任务",
                ["Hud"] = "HUD",
                ["FlyToggle"] = "切换飞行模式",
                ["Smile"] = "微笑",
                ["Hit"] = "击中",
                ["Die"] = "死亡",
                ["Recover"] = "重生",
            };

        public static event Action BindingsChanged;

        public static InputActionAsset Actions
        {
            get
            {
                EnsureInitialized();
                return actions;
            }
        }

        public static string ActiveBindingGroup
        {
            get
            {
                EnsureInitialized();
                return activeBindingGroup;
            }
        }

        public static int BindingRevision => bindingRevision;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (actions != null)
            {
                actions.Disable();
                UnityEngine.Object.Destroy(actions);
            }
            InputSystem.onActionChange -= OnActionChange;
            activeRebind?.Dispose();
            activeRebind = null;
            actions = null;
            actionCache.Clear();
            initializing = false;
            activeBindingGroup = GameInputDefinitions.KeyboardMouseScheme;
            bindingRevision = 0;
            BindingsChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeSceneLoad()
        {
            EnsureInitialized();
        }

        public static InputAction Action(GameInputActionId id)
        {
            EnsureInitialized();
            if (actionCache.TryGetValue(id, out InputAction cached))
                return cached;

            string path = GameInputDefinitions.GetActionPath(id);
            InputAction resolved = actions.FindAction(path, true);
            actionCache.Add(id, resolved);
            return resolved;
        }

        public static InputAction FindAction(string path)
        {
            EnsureInitialized();
            return actions.FindAction(path, false);
        }

        public static bool Pressed(GameInputActionId id)
        {
            return Action(id).WasPressedThisFrame();
        }

        public static bool Released(GameInputActionId id)
        {
            return Action(id).WasReleasedThisFrame();
        }

        public static bool Held(GameInputActionId id)
        {
            return Action(id).IsPressed();
        }

        public static Vector2 ReadVector2(GameInputActionId id)
        {
            return Action(id).ReadValue<Vector2>();
        }

        public static float ReadFloat(GameInputActionId id)
        {
            return Action(id).ReadValue<float>();
        }

        public static void ConfigureUiModule(InputSystemUIInputModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            module.actionsAsset = Actions;
            module.point = InputActionReference.Create(Action(GameInputActionId.Point));
            module.leftClick = InputActionReference.Create(Action(GameInputActionId.Click));
            module.rightClick = InputActionReference.Create(Action(GameInputActionId.RightClick));
            module.middleClick = InputActionReference.Create(Action(GameInputActionId.MiddleClick));
            module.scrollWheel = InputActionReference.Create(Action(GameInputActionId.ScrollWheel));
            module.move = InputActionReference.Create(Action(GameInputActionId.Navigate));
            module.submit = InputActionReference.Create(Action(GameInputActionId.Submit));
            module.cancel = InputActionReference.Create(Action(GameInputActionId.Cancel));
        }

        public static IReadOnlyList<GameInputBindingInfo> GetRebindableBindings(
            string bindingGroup = null)
        {
            EnsureInitialized();
            string group = string.IsNullOrWhiteSpace(bindingGroup)
                ? activeBindingGroup
                : bindingGroup;
            var result = new List<GameInputBindingInfo>();

            foreach (GameInputActionId id in Enum.GetValues(
                         typeof(GameInputActionId)))
            {
                InputAction action = Action(id);
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (binding.isComposite
                        || !BindingMatchesGroup(binding, group)
                        || (!binding.isPartOfComposite
                            && !string.Equals(
                                action.expectedControlType,
                                "Button",
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    result.Add(new GameInputBindingInfo(
                        id,
                        i,
                        action.actionMap.name,
                        LocalizeActionName(action.name),
                        SplitDisplayName(binding.name),
                        action.GetBindingDisplayString(i)));
                }
            }

            return result;
        }


        public static string GetBindingDisplayString(
            string actionPath,
            string partName = null,
            bool compact = false)
        {
            InputAction action = FindAction(actionPath);
            if (action == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(partName))
            {
                int partIndex = FindBindingIndex(action, partName);
                return partIndex >= 0
                    ? action.GetBindingDisplayString(partIndex)
                    : string.Empty;
            }

            // Move has no single key, so it always uses the joined form even
            // without the compact modifier; otherwise it would render empty.
            if (actionPath == GameInputDefinitions.GetActionPath(
                    GameInputActionId.Move))
            {
                return GetCompactMoveDisplay(action);
            }

            // A prompt names one key to press, so alternates such as
            // "Ctrl | Right Ctrl" would only add noise. Composite actions have no
            // single key, so they keep the combined form (Move renders as WASD).
            int primary = FindPrimaryBindingIndex(action);
            if (primary >= 0)
                return action.GetBindingDisplayString(primary);

            InputBinding mask = InputBinding.MaskByGroup(activeBindingGroup);
            return action.GetBindingDisplayString(mask);
        }

        public static void SetActiveBindingGroup(string bindingGroup)
        {
            if (string.IsNullOrWhiteSpace(bindingGroup)
                || string.Equals(
                    activeBindingGroup,
                    bindingGroup,
                    StringComparison.Ordinal))
            {
                return;
            }

            activeBindingGroup = bindingGroup;
            NotifyBindingsChanged();
        }

        public static void StartInteractiveRebind(
            GameInputActionId id,
            int bindingIndex,
            Action<bool> completed)
        {
            CancelInteractiveRebind();
            InputAction action = Action(id);
            if ((uint)bindingIndex >= action.bindings.Count)
                throw new ArgumentOutOfRangeException(nameof(bindingIndex));

            action.Disable();
            activeRebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(operation => FinishRebind(action, operation, false, completed))
                .OnComplete(operation => FinishRebind(action, operation, true, completed));
            activeRebind.Start();
        }

        public static void CancelInteractiveRebind()
        {
            if (activeRebind == null)
                return;
            activeRebind.Cancel();
        }

        public static void SaveBindingOverrides()
        {
            EnsureInitialized();
            PlayerPrefs.SetString(
                BindingOverridesPreferenceKey,
                actions.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public static void ResetBindingOverrides()
        {
            EnsureInitialized();
            actions.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(BindingOverridesPreferenceKey);
            PlayerPrefs.Save();
            NotifyBindingsChanged();
        }

        private static void EnsureInitialized()
        {
            if (actions != null || initializing)
                return;

            initializing = true;
            try
            {
                InputActionAsset source = GameAssetCatalog.Current != null
                    && GameAssetCatalog.Current.Input != null
                    ? GameAssetCatalog.Current.Input.Actions
                    : null;
                actions = source != null
                    ? UnityEngine.Object.Instantiate(source)
                    : GameInputDefinitions.CreateAsset();
                actions.name = "GameInputActions (Runtime)";
                LoadBindingOverrides();
                actions.Enable();
                InputSystem.onActionChange -= OnActionChange;
                InputSystem.onActionChange += OnActionChange;
            }
            finally
            {
                initializing = false;
            }
        }

        private static void LoadBindingOverrides()
        {
            string json = PlayerPrefs.GetString(
                BindingOverridesPreferenceKey,
                string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                actions.LoadBindingOverridesFromJson(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Ignoring invalid saved input bindings: "
                    + exception.Message);
                PlayerPrefs.DeleteKey(BindingOverridesPreferenceKey);
            }
        }

        private static void FinishRebind(
            InputAction action,
            InputActionRebindingExtensions.RebindingOperation operation,
            bool applied,
            Action<bool> completed)
        {
            operation.Dispose();
            if (ReferenceEquals(activeRebind, operation))
                activeRebind = null;
            action.Enable();
            if (applied)
                SaveBindingOverrides();
            NotifyBindingsChanged();
            completed?.Invoke(applied);
        }

        private static void OnActionChange(object changed, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed
                || !(changed is InputAction action)
                || action.actionMap?.asset != actions
                || action.activeControl?.device == null)
            {
                return;
            }

            string group = action.activeControl.device is Gamepad
                ? GameInputDefinitions.GamepadScheme
                : GameInputDefinitions.KeyboardMouseScheme;
            SetActiveBindingGroup(group);
        }

        /// <summary>
        /// The first non-composite binding in the active group, or -1 when the
        /// action is driven purely by composites.
        /// </summary>
        private static int FindPrimaryBindingIndex(InputAction action)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite
                    || binding.isPartOfComposite
                    || !BindingMatchesActiveGroup(binding))
                {
                    continue;
                }
                return i;
            }
            return -1;
        }

        private static int FindBindingIndex(
            InputAction action,
            string partName)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isPartOfComposite
                    && string.Equals(
                        binding.name,
                        partName,
                        StringComparison.OrdinalIgnoreCase)
                    && BindingMatchesActiveGroup(binding))
                {
                    return i;
                }
            }
            return -1;
        }

        private static string GetCompactMoveDisplay(InputAction action)
        {
            string[] order = { "up", "left", "down", "right" };
            var builder = new StringBuilder();
            for (int i = 0; i < order.Length; i++)
            {
                int bindingIndex = FindBindingIndex(action, order[i]);
                if (bindingIndex < 0)
                    return action.GetBindingDisplayString(
                        InputBinding.MaskByGroup(activeBindingGroup));
                builder.Append(action.GetBindingDisplayString(bindingIndex));
            }
            return builder.ToString();
        }

        private static bool BindingMatchesActiveGroup(InputBinding binding)
        {
            if (string.IsNullOrWhiteSpace(binding.groups))
                return true;
            string[] groups = binding.groups.Split(';');
            for (int i = 0; i < groups.Length; i++)
            {
                if (string.Equals(
                    groups[i],
                    activeBindingGroup,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void NotifyBindingsChanged()
        {
            bindingRevision++;
            BindingsChanged?.Invoke();
        }
    

        private static string LocalizeActionName(string actionName)
        {
            return ActionDisplayNames.TryGetValue(actionName, out string localized)
                ? localized
                : SplitDisplayName(actionName);
        }

        private static string SplitDisplayName(string source)
        {
            if (string.IsNullOrEmpty(source))
                return string.Empty;

            var builder = new StringBuilder(source.Length + 8);
            for (int i = 0; i < source.Length; i++)
            {
                char current = source[i];
                if (i > 0 && char.IsUpper(current)
                    && !char.IsUpper(source[i - 1]))
                {
                    builder.Append(' ');
                }
                builder.Append(char.ToUpperInvariant(current));
            }
            return builder.ToString();
        }


        private static bool BindingMatchesGroup(
            InputBinding binding,
            string group)
        {
            if (string.IsNullOrWhiteSpace(binding.groups))
                return true;
            string[] groups = binding.groups.Split(';');
            for (int i = 0; i < groups.Length; i++)
            {
                if (string.Equals(groups[i], group, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
}
}
