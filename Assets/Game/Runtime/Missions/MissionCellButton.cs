using Supernova.Infrastructure;
using UnityEngine;

namespace Supernova.Missions
{
    [DisallowMultipleComponent]
    public sealed class MissionCellButton : MonoBehaviour
    {
        public const string ObjectName = "Cell Mission Console";

        [SerializeField] private bool homeMode;

        public bool IsHomeMode => homeMode;

        public static MissionCellButton Create(Transform cell, bool isHome)
        {
            if (cell == null) return null;

            MissionCellButton existing =
                cell.GetComponentInChildren<MissionCellButton>(true);
            if (existing != null)
            {
                existing.homeMode = isHome;
                return existing;
            }

            var consoleObject = new GameObject(ObjectName);
            consoleObject.transform.SetParent(cell, false);
            consoleObject.transform.localPosition = new Vector3(0f, 1.1f, 1.55f);
            consoleObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            consoleObject.layer = cell.gameObject.layer;

            MissionCellButton console =
                consoleObject.AddComponent<MissionCellButton>();
            console.homeMode = isHome;
            CreatePart(
                "Console Housing",
                PrimitiveType.Cube,
                consoleObject.transform,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.85f, 0.72f, 0.16f),
                new Color(0.08f, 0.13f, 0.16f));
            CreatePart(
                "Console Screen",
                PrimitiveType.Cube,
                consoleObject.transform,
                new Vector3(0f, 0.12f, 0.1f),
                Quaternion.identity,
                new Vector3(0.58f, 0.27f, 0.05f),
                new Color(0.08f, 0.8f, 0.92f));
            CreatePart(
                "Interaction Button",
                PrimitiveType.Cylinder,
                consoleObject.transform,
                new Vector3(0f, -0.19f, 0.16f),
                Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.15f, 0.07f, 0.15f),
                isHome
                    ? new Color(0.2f, 0.95f, 0.55f)
                    : new Color(1f, 0.55f, 0.12f));
            return console;
        }

        private static void CreatePart(
            string partName,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Color color)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            part.layer = parent.gameObject.layer;

            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null) partCollider.enabled = false;

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer == null) return;
            Material material = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.MissionCellConsoleMaterial
                : null;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
            else
            {
                Debug.LogError(
                    "Mission Cell console material is missing from the "
                    + "preloaded game asset catalog.",
                    parent);
            }
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }
    }
}
