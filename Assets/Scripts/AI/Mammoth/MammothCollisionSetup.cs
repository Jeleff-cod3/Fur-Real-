using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class MammothCollisionSetup : MonoBehaviour
{
    [Header("Layer")]
    [SerializeField] private string enemyLayerName = "Enemy";
    [SerializeField] private bool applyLayerToChildren = true;

    [Header("Root Collider")]
    [SerializeField] private Vector3 colliderCenter = new Vector3(0f, 1.5f, 0f);
    [SerializeField] private Vector3 colliderSize = new Vector3(4f, 3f, 3f);

    [Header("Visual")]
    [SerializeField] private string visualChildName = "MammothVisual";
    [SerializeField] private bool disableChildColliders = true;

    private void Awake()
    {
        ApplySetup();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            ApplySetup();
        }
    }

    public void ApplySetup()
    {
        ApplyEnemyLayer();
        EnsureRootCollider();
        EnsureRigidbody();
        EnsureNavMeshAgentSettings();
        EnsureEnemyHealth();
        DisableVisualChildColliders();
    }

    private void ApplyEnemyLayer()
    {
        int enemyLayer = LayerMask.NameToLayer(enemyLayerName);

        if (enemyLayer < 0)
        {
            Debug.LogWarning($"MammothCollisionSetup: layer '{enemyLayerName}' does not exist. Create it in Tags and Layers.");
            return;
        }

        gameObject.layer = enemyLayer;

        if (!applyLayerToChildren)
        {
            return;
        }

        Transform[] children = GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            child.gameObject.layer = enemyLayer;
        }
    }

    private void EnsureRootCollider()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();

        if (boxCollider == null)
        {
            boxCollider = gameObject.AddComponent<BoxCollider>();
        }

        boxCollider.isTrigger = false;
        boxCollider.center = colliderCenter;
        boxCollider.size = colliderSize;
        boxCollider.enabled = true;
    }

    private void EnsureRigidbody()
    {
        Rigidbody rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void EnsureNavMeshAgentSettings()
    {
        NavMeshAgent agent = GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            return;
        }

        agent.baseOffset = 0f;
        agent.radius = 1.5f;
        agent.height = 3f;
        agent.stoppingDistance = 3.5f;
    }

    private void EnsureEnemyHealth()
    {
        EnemyHealth enemyHealth = GetComponent<EnemyHealth>();

        if (enemyHealth == null)
        {
            gameObject.AddComponent<EnemyHealth>();
        }
    }

    private void DisableVisualChildColliders()
    {
        if (!disableChildColliders)
        {
            return;
        }

        Transform visual = transform.Find(visualChildName);

        if (visual == null)
        {
            return;
        }

        Collider[] childColliders = visual.GetComponentsInChildren<Collider>(true);

        foreach (Collider childCollider in childColliders)
        {
            childCollider.enabled = false;
        }
    }
}