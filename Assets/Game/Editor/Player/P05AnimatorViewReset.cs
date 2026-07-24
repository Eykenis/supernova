#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Supernova.EditorTools.PlayerSetup
{
    public static class P05AnimatorViewReset
    {
        private const string ControllerPath = "Assets/Game/Animations/P05Player.controller";
        private const string SessionKey = "Supernova.P05AnimatorViewReset.v4";

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionKey, false)) return;
                SessionState.SetBool(SessionKey, true);
                OpenAndFrameAll();
            };
        }

        [MenuItem("Tools/Supernova/Player/Open And Frame P05 Animator")]
        public static void OpenAndFrameAll()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) return;
            Type animatorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEditor.Graphs.AnimatorControllerTool"))
                .FirstOrDefault(type => type != null);
            if (animatorType != null)
                EditorWindow.GetWindow(animatorType, false, "Animator", true);
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
            AssetDatabase.OpenAsset(controller);
            EditorApplication.delayCall += () => EditorApplication.delayCall += SendFrameAll;
        }

        private static void SendFrameAll()
        {
            EditorWindow animatorWindow = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(window => window.GetType().FullName == "UnityEditor.Graphs.AnimatorControllerTool");
            if (animatorWindow == null) return;
            animatorWindow.Focus();
            animatorWindow.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A });
            animatorWindow.SendEvent(new Event { type = EventType.KeyUp, keyCode = KeyCode.A });
            animatorWindow.Repaint();
            Debug.Log("P05Player Animator opened and framed to show all states.");
        }
    }
}
#endif
