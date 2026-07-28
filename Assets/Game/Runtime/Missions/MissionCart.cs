using UnityEngine;

namespace Supernova.Missions
{
    [DisallowMultipleComponent]
    public sealed class MissionCart : MonoBehaviour
    {
        public static MissionCart Create(Vector3 position)
        {
            GameObject root = new GameObject("Mission Cart");
            root.transform.position = position;
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
            return root.AddComponent<MissionCart>();
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
