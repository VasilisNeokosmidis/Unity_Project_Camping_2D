using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [Header("Defaults")]
    [SerializeField] float defaultFadeOut = 0.6f;
    [SerializeField] float defaultFadeIn  = 0.6f;

    CanvasGroup cg;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        cg = GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;
    }

    public static void FadeToScene(string sceneName, float fadeOut = -1f, float fadeIn = -1f)
    {
        if (Instance == null)
        {
            Debug.LogError("ScreenFader: No instance in the scene. Add a Canvas + Image + CanvasGroup with ScreenFader on it.");
            return;
        }
        Instance.StartCoroutine(Instance.DoFadeToScene(sceneName,
            fadeOut >= 0f ? fadeOut : Instance.defaultFadeOut,
            fadeIn  >= 0f ? fadeIn  : Instance.defaultFadeIn));
    }

    IEnumerator DoFadeToScene(string sceneName, float outDur, float inDur)
    {
        // Fade OUT current scene
        yield return Fade(1f, outDur, blockInput: true);

        // Load the new scene
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        yield return op;         // waits until loaded
        yield return null;       // one frame so UI/layout settles

        // Fade IN new scene
        yield return Fade(0f, inDur, blockInput: false);
    }

    public IEnumerator Fade(float targetAlpha, float duration, bool blockInput)
    {
        cg.blocksRaycasts = blockInput;
        cg.interactable   = blockInput;

        float start = cg.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // unaffected by timeScale
            cg.alpha = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t / duration));
            yield return null;
        }
        cg.alpha = targetAlpha;

        if (!blockInput)
        {
            cg.blocksRaycasts = false;
            cg.interactable   = false;
        }
    }

    // Optional helper if you want to fade IN when any scene starts
    public static void FadeInOnSceneStart(float duration = 0.6f)
    {
        if (Instance == null) return;
        Instance.StopAllCoroutines();
        Instance.StartCoroutine(Instance.Fade(0f, duration, blockInput:false));
    }
}
