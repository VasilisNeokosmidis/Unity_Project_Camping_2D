using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class PegPanelController : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    [Header("UI Refs")]
    [SerializeField] RectTransform stakeImage;
    [SerializeField] Button lockAngleButton;
    [SerializeField] Slider pressureSlider;
    [SerializeField] Button hitButton;
    [SerializeField] Button nextPegButton;
    [SerializeField] TMP_Text angleText;
    [SerializeField] TMP_Text pressureText;
    [SerializeField] TMP_Text feedbackText;
    [SerializeField] Image[] pegDots;

    [Header("Angle Rules")]
    [Range(-180,180)] public float startingAngleDeg = 45f;
    [Range(-180,180)] public float targetAngleDeg = 0f;
    public float angleToleranceDeg = 3f;

    [Header("Pressure Rules")]
    [Range(0f,1f)] public float pressureTarget = 0.30f;     // 30%
    [Range(0f,1f)] public float pressureTolerance = 0.00f;  // exact 30

    [Header("Flow")]
    public int totalPegs = 4;

    [Header("Lock Button Visuals")]
    [SerializeField] Image   lockBtnBackground;
    [SerializeField] TMP_Text lockBtnLabel;
    [SerializeField] Color   lockBtnColor_NotReady = new Color(0.85f, 0.1f, 0.1f);
    [SerializeField] Color   lockBtnColor_Ready    = new Color(0.1f, 0.7f, 0.2f);
    [SerializeField] Color   lockBtnTextColor      = Color.white;

    int currentPegIndex = 0;
    bool angleLocked = false;
    float currentAngle;

    [Header("Angle Snapping / Visual")]
    [SerializeField] float snapStepDeg = 25f;
    [SerializeField] float visualZeroOffsetDeg = 0f;

    [Header("Completion UI / Tent Swap")]
    [SerializeField] GameObject completionPanel;     // inactive by default
    [SerializeField] Button completionCloseButton;
    [SerializeField] GameObject finishedTentPrefab;
    GameObject targetTentInstance;

    // --- Hit button visuals (cached, not serialized)
    TMP_Text hitBtnLabel;
    Graphic  hitBtnBackground;

    [Header("Hit Animation")]
    [SerializeField] float hitDownPixels = 20f;
    [SerializeField] float hitDownTime  = 0.08f;
    [SerializeField] float hitUpTime    = 0.12f;

    Vector2 stakeBasePos;
    bool isHitAnimating = false;

    CanvasGroup _cg;

    [Header("Player Placement After Build")]
    [SerializeField] Transform player;            // drag your Player root here
    [SerializeField] Vector2   spawnOffset = new Vector2(0f, -0.1f); // tweak in Inspector
    [SerializeField] float     triggerCooldown = 0.25f;              // grace period

    void Awake()
    {
        if (lockAngleButton) lockAngleButton.onClick.AddListener(OnLockAngle);
        if (hitButton)       hitButton.onClick.AddListener(OnHitStake);
        if (nextPegButton)   nextPegButton.onClick.AddListener(OnNextPeg);

        if (hitButton)
        {
            hitBtnBackground = hitButton.targetGraphic;
            hitBtnLabel = hitButton.GetComponentInChildren<TMP_Text>(true);
            hitButton.transition = Selectable.Transition.None;     // ensure our colors show
        }
        if (lockAngleButton) lockAngleButton.transition = Selectable.Transition.None;

        if (pressureSlider)
        {
            pressureSlider.onValueChanged.AddListener(_ =>
            {
                if (pressureText) pressureText.text = $"Pressure: {Mathf.RoundToInt(pressureSlider.value * 100f)}%";
                UpdateHitButtonVisual();
            });
        }

        // ensure a CanvasGroup exists for fade/disable
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
    }

    void OnEnable()
    {
        if (completionPanel) completionPanel.SetActive(false);
        ResetForPeg();
        RefreshUI();
        UpdateLockButtonVisual();
        UpdateHitButtonVisual();
        ShowStakeUI(true); // make sure the stake-config UI is visible/interactive

        DisablePlayerControl();
    }

    public void SetTargetTent(GameObject tentGO) => targetTentInstance = tentGO;

    public void OnPointerDown(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (angleLocked || !stakeImage) return;

        Vector2 center = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, stakeImage.position);
        Vector2 mouse  = eventData.position;
        Vector2 dir    = mouse - center;

        float rawAngle = Vector2.SignedAngle(Vector2.up, dir);
        currentAngle = Mathf.Round(rawAngle / snapStepDeg) * snapStepDeg;
        stakeImage.localRotation = Quaternion.Euler(0, 0, currentAngle + visualZeroOffsetDeg);

        if (angleText) angleText.text = $"Angle: {Mathf.RoundToInt(Mathf.DeltaAngle(0f, currentAngle))}°";
        SetFeedback("");
        UpdateLockButtonVisual();
    }

    void OnLockAngle()
    {
        if (!IsAngleCorrect() || angleLocked) return;
        angleLocked = true;

        SetFeedback($"Angle OK ({Mathf.RoundToInt(Norm180(currentAngle))}°). Choose pressure.");

        if (pressureSlider) pressureSlider.interactable = true;
        if (hitButton)      hitButton.interactable = true;
        if (lockAngleButton) lockAngleButton.interactable = false;

        UpdateHitButtonVisual();
    }

    void OnHitStake()
    {
        if (stakeImage && !isHitAnimating) StartCoroutine(HitAnim());

        bool pressureOk = IsPressureCorrect();

        if (pressureOk)
        {
            SetFeedback("Correct pressure! Proceed to next peg.");
            if (nextPegButton) nextPegButton.interactable = true;

            if (pegDots != null && currentPegIndex < pegDots.Length)
            {
                pegDots[currentPegIndex].color = Color.green;
                var c = pegDots[currentPegIndex].color; c.a = 1f; pegDots[currentPegIndex].color = c;
            }
        }
        else
        {
            SetFeedback("Wrong pressure. Aim 30%.");
            if (nextPegButton) nextPegButton.interactable = false;
        }

        UpdateHitButtonVisual();
    }

    System.Collections.IEnumerator HitAnim()
    {
        isHitAnimating = true;

        Vector2 start = stakeBasePos;
        Vector2 down  = stakeBasePos + Vector2.down * hitDownPixels;

        float t = 0f;
        while (t < hitDownTime)
        {
            t += Time.deltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / hitDownTime), 2f);
            stakeImage.anchoredPosition = Vector2.LerpUnclamped(start, down, e);
            yield return null;
        }

        t = 0f;
        while (t < hitUpTime)
        {
            t += Time.deltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / hitUpTime), 2f);
            stakeImage.anchoredPosition = Vector2.LerpUnclamped(down, start, e);
            yield return null;
        }

        stakeImage.anchoredPosition = start;
        isHitAnimating = false;
    }

    void OnNextPeg()
    {
        currentPegIndex++;
        if (currentPegIndex >= totalPegs)
        {
            // Hide stake-config UI and show completion panel
            ShowStakeUI(false);
            if (completionPanel) completionPanel.SetActive(true);

            if (completionCloseButton)
            {
                completionCloseButton.onClick.RemoveAllListeners();
                completionCloseButton.onClick.AddListener(CompleteTentAndClose);
            }

            if (nextPegButton) nextPegButton.interactable = false;
            if (hitButton)     hitButton.interactable = false;

            SetFeedback("All pegs completed!");
            return;
        }

        ResetForPeg();
        RefreshUI();
        UpdateLockButtonVisual();
        UpdateHitButtonVisual();
        SetFeedback($"Next peg ({currentPegIndex + 1}/{totalPegs})");
    }

    void CompleteTentAndClose()
    {
        if (completionPanel) completionPanel.SetActive(false);

        GameObject finished = null;

        if (targetTentInstance && finishedTentPrefab)
        {
            var src = targetTentInstance.transform;

            // Capture local transform & hierarchy so replacement is seamless
            Transform parent = src.parent;
            int sibling = src.GetSiblingIndex();
            Vector3 lPos = src.localPosition;
            Quaternion lRot = src.localRotation;
            Vector3 lScale = src.localScale;

            // Copy a representative renderer’s sorting (optional)
            var srcSR = targetTentInstance.GetComponentInChildren<SpriteRenderer>(true);
            string srcLayer = srcSR ? srcSR.sortingLayerName : null;
            int srcOrder = srcSR ? srcSR.sortingOrder : 0;

            // Optional: anchor alignment
            Transform srcAnchor = src.Find("PlacementAnchor");
            Vector3 srcAnchorPos = srcAnchor ? srcAnchor.position : Vector3.zero;

            // Destroy the temp tent
            Destroy(targetTentInstance);

            // Spawn finished tent in the same place in hierarchy
            finished = Instantiate(finishedTentPrefab, parent);
            finished.transform.SetSiblingIndex(sibling);
            finished.transform.localPosition = lPos;
            finished.transform.localRotation = lRot;
            finished.transform.localScale = lScale;

            // Re-align using anchors if both exist
            Transform dstAnchor = finished.transform.Find("PlacementAnchor");
            if (srcAnchor && dstAnchor)
            {
                Vector3 delta = srcAnchorPos - dstAnchor.position;
                finished.transform.position += delta;
            }

            // Keep sorting consistent (optional)
            var dstSR = finished.GetComponentInChildren<SpriteRenderer>(true);
            if (srcSR && dstSR)
            {
                dstSR.sortingLayerName = srcLayer;
                dstSR.sortingOrder = srcOrder;
            }
        }

        // -------- Place player in front of the tent (no cooldown) --------
        if (player && finished)
        {
            // 1) Use EntranceSpawn child if present (recommended)
            Transform entranceSpawn = finished.transform.Find("EntranceSpawn");

            Vector3 targetPos;
            if (entranceSpawn)
            {
                targetPos = entranceSpawn.position + (Vector3)spawnOffset;
            }
            else
            {
                // 2) Fallbacks: try tent bounds; else tent position
                var sr = finished.GetComponentInChildren<SpriteRenderer>(true);
                if (sr)
                {
                    var b = sr.bounds;
                    // “Front” = just below the bottom of the tent sprite; tweak with spawnOffset
                    targetPos = new Vector3(b.center.x, b.min.y - 0.1f, player.position.z) + (Vector3)spawnOffset;
                }
                else
                {
                    targetPos = finished.transform.position + (Vector3)spawnOffset;
                }
            }

            // Move player (works for Transform or Rigidbody2D)
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb) rb.position = targetPos; else player.position = targetPos;
        }

        // Reset stake UI state and close panel
        currentPegIndex = 0;
        angleLocked = false;
        targetTentInstance = null;
        gameObject.SetActive(false);

        EnablePlayerControl();
    }

