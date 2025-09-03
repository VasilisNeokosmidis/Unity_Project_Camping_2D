using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider2D))]
public class TentEntrance : MonoBehaviour
{
    [Header("Scene to load")]
    public string sceneToLoad = "TentInterior";

    [Header("Fade (slower)")]
    [Tooltip("Full-screen black Image with a CanvasGroup.")]
    public CanvasGroup fader;

    [Tooltip("Seconds to fade OUT the current scene.")]
    public float fadeOutDuration = 1.8f;

    [Tooltip("Seconds to fade IN the new scene.")]
    public float fadeInDuration  = 1.4f;

    [Tooltip("Curve for OUT; leave empty to auto-fill with a slow-ease curve.")]
    public AnimationCurve fadeOutCurve;

    [Tooltip("Curve for IN; leave empty to auto-fill with a slow-ease curve.")]
    public AnimationCurve fadeInCurve;

    bool isLoading;
    Collider2D _col;

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    void Awake()
    {
        _col = GetComponent<Collider2D>();

        // Default curves if none provided (slow-ease, lingers a bit)
        if (fadeOutCurve == null || fadeOutCurve.length < 2)
            fadeOutCurve = new AnimationCurve(
                new Keyframe(0.00f, 0.00f),
                new Keyframe(0.30f, 0.05f),
                new Keyframe(0.70f, 0.95f),
                new Keyframe(1.00f, 1.00f)
            );

        if (fadeInCurve == null || fadeInCurve.length < 2)
            fadeInCurve = new AnimationCurve(
                new Keyframe(0.00f, 1.00f),
                new Keyframe(0.30f, 0.95f),
                new Keyframe(0.70f, 0.05f),
                new Keyframe(1.00f, 0.00f)
            );

        if (fader != null)
        {
            fader.alpha = 0f;
            fader.blocksRaycasts = false;
            fader.interactable   = false;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isLoading) return;
        if (!other.CompareTag("Player")) return;

        StartCoroutine(FadeOutLoadFadeIn());
    }

    IEnumerator FadeOutLoadFadeIn()
    {
        isLoading = true;
        if (_col) _col.enabled = false; // prevent retriggers

        CanvasGroup runtimeFader = fader;

        // If you dragged a prefab asset, instantiate a runtime copy
        if (runtimeFader != null && !runtimeFader.gameObject.scene.IsValid())
        {
            var clone = Instantiate(runtimeFader.gameObject);
            runtimeFader = clone.GetComponent<CanvasGroup>();
        }

        if (runtimeFader == null)
        {
            // No fader? Just load (will be instant)
            yield return SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Single);
            yield break;
        }

        // Persist fader through the load & ensure it renders on top
        var root = runtimeFader.transform.root.gameObject;
        DontDestroyOnLoad(root);

        var canvas = runtimeFader.GetComponentInParent<Canvas>();
        if (canvas)
        {
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
        }

        runtimeFader.blocksRaycasts = true;
        runtimeFader.interactable   = true;
        runtimeFader.alpha          = 0f;

        // ---- Fade OUT (slow)
        yield return StartCoroutine(FadeTo(runtimeFader, 1f, fadeOutDuration, fadeOutCurve));

        // ---- Begin loading new scene in background
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Single);
        op.allowSceneActivation = false;

        // Wait until scene is ready to activate (~0.9 progress)
        while (op.progress < 0.9f)
            yield return null;

        // Activate now that we’re fully faded to black
        op.allowSceneActivation = true;
        yield return null; // let one frame settle after activation

        // ---- Fade IN (slow)
        yield return StartCoroutine(FadeTo(runtimeFader, 0f, fadeInDuration, fadeInCurve));

        runtimeFader.blocksRaycasts = false;
        runtimeFader.interactable   = false;

        Destroy(root); // clean up temporary fader
        isLoading = false;
    }

    IEnumerator FadeTo(CanvasGroup cg, float target, float duration, AnimationCurve curve)
    {
        float start = cg.alpha;
        float t = 0f;

        // If duration is tiny, snap
        if (duration <= 0.001f)
        {
            cg.alpha = target;
            yield break;
        }

        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // unaffected by timescale
            float u = Mathf.Clamp01(t / duration);
            float eased = (curve != null && curve.length >= 2) ? curve.Evaluate(u) : u;
            cg.alpha = Mathf.Lerp(start, target, eased);
            yield return null;
        }
        cg.alpha = target;
    }
}
