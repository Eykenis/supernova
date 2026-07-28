using UnityEngine;

namespace Supernova.Missions
{
    [DisallowMultipleComponent]
    public sealed class MissionCart : MonoBehaviour
    {
        public static MissionCart Create(Vector3 position)
        {
            return Create(position, Quaternion.identity);
        }

        public static MissionCart Create(
            Vector3 position,
            Quaternion rotation)
        {
            GameObject root = new GameObject("Mission Cart");
            root.transform.SetPositionAndRotation(position, rotation);
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 35f;
            body.drag = 1.2f;
            body.angularDrag = 2f;
            BoxCollider floor = root.AddComponent<BoxCollider>();
            floor.center = new Vector3(0f, 0.25f, 0f);
            floor.size = new Vector3(1.8f, 0.2f, 2.3f);

            CreatePart(root.transform, "Tray", new Vector3(0f, 0.25f, 0f),
                new Vector3(1.8f, 0.16f, 2.3f), new Color(0.12f, 0.24f, 0.28f));
            CreateWall(root.transform, "Left Rail", new Vector3(-0.88f, 0.75f, 0f),
                new Vector3(0.12f, 1f, 2.3f));
            CreateWall(root.transform, "Right Rail", new Vector3(0.88f, 0.75f, 0f),
                new Vector3(0.12f, 1f, 2.3f));
            CreateWall(root.transform, "Back Rail", new Vector3(0f, 0.75f, -1.1f),
                new Vector3(1.7f, 1f, 0.12f));
            CreatePart(root.transform, "Beacon", new Vector3(0f, 1.35f, -1.1f),
                new Vector3(0.25f, 0.25f, 0.25f), new Color(0.1f, 0.9f, 1f));
            GameObject handle = CreatePart(
                root.transform,
                "Tow Handle",
                new Vector3(0f, 0.85f, 1.45f),
                new Vector3(0.9f, 0.12f, 0.12f),
                new Color(0.95f, 0.62f, 0.12f));
            BoxCollider handleCollider = handle.AddComponent<BoxCollider>();
            handleCollider.size = new Vector3(1.35f, 2f, 2f);
            handle.AddComponent<Supernova.Gameplay.CartHandle>().Configure(body);
            MissionCart cart = root.AddComponent<MissionCart>();
            CreateCargoZone(
                root.transform,
                new Vector3(0f, 0.9f, 0f),
                new Vector3(1.55f, 1.2f, 2f));
            return cart;
        }

        public static MissionCart ConfigureExisting(
            GameObject root,
            Vector3 position,
            Quaternion rotation)
        {
            if (root == null)
            {
                return Create(position, rotation);
            }

            MissionCart cart = root.GetComponent<MissionCart>();
            if (cart == null)
            {
                cart = root.AddComponent<MissionCart>();
            }

            Rigidbody body = root.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = root.AddComponent<Rigidbody>();
            }

            root.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            body.WakeUp();
            cart.EnsureCargoZone();
            return cart;
        }

        private void EnsureCargoZone()
        {
            if (GetComponentInChildren<CartCargoValueZone>(true) != null)
            {
                return;
            }

            BoxCollider[] boxes = GetComponentsInChildren<BoxCollider>(true);
            BoxCollider cargoReference = null;
            float largestVolume = 0f;
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxCollider candidate = boxes[i];
                if (candidate.isTrigger)
                {
                    continue;
                }

                Vector3 size = candidate.size;
                float volume = size.x * size.y * size.z;
                if (volume > largestVolume)
                {
                    largestVolume = volume;
                    cargoReference = candidate;
                }
            }

            if (cargoReference == null)
            {
                CreateCargoZone(
                    transform,
                    new Vector3(0f, 0.9f, 0f),
                    new Vector3(1.5f, 1.2f, 2f));
                return;
            }

            Vector3 referenceSize = cargoReference.size;
            Vector3 centre = cargoReference.center
                + Vector3.up * referenceSize.y * 0.65f;
            Vector3 zoneSize = new Vector3(
                referenceSize.x * 0.82f,
                Mathf.Max(0.5f, referenceSize.y * 0.85f),
                referenceSize.z * 0.82f);
            CreateCargoZone(cargoReference.transform, centre, zoneSize);
        }

        private static void CreateCargoZone(
            Transform parent,
            Vector3 localCentre,
            Vector3 localSize)
        {
            GameObject zoneObject = new GameObject(
                "Cargo Value Protection Zone");
            zoneObject.transform.SetParent(parent, false);
            BoxCollider trigger = zoneObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = localCentre;
            trigger.size = localSize;
            zoneObject.AddComponent<CartCargoValueZone>();
        }

        private static void CreateWall(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            GameObject wall = CreatePart(parent, name, position, scale, new Color(0.12f, 0.24f, 0.28f));
            wall.AddComponent<BoxCollider>();
        }

        private static GameObject CreatePart(
            Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Object.Destroy(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().material.color = color;
            return part;
        }
    }
}
