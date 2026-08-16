using System.Linq;
using System;
using NUnit.Framework;
using Supernova.Infrastructure;
using Supernova.Inputs;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class GameInputTests
{
    [Test]
    public void Definitions_ContainEveryRegisteredAction()
    {
        InputActionAsset asset = GameInputDefinitions.CreateAsset();
        try
        {
            foreach (GameInputActionId id in Enum.GetValues(typeof(GameInputActionId)))
            {
                string path = GameInputDefinitions.GetActionPath(id);
                Assert.That(path, Is.Not.Empty, id.ToString());
                Assert.That(asset.FindAction(path), Is.Not.Null, path);
                Assert.That(asset.FindAction(path).bindings.Count, Is.GreaterThan(0), path);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    /// <summary>
    /// A binding path that parses but matches no control is dropped silently by
    /// the Input System, so the action simply never fires (this is how
    /// "&lt;Keyboard&gt;/digit1" broke every hotbar key). Counting bindings cannot
    /// catch that; the paths have to be resolved against a real device.
    /// </summary>
    [Test]
    public void Definitions_ResolveEveryKeyboardAndMouseBindingToAControl()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        InputActionAsset asset = GameInputDefinitions.CreateAsset();
        var unresolved = new System.Collections.Generic.List<string>();
        try
        {
            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputBinding binding in map.bindings)
                {
                    if (binding.isComposite
                        || string.IsNullOrEmpty(binding.path)
                        || !TargetsKeyboardOrMouse(binding.path))
                    {
                        continue;
                    }

                    using (var probe = new InputAction(binding: binding.path))
                    {
                        probe.Enable();
                        if (probe.controls.Count == 0)
                        {
                            unresolved.Add(
                                $"{map.name}/{binding.action} -> {binding.path}");
                        }
                    }
                }
            }

            Assert.That(unresolved, Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keyboard);
        }
    }

    /// <summary>
    /// Both the top-row digits and the numeric keypad must select hotbar slots.
    /// </summary>
    [Test]
    public void HotbarActions_BindTopRowDigitAndNumpad()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        InputActionAsset asset = GameInputDefinitions.CreateAsset();
        try
        {
            GameInputActionId[] hotbars =
            {
                GameInputActionId.Hotbar1,
                GameInputActionId.Hotbar2,
                GameInputActionId.Hotbar3,
                GameInputActionId.Hotbar4,
                GameInputActionId.Hotbar5,
            };

            for (int slot = 0; slot < hotbars.Length; slot++)
            {
                InputAction action = asset.FindAction(
                    GameInputDefinitions.GetActionPath(hotbars[slot]),
                    true);
                action.Enable();
                string digit = (slot + 1).ToString();
                string[] controlPaths = action.controls
                    .Select(control => control.path)
                    .ToArray();

                Assert.That(
                    controlPaths,
                    Has.Some.EndsWith("/" + digit),
                    $"{hotbars[slot]} is missing the top-row digit key.");
                Assert.That(
                    controlPaths,
                    Has.Some.EndsWith("/numpad" + digit),
                    $"{hotbars[slot]} is missing the numpad key.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
            InputSystem.RemoveDevice(keyboard);
        }
    }

    [Test]
    public void StructureBoxActions_RequireControlModifier()
    {
        InputActionAsset asset = GameInputDefinitions.CreateAsset();
        try
        {
            AssertControlModifiedBinding(
                asset,
                GameInputActionId.StructureFill,
                "<Keyboard>/g");
            AssertControlModifiedBinding(
                asset,
                GameInputActionId.StructureClearFillBox,
                "<Keyboard>/d");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    private static void AssertControlModifiedBinding(
        InputActionAsset asset,
        GameInputActionId actionId,
        string buttonPath)
    {
        InputAction action = asset.FindAction(
            GameInputDefinitions.GetActionPath(actionId),
            true);
        Assert.That(action.bindings.Count, Is.EqualTo(3));
        Assert.That(action.bindings[0].isComposite, Is.True);
        Assert.That(
            action.bindings[0].path,
            Is.EqualTo("ButtonWithOneModifier"));
        Assert.That(action.bindings[1].name, Is.EqualTo("Modifier"));
        Assert.That(action.bindings[1].path, Is.EqualTo("<Keyboard>/ctrl"));
        Assert.That(action.bindings[1].isPartOfComposite, Is.True);
        Assert.That(action.bindings[2].name, Is.EqualTo("Button"));
        Assert.That(action.bindings[2].path, Is.EqualTo(buttonPath));
        Assert.That(action.bindings[2].isPartOfComposite, Is.True);
    }

    private static bool TargetsKeyboardOrMouse(string path)
    {
        return path.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [Test]
    public void GeneratedAsset_UsesCentralizedPathAndCatalogReference()
    {
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
            ProjectAssetPaths.Config.GameInputActions);
        GameAssetCatalog catalog = AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
            ProjectAssetPaths.Config.GameAssetCatalog);

        Assert.That(asset, Is.Not.Null);
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.Input, Is.Not.Null);
        Assert.That(catalog.Input.Actions, Is.SameAs(asset));
    }

    [Test]
    public void RebindableBindings_ExposeKeysButExcludePointerAxes()
    {
        GameInputBindingInfo[] bindings = GameInput
            .GetRebindableBindings(GameInputDefinitions.KeyboardMouseScheme)
            .ToArray();

        Assert.That(
            bindings.Any(binding =>
                binding.ActionId == GameInputActionId.Move
                && binding.BindingName == "UP"
                && binding.DisplayString == "W"),
            Is.True);
        Assert.That(
            bindings.Any(binding =>
                binding.ActionId == GameInputActionId.TogglePerspective),
            Is.True);
        Assert.That(
            bindings.Any(binding => binding.ActionId == GameInputActionId.Look),
            Is.False);
        Assert.That(
            bindings.Any(binding => binding.ActionId == GameInputActionId.Point),
            Is.False);
    }


    [Test]
    public void PromptResolver_ResolvesCompactMovementAndCompositeParts()
    {
        GameInput.SetActiveBindingGroup(GameInputDefinitions.KeyboardMouseScheme);

        Assert.That(
            InputPromptResolver.Resolve("按{{input:Gameplay/Move|compact}}移动"),
            Is.EqualTo("按WASD移动"));
        Assert.That(
            InputPromptResolver.Resolve("向前：{{input:Gameplay/Move.up}}"),
            Is.EqualTo("向前：W"));
        Assert.That(
            InputPromptResolver.Resolve("{{input:Gameplay/Move}} to Move"),
            Is.EqualTo("WASD to Move"),
            "Move has no single key, so it must not resolve to an empty string.");
    }

    /// <summary>
    /// A prompt tells the player one key to press. Actions that carry an
    /// alternate binding must not render as "Ctrl | Right Ctrl".
    /// </summary>
    [Test]
    public void PromptResolver_ShowsOnlyThePrimaryKeyForAlternateBindings()
    {
        GameInput.SetActiveBindingGroup(GameInputDefinitions.KeyboardMouseScheme);

        Assert.That(
            GameInput.GetBindingDisplayString("Gameplay/Crouch"),
            Is.EqualTo("Ctrl"));
        Assert.That(
            GameInput.GetBindingDisplayString("Gameplay/Sprint"),
            Is.EqualTo("Shift"));
        Assert.That(
            GameInput.GetBindingDisplayString("UI/Submit"),
            Is.EqualTo("Enter"));
        Assert.That(
            GameInput.GetBindingDisplayString("Gameplay/Hotbar1"),
            Is.EqualTo("1"));
    }

    [Test]
    public void PromptResolver_PreservesUnknownAndEscapedTokens()
    {
        Assert.That(
            InputPromptResolver.Resolve("{{input:Gameplay/NotRegistered}}"),
            Is.EqualTo("{{input:Gameplay/NotRegistered}}"));
        Assert.That(
            InputPromptResolver.Resolve(@"\{{input:Gameplay/Jump}}"),
            Is.EqualTo("{{input:Gameplay/Jump}}"));
    }
}