// at the top of PegPanelController
[SerializeField] MonoBehaviour playerMovementScript; // e.g. TopDownController (toggle via .enabled)
[SerializeField] Rigidbody2D  playerRb;              // optional: also freeze physics
// (Optional, if you use the new Input System)
// [SerializeField] UnityEngine.InputSystem.PlayerInput playerInput;
// [SerializeField] string gameplayActionMap = "Gameplay";

void DisablePlayerControl()
{
    if (playerMovementScript) playerMovementScript.enabled = false;

    if (playerRb)
    {
        playerRb.linearVelocity = Vector2.zero;
        playerRb.angularVelocity = 0f;
        // Keep rotation frozen; stop all motion while UI is up
        playerRb.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    // if (playerInput) playerInput.SwitchCurrentActionMap("UI"); // optional
}

void EnablePlayerControl()
{
    if (playerMovementScript) playerMovementScript.enabled = true;

    if (playerRb)
    {
        playerRb.constraints = RigidbodyConstraints2D.FreezeRotation; // unfreeze movement, keep rotation locked
    }

    // if (playerInput) playerInput.SwitchCurrentActionMap(gameplayActionMap); // optional
}





    void ResetForPeg()
    {
        angleLocked = false;

        currentAngle = startingAngleDeg;
        if (stakeImage)
        {
            stakeImage.localRotation = Quaternion.Euler(0, 0, currentAngle + visualZeroOffsetDeg);
            stakeBasePos = stakeImage.anchoredPosition; // for hit anim
        }

        if (pressureSlider)
        {
            pressureSlider.minValue = 0f;
            pressureSlider.maxValue = 1f;
            pressureSlider.value = 0.5f;
            pressureSlider.interactable = false;
        }

        if (hitButton) hitButton.interactable = false;
        if (nextPegButton) nextPegButton.interactable = false;

        if (pegDots != null)
        {
            for (int i = 0; i < pegDots.Length; i++)
            {
                bool done = i < currentPegIndex;
                pegDots[i].color = done ? Color.green : new Color(1, 1, 1, 0.35f);
            }
        }

        if (angleText)    angleText.text = $"Angle: {Mathf.RoundToInt(Norm180(currentAngle))}°";
        if (pressureText) pressureText.text = $"Pressure: {Mathf.RoundToInt((pressureSlider ? pressureSlider.value : 0.5f) * 100)}%";
        SetFeedback("");
    }

    void RefreshUI()
    {
        if (lockAngleButton) lockAngleButton.interactable = false;
        if (hitButton)       hitButton.interactable = false;
        if (nextPegButton)   nextPegButton.interactable = false;
    }

    bool IsAngleCorrect() => Mathf.RoundToInt(currentAngle) == 0;

    bool IsPressureCorrect()
    {
        if (!pressureSlider) return false;
        int pct = Mathf.RoundToInt(pressureSlider.value * 100f); // 0..100
        return pct == 30;
    }

    void UpdateLockButtonVisual()
    {
        bool ready = !angleLocked && IsAngleCorrect();

        if (lockAngleButton) lockAngleButton.interactable = ready;
        if (lockBtnBackground) lockBtnBackground.color = ready ? lockBtnColor_Ready : lockBtnColor_NotReady;
        if (lockBtnLabel)      lockBtnLabel.color = lockBtnTextColor;
    }

    void UpdateHitButtonVisual()
    {
        bool ready = angleLocked && IsPressureCorrect();

        if (hitButton)        hitButton.interactable = ready;
        if (hitBtnBackground) hitBtnBackground.color = ready ? new Color(0.1f,0.7f,0.2f) : new Color(0.85f,0.1f,0.1f);
        if (hitBtnLabel)      hitBtnLabel.color = Color.white;
    }

    float Norm180(float deg) => Mathf.DeltaAngle(0f, deg);

    void SetFeedback(string s)
    {
        if (feedbackText) feedbackText.text = s;
    }

    void Update()
    {
        if (pressureSlider && pressureText)
            pressureText.text = $"Pressure: {Mathf.RoundToInt(pressureSlider.value * 100f)}%";
    }

    // --- CanvasGroup show/hide for stake-config UI ---
    void ShowStakeUI(bool show)
    {
        if (_cg == null) return;
        _cg.alpha = show ? 1f : 0f;
        _cg.interactable = show;
        _cg.blocksRaycasts = show;
    }
}
