namespace Supernova.Inputs
{
    public readonly struct GameInputBindingInfo
    {
        public GameInputBindingInfo(
            GameInputActionId actionId,
            int bindingIndex,
            string mapName,
            string actionName,
            string bindingName,
            string displayString,
            string controlPath = null)
        {
            ActionId = actionId;
            BindingIndex = bindingIndex;
            MapName = mapName;
            ActionName = actionName;
            BindingName = bindingName;
            DisplayString = displayString;
            ControlPath = controlPath;
        }

        public GameInputActionId ActionId { get; }
        public int BindingIndex { get; }
        public string MapName { get; }
        public string ActionName { get; }
        public string BindingName { get; }
        public string DisplayString { get; }
        public string ControlPath { get; }

        public string Label => string.IsNullOrEmpty(BindingName)
            ? ActionName
            : ActionName + " / " + BindingName;
    }
}
