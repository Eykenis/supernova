using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Shop
{
    /// <summary>Authoring data for one product displayed in the Home shop.</summary>
    [CreateAssetMenu(
        fileName = "ShopProduct",
        menuName = "Supernova/Shop/Product Profile")]
    public sealed class ShopProductProfile : ScriptableObject
    {
        [SerializeField] private string productId = "product";
        [SerializeField] private string displayName = "Product";
        [SerializeField, Min(0)] private int price;
        [SerializeField] private PlayerInventoryItem grantedItem;
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
        public PlayerInventoryItem GrantedItem => grantedItem;
        public GameObject DisplayPrefab => displayPrefab;
        public Material WireframeMaterial => wireframeMaterial;
        public Vector3 DisplayLocalPosition => displayLocalPosition;
        public Vector3 DisplayLocalEulerAngles => displayLocalEulerAngles;
        public Vector3 DisplayLocalScale => displayLocalScale;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ProductId)
            && grantedItem != PlayerInventoryItem.Empty
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
            grantedItem = item;
            displayPrefab = prefab;
            wireframeMaterial = outlineMaterial;
        }
    }
}
