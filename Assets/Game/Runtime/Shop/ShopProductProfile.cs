using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Shop
{
    public enum ShopProductGrantType
    {
        InventoryItem = 0,
        Upgrade = 1,
    }

    /// <summary>Authoring data for one product displayed in the Home shop.</summary>
    [CreateAssetMenu(
        fileName = "ShopProduct",
        menuName = "Supernova/Shop/Product Profile")]
    public sealed class ShopProductProfile : ScriptableObject
    {
        [SerializeField] private string productId = "product";
        [SerializeField] private string displayName = "Product";
        [SerializeField, Min(0)] private int price;
        [SerializeField] private ShopProductGrantType grantType;
        [SerializeField] private PlayerInventoryItem grantedItem;
        [SerializeField] private PlayerUpgrade grantedUpgrade;
        [SerializeField, Min(0f)] private float upgradeValue;
        [SerializeField] private GameObject displayPrefab;
        [SerializeField] private Material wireframeMaterial;
        [SerializeField] private Vector3 displayLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 displayLocalEulerAngles =
            new Vector3(0f, 90f, 0f);
        [SerializeField] private Vector3 displayLocalScale =
            Vector3.one * 3f;

        public string ProductId => string.IsNullOrWhiteSpace(productId)
            ? name
            : productId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;
        public int Price => Mathf.Max(0, price);
        public ShopProductGrantType GrantType => grantType;
        public PlayerInventoryItem GrantedItem => grantedItem;
        public PlayerUpgrade GrantedUpgrade => grantedUpgrade;
        public float UpgradeValue => Mathf.Max(0f, upgradeValue);
        public GameObject DisplayPrefab => displayPrefab;
        public Material WireframeMaterial => wireframeMaterial;
        public Vector3 DisplayLocalPosition => displayLocalPosition;
        public Vector3 DisplayLocalEulerAngles => displayLocalEulerAngles;
        public Vector3 DisplayLocalScale => displayLocalScale;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ProductId)
            && ((grantType == ShopProductGrantType.InventoryItem
                    && grantedItem != PlayerInventoryItem.Empty)
                || (grantType == ShopProductGrantType.Upgrade
                    && grantedUpgrade != PlayerUpgrade.None
                    && UpgradeValue > 0f))
            && displayPrefab != null
            && wireframeMaterial != null;

        public void Configure(
            string id,
            string productDisplayName,
            int productPrice,
            PlayerInventoryItem item,
            GameObject prefab,
            Material outlineMaterial)
        {
            productId = id;
            displayName = productDisplayName;
            price = Mathf.Max(0, productPrice);
            grantType = ShopProductGrantType.InventoryItem;
            grantedItem = item;
            grantedUpgrade = PlayerUpgrade.None;
            upgradeValue = 0f;
            displayPrefab = prefab;
            wireframeMaterial = outlineMaterial;
        }

        public void ConfigureUpgrade(
            string id,
            string productDisplayName,
            int productPrice,
            PlayerUpgrade upgrade,
            float value,
            GameObject prefab,
            Material outlineMaterial)
        {
            productId = id;
            displayName = productDisplayName;
            price = Mathf.Max(0, productPrice);
            grantType = ShopProductGrantType.Upgrade;
            grantedItem = PlayerInventoryItem.Empty;
            grantedUpgrade = upgrade;
            upgradeValue = Mathf.Max(0f, value);
            displayPrefab = prefab;
            wireframeMaterial = outlineMaterial;
        }

        public void ConfigureDisplayTransform(
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            displayLocalPosition = localPosition;
            displayLocalEulerAngles = localEulerAngles;
            displayLocalScale = localScale;
        }
    }
}
