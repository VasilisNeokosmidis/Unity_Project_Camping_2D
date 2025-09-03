using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class MapPanelController : MonoBehaviour,
    IPointerMoveHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI")]
    [SerializeField] GameObject panel;
    [SerializeField] Button confirmButton;
    [SerializeField] Button cancelButton;
    [SerializeField] RawImage miniMapImage;

    [Header("Mini-map")]
    [SerializeField] Camera miniMapCamera;          // Orthographic top-down

    [Header("Placement")]
    [SerializeField] GameObject tentPrefab;
    [SerializeField] Transform worldParent;
    [SerializeField] float worldZ = 0f;             // 2D plane (usually 0)
    [SerializeField, Range(0f, 1f)] float ghostAlpha = 0.5f;

    [Header("No-overlap settings")]
    [SerializeField] float minTentSpacing = 0.8f;   // distance between tent centers
    [SerializeField] Texture2D cursorForbidden;     // optional “forbidden” cursor
    [SerializeField] Vector2 cursorHotspot = default;

    [Header("(Optional) Existing tents in scene")]
    [SerializeField] bool scanExistingTentsOnOpen = false;
    [SerializeField] string tentTag = "Tent";       // tag on finalized tents

    [Header("No-build zones")]
    [SerializeField] LayerMask noBuildMask;
    [SerializeField] Vector2 footprintSize2D = new Vector2(1.6f, 1.6f);

    public bool IsOpen => panel && panel.activeSelf;

    // ---- Session ghost record ----
    class Ghost
    {
        public GameObject go;
        public string uiTag;     // which counter this ghost incremented ("Shadow" or ground tag) – can be null/empty
    }

    GameObject cursorGhost;                  // moving preview
    readonly List<Ghost> ghosts = new();     // placed-but-not-confirmed (and their ui tags)
    readonly List<Transform> placedTents = new(); // finalized tents (this session or scanned)
    bool pointerInside;
    bool lastValid;

    [Header("Tent Interaction")]
    [SerializeField] GameObject tentTouchPanel;

    // ===== Ground feedback (yellow) & counters =====
    [Header("Ground feedback")]
    [SerializeField] LayerMask groundMask;           // layers that represent the ground (yellow)
    [SerializeField] Color warnHoverColor = Color.yellow;
    [SerializeField, Range(0f, 1f)] float fadedIconAlpha = 0.35f;

    [System.Serializable]
    public class GroundIconEntry
    {
        public string groundTag;          // e.g., "Sand", "Grass", "Mud" or "Shadow"
        public CanvasGroup iconGroup;     // to fade/unfade the icon
        public Image iconImage;           // optional (for highlight)
        public TMP_Text tmpCounter;       // if using TextMeshPro
        public Text uiCounter;            // if using legacy Text

        [HideInInspector] public int persistentCount; // confirmed tents
        [HideInInspector] public int sessionCount;    // ghosts placed this session
    }

    [SerializeField] List<GroundIconEntry> groundIcons = new();

    readonly Dictionary<string, GroundIconEntry> groundMap = new();
    string lastHoverGroundTag;            // for reference
    Color currentHoverTint = Color.white; // current ghost tint

    // ===== Special “shadow” (green) =====
    [Header("Special shadow hover")]
    [SerializeField] string shadowTag = "Shadow";
    [SerializeField] LayerMask shadowLayer;         // ONLY the shadow layer
    [SerializeField] Color shadowHoverColor = Color.green;

    // Right-click removal distance (world units)
    [Header("UX")]
    [SerializeField, Tooltip("If you right-click within this distance (world units) of a ghost, it will be removed.")]
    float rightClickRemoveRadius = 0.6f;

    void Awake()
    {
        if (confirmButton) confirmButton.onClick.AddListener(OnConfirm);
        if (cancelButton)  cancelButton.onClick.AddListener(OnCancel);
        if (panel) panel.SetActive(false);

        // Build ground tag → UI entry map and reset UI
        foreach (var e in groundIcons)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.groundTag)) continue;
            groundMap[e.groundTag] = e;
            e.persistentCount = 0;
            e.sessionCount = 0;
            SetIconFaded(e, true);
            SetIconCountText(e, 0);
        }

        // Helpful warnings so the Shadow UI can't silently fail
        if (!groundMap.ContainsKey(shadowTag))
            Debug.LogWarning($"[MapPanelController] No GroundIconEntry found for shadowTag '{shadowTag}'. " +
                             $"Add one in 'groundIcons' so the green counter updates.");
        if (tentPrefab && tentPrefab.GetComponent<Collider2D>())
            Debug.LogWarning("[MapPanelController] tentPrefab has a Collider2D. Prefer NO collider on the prefab; " +
                             "a trigger collider is added on Confirm.");
    }

    // ---------------- Public open/close ----------------
    public void Open()
    {
        if (!panel) return;
        panel.SetActive(true);
        ClearSession();

        if (scanExistingTentsOnOpen)
        {
            placedTents.Clear();
            if (!string.IsNullOrEmpty(tentTag))
            {
                var found = GameObject.FindGameObjectsWithTag(tentTag);
                foreach (var go in found) placedTents.Add(go.transform);
            }
            else if (worldParent)
            {
                foreach (Transform t in worldParent)
                    if (t.name.Contains("Tent")) placedTents.Add(t);
            }
        }

        EnsureCursorGhost();
        SetCursorAllowed(true);
    }

    public void Close()
    {
        pointerInside = false;
        if (cursorGhost) cursorGhost.SetActive(false);
        ResetCursor();
        if (panel) panel.SetActive(false);
    }

    // ---------------- Pointer events ----------------
    public void OnPointerEnter(PointerEventData e)
    {
        if (e.pointerEnter == miniMapImage.gameObject) pointerInside = true;
        if (cursorGhost) cursorGhost.SetActive(true);
    }

    public void OnPointerExit(PointerEventData e)
    {
        pointerInside = false;
        if (cursorGhost) cursorGhost.SetActive(false);
        SetCursorAllowed(true);
    }

    public void OnPointerMove(PointerEventData e)
    {
        if (!IsOpen || !pointerInside) return;
        if (!IsOverMap(e.position)) return;

        EnsureCursorGhost();
        var pos = ScreenToWorldOnMiniMap(e.position);
        cursorGhost.transform.position = pos;

        // 1) Validity
        bool valid = IsPositionFree(pos);

        // 2) Determine hover category + UI tag for bar
        bool isShadow = IsShadowAt(pos);
        string uiTag = isShadow ? shadowTag : GetGroundTagAt(pos);
        lastHoverGroundTag = uiTag;

        // 3) Tint priority: Red (invalid) → Green (shadow) → Yellow (tracked) → White
        if (!valid)
        {
            TintGhost(cursorGhost, 0.5f, Color.red);
            currentHoverTint = Color.red;
        }
        else if (isShadow)
        {
            TintGhost(cursorGhost, ghostAlpha, shadowHoverColor);
            currentHoverTint = shadowHoverColor;
        }
        else if (!string.IsNullOrEmpty(uiTag) && groundMap.ContainsKey(uiTag))
        {
            TintGhost(cursorGhost, ghostAlpha, warnHoverColor);
            currentHoverTint = warnHoverColor;
        }
        else
        {
            TintGhost(cursorGhost, ghostAlpha, Color.white);
            currentHoverTint = Color.white;
        }

        if (valid != lastValid)
        {
            SetCursorAllowed(valid);
            lastValid = valid;
        }
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (!IsOpen || !IsOverMap(e.position)) return;

        var pos = ScreenToWorldOnMiniMap(e.position);

        // RIGHT CLICK: remove nearest session ghost + rollback its UI counter
        if (e.button == PointerEventData.InputButton.Right)
        {
            TryRemoveGhostAt(pos);
            return;
        }

        // LEFT CLICK: place new ghost if valid
        if (e.button != PointerEventData.InputButton.Left) return;
        if (!IsPositionFree(pos)) return;

        // Determine the UI-tag BEFORE we instantiate
        string tagForUI = IsShadowAt(pos) ? shadowTag : GetGroundTagAt(pos);

        // Place a ghost that keeps the CURRENT hover color (green/yellow/white)
        var go = Instantiate(tentPrefab, pos, Quaternion.identity, worldParent);
        TintGhost(go, ghostAlpha, currentHoverTint);

        ghosts.Add(new Ghost { go = go, uiTag = tagForUI });

        if (!string.IsNullOrEmpty(tagForUI))
        {
            if (!UI_AddToSession(tagForUI))
                Debug.LogWarning($"[MapPanelController] No GroundIconEntry mapped for tag '{tagForUI}'. " +
                                 "Add an entry in 'groundIcons' and be sure the tag string matches exactly.");
        }
    }

    // ---------------- Buttons ----------------
    void OnConfirm()
    {
        foreach (var gh in ghosts)
        {
            var g = gh.go;
            if (!g) continue;

            // Keep faded + keep existing tint

            // Ensure PolygonCollider2D is present & is the only collider
            var poly = g.GetComponent<PolygonCollider2D>();
            if (!poly) poly = g.AddComponent<PolygonCollider2D>();
            poly.isTrigger = true;

            var allCols = g.GetComponents<Collider2D>();
            foreach (var c in allCols)
            {
                if (c != poly) Destroy(c);
            }

            // Attach interaction script
            var ti = g.GetComponent<TentInteract2D>();
            if (!ti) ti = g.AddComponent<TentInteract2D>();
            ti.panelToOpen = tentTouchPanel;
            ti.fadedAlpha = ghostAlpha;

            // Bookkeeping
            g.tag = tentTag;
            if (!placedTents.Contains(g.transform))
                placedTents.Add(g.transform);
        }

        UI_CommitSession();   // make counters persistent
        ghosts.Clear();
        DestroyCursorGhost();
        ResetCursor();
        Close();
    }

    void OnCancel()
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
            if (ghosts[i].go) Destroy(ghosts[i].go);
        ghosts.Clear();
        DestroyCursorGhost();
        ResetCursor();
        UI_RollbackSession();
        Close();
    }

    // ---------------- Helpers ----------------
    void ClearSession()
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
            if (ghosts[i].go) Destroy(ghosts[i].go);
        ghosts.Clear();
        DestroyCursorGhost();
        lastValid = true;
        UI_RollbackSession();
    }

    void EnsureCursorGhost()
    {
        if (cursorGhost || !tentPrefab) return;
        cursorGhost = Instantiate(tentPrefab, Vector3.zero, Quaternion.identity, worldParent);
        TintGhost(cursorGhost, ghostAlpha, Color.white);
        cursorGhost.SetActive(false);
    }

    void DestroyCursorGhost()
    {
        if (cursorGhost) Destroy(cursorGhost);
        cursorGhost = null;
    }

    bool IsOverMap(Vector2 screenPos)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(miniMapImage.rectTransform, screenPos, null);
    }

    Vector3 ScreenToWorldOnMiniMap(Vector2 screenPos)
    {
        var rt = miniMapImage.rectTransform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out var local);
        var r = rt.rect;
        float u = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
        float v = Mathf.InverseLerp(r.yMin, r.yMax, local.y);
        var world = miniMapCamera.ViewportToWorldPoint(new Vector3(u, v, 0f));
        world.z = worldZ;
        return world;
    }

    // ---------- Spatial rules ----------
    bool OverlapsNoBuild(Vector3 pos)
{
    // If you want to use prefab collider size instead of hardcoded footprint
    var prefabCollider = tentPrefab.GetComponentInChildren<Collider2D>();
    Vector2 size = footprintSize2D;

    if (prefabCollider)
        size = prefabCollider.bounds.size;

    Vector2 p = new Vector2(pos.x, pos.y);
    var hit = Physics2D.OverlapBox(p, size, 0f, noBuildMask);
    return hit != null;
}


    bool IsPositionFree(Vector3 pos)
    {
        if (OverlapsNoBuild(pos)) return false;

        float minSqr = minTentSpacing * minTentSpacing;

        for (int i = 0; i < placedTents.Count; i++)
        {
            var t = placedTents[i];
            if (!t) continue;
            if ((t.position - pos).sqrMagnitude < minSqr) return false;
        }

        for (int i = 0; i < ghosts.Count; i++)
        {
            var g = ghosts[i].go;
            if (!g) continue;
            if ((g.transform.position - pos).sqrMagnitude < minSqr) return false;
        }

        return true;
    }

    // ---------- Ground lookups & UI ----------
    string GetGroundTagAt(Vector3 pos)
    {
        var hit = Physics2D.OverlapPoint(new Vector2(pos.x, pos.y), groundMask);
        return hit ? hit.tag : null;
    }

    bool IsShadowAt(Vector3 pos)
    {
        var hit = Physics2D.OverlapPoint(new Vector2(pos.x, pos.y), shadowLayer);
        return hit && hit.CompareTag(shadowTag);
    }

    // returns true if updated, false if no entry exists
    bool UI_AddToSession(string tag)
    {
        if (!groundMap.TryGetValue(tag, out var e)) return false;
        e.sessionCount++;
        SetIconCountText(e, e.persistentCount + e.sessionCount);
        SetIconFaded(e, false);
        return true;
    }

    void UI_RemoveOneFromSession(string tag)
    {
        if (!groundMap.TryGetValue(tag, out var e)) return;
        e.sessionCount = Mathf.Max(0, e.sessionCount - 1);
        int total = e.persistentCount + e.sessionCount;
        SetIconCountText(e, total);
        SetIconFaded(e, total == 0);
    }

    void UI_RollbackSession()
    {
        foreach (var e in groundIcons)
        {
            e.sessionCount = 0;
            SetIconCountText(e, e.persistentCount);
            SetIconFaded(e, (e.persistentCount == 0));
        }
    }

    void UI_CommitSession()
    {
        foreach (var e in groundIcons)
        {
            e.persistentCount += e.sessionCount;
            e.sessionCount = 0;
            SetIconCountText(e, e.persistentCount);
            SetIconFaded(e, e.persistentCount == 0);
        }
    }

    void SetIconFaded(GroundIconEntry e, bool faded)
    {
        if (!e?.iconGroup) return;
        e.iconGroup.alpha = faded ? fadedIconAlpha : 1f;
    }

    void SetIconCountText(GroundIconEntry e, int value)
    {
        if (e.tmpCounter) e.tmpCounter.text = value.ToString();
        if (e.uiCounter)  e.uiCounter.text  = value.ToString();
    }

    void SetCursorAllowed(bool allowed)
    {
        if (!cursorForbidden) return; // no custom cursor assigned; skip
        if (allowed)
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        else
            Cursor.SetCursor(cursorForbidden, cursorHotspot, CursorMode.Auto);
    }

    void ResetCursor()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    // Tint all SpriteRenderers in a prefab
    void TintGhost(GameObject go, float alpha, Color tint)
    {
        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            var c = tint;
            c.a = alpha;
            sr.color = c;
        }
    }

    // ---------- Right-click removal ----------
    void TryRemoveGhostAt(Vector3 cursorWorldPos)
    {
        if (ghosts.Count == 0) return;

        float bestSqr = rightClickRemoveRadius * rightClickRemoveRadius;
        int bestIndex = -1;

        for (int i = 0; i < ghosts.Count; i++)
        {
            var g = ghosts[i].go;
            if (!g) continue;
            float d = (g.transform.position - cursorWorldPos).sqrMagnitude;
            if (d <= bestSqr)
            {
                bestSqr = d;
                bestIndex = i;
            }
        }

        if (bestIndex == -1) return;

        var ghost = ghosts[bestIndex];
        if (ghost.go) Destroy(ghost.go);
        if (!string.IsNullOrEmpty(ghost.uiTag)) UI_RemoveOneFromSession(ghost.uiTag);
        ghosts.RemoveAt(bestIndex);
    }
}
