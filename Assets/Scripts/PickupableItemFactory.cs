using UnityEngine;

public static class PickupableItemFactory
{
    private static readonly Vector3 StickHeldOffset = new Vector3(0.32f, 0.12f, 0.38f);
    private static readonly Vector3 StickHeldEuler = new Vector3(12f, 28f, 84f);
    private static readonly Vector3 RockHeldOffset = new Vector3(0.26f, 0.08f, 0.28f);
    private static readonly Vector3 RockHeldEuler = new Vector3(8f, 0f, 20f);

    private static Material stickMaterial;
    private static Material rockMaterial;

    public static PickupableItem CreateStick(Transform parent, Vector3 localPosition, Quaternion localRotation)
    {
        GameObject root = new GameObject("Stick");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = localRotation;

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.direction = 2;
        collider.radius = 0.08f;
        collider.height = 0.9f;

        Rigidbody rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        visual.transform.localScale = new Vector3(0.1f, 0.45f, 0.1f);
        Object.Destroy(visual.GetComponent<Collider>());

        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetStickMaterial();

        PickupableItem item = root.AddComponent<PickupableItem>();
        item.Configure(PickupItemType.Stick, StickHeldOffset, StickHeldEuler);
        item.SetWorldPose(root.transform.position, root.transform.rotation);
        return item;
    }

    public static PickupableItem CreateRock(
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 visualScale
    )
    {
        GameObject root = new GameObject("Rock");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = localRotation;

        SphereCollider collider = root.AddComponent<SphereCollider>();
        collider.radius = 0.34f;

        Rigidbody rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = visualScale;
        Object.Destroy(visual.GetComponent<Collider>());

        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetRockMaterial();

        PickupableItem item = root.AddComponent<PickupableItem>();
        item.Configure(PickupItemType.Rock, RockHeldOffset, RockHeldEuler);
        item.SetWorldPose(root.transform.position, root.transform.rotation);
        return item;
    }

    private static Material GetStickMaterial()
    {
        if (stickMaterial != null)
        {
            return stickMaterial;
        }

        stickMaterial = CreateLitMaterial(
            "StickPickupMaterial",
            new Color(0.46f, 0.29f, 0.13f)
        );
        return stickMaterial;
    }

    private static Material GetRockMaterial()
    {
        if (rockMaterial != null)
        {
            return rockMaterial;
        }

        rockMaterial = CreateLitMaterial(
            "RockPickupMaterial",
            new Color(0.58f, 0.56f, 0.54f)
        );
        return rockMaterial;
    }

    private static Material CreateLitMaterial(string materialName, Color baseColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.color = baseColor;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", baseColor);
        }

        return material;
    }
}
