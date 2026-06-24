using UnityEngine;

public class MammothPersonality : MonoBehaviour
{
    [Header("Generated Personality")]
    [Range(0f, 1f)] public float bravery;
    [Range(0f, 1f)] public float aggression;
    [Range(0f, 1f)] public float curiosity;
    [Range(0f, 1f)] public float fearfulness;

    [Header("Behaviour Thresholds")]
    [Range(0f, 1f)] public float panicHealthThreshold = 0.3f;
    [Range(0f, 1f)] public float enragedHealthThreshold = 0.45f;

    [Header("Runtime Emotion")]
    [Range(0f, 1f)] public float anger;
    [Range(0f, 1f)] public float fear;
    [Range(0f, 1f)] public float alertness;
    [Range(0f, 1f)] public float pain;
    [Range(0f, 1f)] public float fatigue;

    [Header("Generation")]
    [SerializeField] private bool randomizeOnAwake = true;
    [SerializeField] private float angerSettleRate = 0.04f;
    [SerializeField] private float fearSettleRate = 0.035f;
    [SerializeField] private float alertnessSettleRate = 0.05f;
    [SerializeField] private float painSettleRate = 0.025f;
    [SerializeField] private float fatigueRecoveryRate = 0.08f;
    [SerializeField] private float fatigueBuildRate = 0.16f;

    private MammothState state;

    private void Awake()
    {
        state = GetComponent<MammothState>();

        if (randomizeOnAwake)
        {
            RandomizePersonality();
        }
    }

    private void Update()
    {
        float calmAnger = aggression * 0.18f;
        float calmFear = fearfulness * 0.18f;
        float calmAlertness = 0.06f + curiosity * 0.14f;

        anger = Mathf.MoveTowards(anger, calmAnger, angerSettleRate * Time.deltaTime);
        fear = Mathf.MoveTowards(fear, calmFear, fearSettleRate * Time.deltaTime);
        alertness = Mathf.MoveTowards(alertness, calmAlertness, alertnessSettleRate * Time.deltaTime);
        pain = Mathf.MoveTowards(pain, 0f, painSettleRate * Time.deltaTime);

        float fatigueDeltaPerSecond = GetFatigueDeltaForCurrentAction();
        float fatigueStep = (fatigueDeltaPerSecond >= 0f ? fatigueBuildRate : fatigueRecoveryRate) * Time.deltaTime;
        fatigue = Mathf.Clamp01(fatigue + fatigueDeltaPerSecond * fatigueStep);
    }

    public void RandomizePersonality()
    {
        bravery = Random.Range(0.15f, 1f);
        aggression = Random.Range(0.15f, 1f);
        curiosity = Random.Range(0.1f, 0.8f);

        fearfulness = 1f - bravery;

        panicHealthThreshold = Mathf.Lerp(0.2f, 0.55f, fearfulness);
        enragedHealthThreshold = Mathf.Lerp(0.65f, 0.3f, bravery);

        ResetRuntimeEmotion();

        Debug.Log(
            $"Mammoth personality generated | Bravery: {bravery:0.00}, " +
            $"Aggression: {aggression:0.00}, Fearfulness: {fearfulness:0.00}"
        );
    }

    public void ResetRuntimeEmotion()
    {
        anger = aggression * 0.25f;
        fear = fearfulness * 0.25f;
        alertness = 0.05f + curiosity * 0.1f;
        pain = 0f;
        fatigue = 0f;
    }

    public void AddAnger(float amount)
    {
        anger = Mathf.Clamp01(anger + amount);
    }

    public void AddFear(float amount)
    {
        fear = Mathf.Clamp01(fear + amount);
    }

    public void AddAlertness(float amount)
    {
        alertness = Mathf.Clamp01(alertness + amount);
    }

    public void AddPain(float amount)
    {
        pain = Mathf.Clamp01(pain + amount);
    }

    public void AddFatigue(float amount)
    {
        fatigue = Mathf.Clamp01(fatigue + amount);
    }

    public bool IsExhausted => fatigue >= 0.75f;

    public float GetFightDrive()
    {
        return Mathf.Clamp01(
            (bravery * 0.34f) +
            (aggression * 0.26f) +
            (anger * 0.18f) +
            (alertness * 0.14f) -
            (fear * 0.18f) -
            (pain * 0.08f) -
            (fatigue * 0.12f)
        );
    }

    public float GetFlightDrive()
    {
        return Mathf.Clamp01(
            (fearfulness * 0.34f) +
            (fear * 0.24f) +
            (pain * 0.18f) +
            (fatigue * 0.13f) +
            (alertness * 0.09f) -
            (bravery * 0.18f) -
            (anger * 0.08f)
        );
    }

    private float GetFatigueDeltaForCurrentAction()
    {
        if (state == null)
        {
            return -0.2f;
        }

        switch (state.currentAction)
        {
            case MammothActionType.Charge:
                return 1f;
            case MammothActionType.RunAway:
                return 0.8f;
            case MammothActionType.ChasePlayer:
                return 0.6f;
            case MammothActionType.NormalAttack:
            case MammothActionType.Stomp:
            case MammothActionType.TwistAttack:
                return 0.45f;
            case MammothActionType.Investigate:
                return 0.12f;
            case MammothActionType.Roam:
                return -0.12f;
            case MammothActionType.Idle:
            case MammothActionType.Recover:
                return -0.35f;
            case MammothActionType.Threaten:
                return -0.05f;
            default:
                return -0.1f;
        }
    }
}
