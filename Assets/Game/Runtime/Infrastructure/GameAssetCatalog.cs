using System;
using System.Collections.Generic;
using Supernova.Missions;
using Supernova.UI;
using UnityEngine;

namespace Supernova.Infrastructure
{
    [Serializable]
    public sealed class MissionAssetReferences
    {
        [SerializeField] private LevelConfiguration defaultLevel;
        [SerializeField] private List<LevelConfiguration> levels =
            new List<LevelConfiguration>();
        [SerializeField] private Font uiFont;

        public LevelConfiguration DefaultLevel => defaultLevel;
        public IReadOnlyList<LevelConfiguration> Levels => levels;
        public Font UiFont => uiFont;
        public bool IsComplete
        {
            get
            {
                if (defaultLevel == null || uiFont == null
                    || levels == null || levels.Count == 0
                    || levels[0] != defaultLevel)
                {
                    return false;
                }

                for (int i = 0; i < levels.Count; i++)
                {
                    if (levels[i] == null)
                        return false;
                }
                return true;
            }
        }
    }

    [Serializable]
    public sealed class UiAssetReferences
    {
        [Header("Views")]
        [SerializeField] private GameObject mainMenuPrefab;
        [SerializeField] private UiDesignTokens designTokens;
        [SerializeField] private PausePortraitSettings pausePortraitSettings;
        [SerializeField] private EquipmentIconCatalog equipmentIcons;
        [SerializeField] private EquipmentPortraitSettings equipmentPortraitSettings;

        [Header("Pause Portrait")]
        [SerializeField] private Material pauseBodyMaterial;
        [SerializeField] private Material pauseBackgroundMaterial;

        [Header("Sci-Fi Skin")]
        [SerializeField] private Sprite primaryFrame;
        [SerializeField] private Sprite wideFrame;
        [SerializeField] private Sprite slotFrame;
        [SerializeField] private Sprite thinFrame;
        [SerializeField] private Sprite hudPanelFrame;
        [SerializeField] private Sprite slotCleanFrame;
        [SerializeField] private Sprite buttonCleanFrame;
        [SerializeField] private Sprite progressCleanFrame;
        [SerializeField] private Sprite pauseCardFrame;
        [SerializeField] private Sprite loadingDial;
        [SerializeField] private Texture2D telemetryBackdrop;

        public GameObject MainMenuPrefab => mainMenuPrefab;
        public UiDesignTokens DesignTokens => designTokens;
        public PausePortraitSettings PausePortraitSettings => pausePortraitSettings;
        public EquipmentIconCatalog EquipmentIcons => equipmentIcons;
        public EquipmentPortraitSettings EquipmentPortraitSettings =>
            equipmentPortraitSettings;
        public Material PauseBodyMaterial => pauseBodyMaterial;
        public Material PauseBackgroundMaterial => pauseBackgroundMaterial;
        public Sprite PrimaryFrame => primaryFrame;
        public Sprite WideFrame => wideFrame;
        public Sprite SlotFrame => slotFrame;
        public Sprite ThinFrame => thinFrame;
        public Sprite HudPanelFrame => hudPanelFrame;
        public Sprite SlotCleanFrame => slotCleanFrame;
        public Sprite ButtonCleanFrame => buttonCleanFrame;
        public Sprite ProgressCleanFrame => progressCleanFrame;
        public Sprite PauseCardFrame => pauseCardFrame;
        public Sprite LoadingDial => loadingDial;
        public Texture2D TelemetryBackdrop => telemetryBackdrop;

        public bool IsComplete =>
            mainMenuPrefab != null
            && designTokens != null
            && pausePortraitSettings != null
            && equipmentIcons != null
            && equipmentPortraitSettings != null
            && pauseBodyMaterial != null
            && pauseBackgroundMaterial != null
            && primaryFrame != null
            && wideFrame != null
            && slotFrame != null
            && thinFrame != null
            && hudPanelFrame != null
            && slotCleanFrame != null
            && buttonCleanFrame != null
            && progressCleanFrame != null
            && pauseCardFrame != null
            && loadingDial != null
            && telemetryBackdrop != null;
    }

    [Serializable]
    public sealed class SceneLookupReferences
    {
        [SerializeField] private string mainMenuSceneName;
        [SerializeField] private string missionCellObjectName;
        [SerializeField] private string authoredCartObjectName;
        [SerializeField] private string pausePoseStateName;

        public string MainMenuSceneName => mainMenuSceneName;
        public string MissionCellObjectName => missionCellObjectName;
        public string AuthoredCartObjectName => authoredCartObjectName;
        public string PausePoseStateName => pausePoseStateName;

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(mainMenuSceneName)
            && !string.IsNullOrWhiteSpace(missionCellObjectName)
            && !string.IsNullOrWhiteSpace(authoredCartObjectName)
            && !string.IsNullOrWhiteSpace(pausePoseStateName);
    }

    [CreateAssetMenu(
        fileName = "GameAssetCatalog",
        menuName = "Supernova/Infrastructure/Game Asset Catalog")]
    public sealed class GameAssetCatalog : ScriptableObject
    {
        [SerializeField] private MissionAssetReferences missions =
            new MissionAssetReferences();
        [SerializeField] private UiAssetReferences ui =
            new UiAssetReferences();
        [SerializeField] private SceneLookupReferences sceneLookups =
            new SceneLookupReferences();

        private static GameAssetCatalog current;

        public static GameAssetCatalog Current => current;
        public MissionAssetReferences Missions => missions;
        public UiAssetReferences UI => ui;
        public SceneLookupReferences SceneLookups => sceneLookups;
        public bool IsComplete =>
            missions != null
            && missions.IsComplete
            && ui != null
            && ui.IsComplete
            && sceneLookups != null
            && sceneLookups.IsComplete;

        public static bool TryGet(out GameAssetCatalog catalog)
        {
            catalog = current;
            return catalog != null;
        }

        private void OnEnable()
        {
            current = this;
        }

        private void OnDisable()
        {
            if (current == this)
                current = null;
        }
    }
}
