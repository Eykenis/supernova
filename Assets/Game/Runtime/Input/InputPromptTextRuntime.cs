using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Supernova.Inputs
{
    [DefaultExecutionOrder(-10000)]
    public sealed class InputPromptTextRuntime : MonoBehaviour, ITextPreprocessor
    {
        private static InputPromptTextRuntime instance;

        private readonly HashSet<TMP_Text> promptTexts =
            new HashSet<TMP_Text>();
        private readonly Dictionary<Text, string> legacyTemplates =
            new Dictionary<Text, string>();
        private bool handlingTextChange;

        /// <summary>
        /// The live runtime, created on demand so text assigned before the first
        /// scene finishes loading still resolves.
        /// </summary>
        private static InputPromptTextRuntime Instance
        {
            get
            {
                if (instance == null)
                    instance = FindObjectOfType<InputPromptTextRuntime>();
                if (instance == null && Application.isPlaying)
                    instance = CreateRuntimeObject();
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            if (FindObjectOfType<InputPromptTextRuntime>() != null)
                return;
            CreateRuntimeObject();
        }

        private static InputPromptTextRuntime CreateRuntimeObject()
        {
            var runtimeObject = new GameObject("Input Prompt Text Runtime");
            DontDestroyOnLoad(runtimeObject);
            return runtimeObject.AddComponent<InputPromptTextRuntime>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            GameInput.BindingsChanged += RefreshRegisteredText;
            RegisterLoadedText();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            GameInput.BindingsChanged -= RefreshRegisteredText;
        }

        public string PreprocessText(string text)
        {
            return InputPromptResolver.Resolve(text);
        }

        /// <summary>
        /// Assigns <paramref name="value"/> and attaches the preprocessor in the
        /// same call. Text assigned at runtime cannot rely on the scene scan or
        /// on TMP's changed event, which fires too late to stop an unresolved
        /// token from being displayed first.
        /// </summary>
        public static void SetText(TMP_Text target, string value)
        {
            if (target == null)
                return;

            target.text = value ?? string.Empty;
            InputPromptTextRuntime runtime = Instance;
            if (runtime != null)
                runtime.Register(target);
        }

        /// <summary>
        /// Legacy <see cref="Text"/> has no preprocessor hook, so the tokens are
        /// resolved into the assigned value while the template is kept for
        /// re-resolving after a rebind.
        /// </summary>
        public static void SetText(Text target, string value)
        {
            if (target == null)
                return;

            target.text = value ?? string.Empty;
            InputPromptTextRuntime runtime = Instance;
            if (runtime != null)
                runtime.Register(target);
            else
                target.text = InputPromptResolver.Resolve(target.text);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RegisterLoadedText();
        }

        private void RegisterLoadedText()
        {
            TMP_Text[] tmpTexts = FindObjectsOfType<TMP_Text>(true);
            for (int i = 0; i < tmpTexts.Length; i++)
                Register(tmpTexts[i]);

            Text[] legacyTexts = FindObjectsOfType<Text>(true);
            for (int i = 0; i < legacyTexts.Length; i++)
                Register(legacyTexts[i]);
        }

        private void OnTextChanged(Object changed)
        {
            if (handlingTextChange || !(changed is TMP_Text text))
                return;
            Register(text);
        }

        private void Register(TMP_Text text)
        {
            if (text == null
                || string.IsNullOrEmpty(text.text)
                || !text.text.Contains(InputPromptResolver.Marker))
            {
                return;
            }

            if (text.textPreprocessor != null
                && !ReferenceEquals(text.textPreprocessor, this))
            {
                Debug.LogWarning(
                    "Input prompt token was found on TMP text with another "
                    + "text preprocessor. The existing preprocessor was preserved.",
                    text);
                return;
            }

            promptTexts.Add(text);
            handlingTextChange = true;
            text.textPreprocessor = this;
            // Always re-mark dirty: an already-registered label may have just
            // been handed a different token that still needs resolving.
            text.SetVerticesDirty();
            text.SetLayoutDirty();
            handlingTextChange = false;
        }

        private void Register(Text text)
        {
            if (text == null
                || string.IsNullOrEmpty(text.text)
                || !text.text.Contains(InputPromptResolver.Marker))
            {
                return;
            }

            // The current text still holds tokens, so it is the newest template
            // rather than a value this runtime already resolved.
            legacyTemplates[text] = text.text;
            text.text = InputPromptResolver.Resolve(text.text);
        }

        private void RefreshRegisteredText()
        {
            promptTexts.RemoveWhere(text => text == null);
            foreach (TMP_Text text in promptTexts)
            {
                text.SetVerticesDirty();
                text.SetLayoutDirty();
            }

            var stale = new List<Text>();
            foreach (KeyValuePair<Text, string> pair in legacyTemplates)
            {
                if (pair.Key == null)
                    stale.Add(pair.Key);
                else
                    pair.Key.text = InputPromptResolver.Resolve(pair.Value);
            }
            for (int i = 0; i < stale.Count; i++)
                legacyTemplates.Remove(stale[i]);
        }
    }
}
