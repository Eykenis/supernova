using Supernova.Missions;
using UnityEngine;

namespace Supernova.Shop
{
    /// <summary>Crosshair interaction for one product stand in the Home shop.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeShopController : MonoBehaviour
    {
        [SerializeField] private ShopProductProfile productProfile;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private Vector3 productLocalPosition =
            new Vector3(0f, 1.25f, 0f);
        [SerializeField, Min(0.5f)] private float interactionDistance = 5f;
        [SerializeField] private KeyCode purchaseKey = KeyCode.E;

        private ShopProductDisplay productDisplay;

        public ShopProductProfile ProductProfile => productProfile;
        public ShopProductDisplay ProductDisplay => productDisplay;
        public float InteractionDistance =>
            Mathf.Max(0.5f, interactionDistance);

        private void Awake()
        {
            BuildProductDisplay();
        }

        private void Update()
        {
            bool targeted = IsProductTargeted();
            if (productDisplay != null)
                productDisplay.SetTargeted(targeted);
            if (targeted && Input.GetKeyDown(purchaseKey))
                TryPurchaseTargetedProduct();
        }

        public void Configure(ShopProductProfile profile)
        {
            productProfile = profile;
            BuildProductDisplay();
        }

        public ShopPurchaseResult TryPurchaseTargetedProduct()
        {
            ShopPurchaseResult result =
                PlayerEconomy.TryPurchase(productProfile);
            if (productDisplay != null)
                productDisplay.RefreshView();
            RefreshMissionPrompt(result);
            return result;
        }

        public bool IsProductTargeted()
        {
            if (productDisplay == null
                || productDisplay.TargetCollider == null)
            {
                return false;
            }

            Camera camera = ResolveCamera();
            if (camera == null)
                return false;

            Ray ray = camera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    InteractionDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return hit.collider == productDisplay.TargetCollider
                || hit.collider.transform.IsChildOf(
                    productDisplay.transform);
        }

        private void BuildProductDisplay()
        {
            if (productDisplay == null)
            {
                Transform existing =
                    transform.Find("Shop Product");
                GameObject productObject;
                if (existing != null)
                {
                    productObject = existing.gameObject;
                }
                else
                {
                    productObject = new GameObject("Shop Product");
                    productObject.transform.SetParent(transform, false);
                }

                productDisplay =
                    productObject.GetComponent<ShopProductDisplay>();
                if (productDisplay == null)
                    productDisplay =
                        productObject.AddComponent<ShopProductDisplay>();
            }

            productDisplay.transform.localPosition =
                productLocalPosition;
            productDisplay.transform.localRotation =
                Quaternion.identity;
            productDisplay.transform.localScale = Vector3.one;
            productDisplay.Configure(
                productProfile,
                ResolveCamera());
        }

        private Camera ResolveCamera()
        {
            if (interactionCamera == null
                || !interactionCamera.isActiveAndEnabled)
            {
                interactionCamera = Camera.main;
            }
            if (interactionCamera == null)
                interactionCamera = FindObjectOfType<Camera>();
            return interactionCamera;
        }

        private static void RefreshMissionPrompt(
            ShopPurchaseResult result)
        {
            MissionGameLoop missionLoop =
                FindObjectOfType<MissionGameLoop>();
            if (missionLoop == null)
                return;

            switch (result)
            {
                case ShopPurchaseResult.Purchased:
                    missionLoop.SetPrompt(
                        "PURCHASE COMPLETE    BALANCE  $"
                        + PlayerEconomy.Credits);
                    break;
                case ShopPurchaseResult.InsufficientFunds:
                    missionLoop.SetPrompt(
                        "INSUFFICIENT FUNDS    BALANCE  $"
                        + PlayerEconomy.Credits);
                    break;
                default:
                    missionLoop.SetPrompt(
                        "SHOP ONLINE    BALANCE  $"
                        + PlayerEconomy.Credits);
                    break;
            }
        }
    }
}
