using Supernova.Inputs;
using Supernova.Gameplay;
using Supernova.Missions;
using UnityEngine;

namespace Supernova.Shop
{
    /// <summary>Proximity interaction for one product stand in the Home shop.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeShopController : MonoBehaviour
    {
        [SerializeField] private ShopProductProfile productProfile;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private Vector3 productLocalPosition = Vector3.zero;
        [SerializeField, Min(0.5f)] private float interactionDistance = 2.4f;

        private ShopProductDisplay productDisplay;
        private PlayerToolController player;

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
            if (targeted && GameInput.Pressed(GameInputActionId.Interact))
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
            if (productDisplay == null)
                return false;
            if (player == null)
                player = FindObjectOfType<PlayerToolController>();
            return player != null
                && (player.transform.position - productDisplay.transform.position)
                    .sqrMagnitude
                    <= InteractionDistance * InteractionDistance;
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
                        "购买成功 -$"
                        + PlayerEconomy.Credits);
                    break;
                case ShopPurchaseResult.InsufficientFunds:
                    missionLoop.SetPrompt("余额不足");
                    break;
            }
        }
    }
}
