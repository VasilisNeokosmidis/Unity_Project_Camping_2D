using UnityEngine;

[RequireComponent(typeof(PolygonCollider2D))]
public class TentInteract2D : MonoBehaviour
{
    [Header("Interaction")]
    public string playerTag = "Player";    // Tag on your player object
    public GameObject panelToOpen;         // Assign in Inspector (the UI panel)
    public bool oneShotOpen = true;        // Should the panel open only once?

    [Header("Visuals")]
    [Range(0f,1f)] public float fadedAlpha = 0.5f;
    [Range(0f,1f)] public float solidAlpha = 1f;

    SpriteRenderer[] _renderers;
    bool _opened;

    void Awake()
    {
        // Ensure polygon collider exists & is trigger
        var poly = GetComponent<PolygonCollider2D>();
        if (!poly) poly = gameObject.AddComponent<PolygonCollider2D>();
        poly.isTrigger = true;

        // Cache all sprite renderers for tinting
        _renderers = GetComponentsInChildren<SpriteRenderer>(true);

        // Start faded
        SetFaded(true);
    }

    public void SetFaded(bool faded)
    {
        float a = faded ? fadedAlpha : solidAlpha;
        foreach (var r in _renderers)
        {
            var c = r.color; c.a = a; r.color = c;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
{
    if (_opened && oneShotOpen) return;
    if (!other.CompareTag(playerTag)) return;

    if (panelToOpen)
    {
        panelToOpen.SetActive(true);

        // NEW: pass this tent to the panel so it can replace it later
        var ctrl = panelToOpen.GetComponent<PegPanelController>();
        if (ctrl) ctrl.SetTargetTent(gameObject);
    }
    _opened = true;
}
}
