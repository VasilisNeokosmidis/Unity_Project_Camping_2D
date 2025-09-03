using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TentInteriorLoader : MonoBehaviour
{
    // ---------- Context available while you're inside ----------
    public sealed class Context
    {
        public string     TentId;
        public GameObject WorldTent;
        public Transform  Player;
        public Transform  WorldExitSpawn;
    }

    public static Context Current { get; private set; }
    public static bool IsTransitioning { get; private set; }

    // ---------- Singleton ----------
    static TentInteriorLoader _inst;
    void Awake()
    {
        if (_inst && _inst != this) { Destroy(gameObject); return; }
        _inst = this;
        DontDestroyOnLoad(gameObject);
    }

    // ---------- Config ----------
    [Header("World")]
    public string worldSceneName = "World";
    public Camera worldCamera;                         // auto-found if null
    public string[] worldLayers = { "World", "UI" };   // what World cam draws when active

    [Header("Tent camera")]
    public string tentCameraTag = "MainCamera";
    public string[] tentLayers = { "Tent", "UI" };     // what Tent cam draws when inside

    [Header("Fade")]
    public CanvasGroup fader;                          // auto-created if null
    public float fadeOut = 1.0f, fadeIn = 0.8f;
    public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0,0,1,1);
    public AnimationCurve fadeInCurve  = AnimationCurve.EaseInOut(0,1,1,0);

    // ---------- Public API ----------
    public static void EnterTent(string interiorSceneName, Context ctx, Action onFinished = null)
    {
        EnsureInstance();
        if (IsTransitioning) { onFinished?.Invoke(); return; }
        _inst.StartCoroutine(_inst.CoEnterTent(interiorSceneName, ctx, onFinished));
    }

    public static void ExitTent(Action onFinished = null)
    {
        if (_inst) _inst.StartCoroutine(_inst.CoExitTent(onFinished));
    }

    // ---------- Internals ----------
    Scene  _interiorScene;
    Camera _tentCam;

    IEnumerator CoEnterTent(string sceneName, Context ctx, Action onFinished)
    {
        IsTransitioning = true;
        Current = ctx;

        EnsureFader(); PrepFader(0f);
        if (!worldCamera) worldCamera = FindWorldCamera();

        // Fade OUT
        yield return FadeTo(1f, fadeOut, fadeOutCurve);

        // Load interior additively (or reuse if already loaded)
        var already = SceneManager.GetSceneByName(sceneName);
        if (!already.IsValid() || !already.isLoaded)
        {
            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (!load.isDone) yield return null;
            _interiorScene = SceneManager.GetSceneByName(sceneName);
        }
        else
        {
            _interiorScene = already;
        }

        // Make it active so new objects go there
        SceneManager.SetActiveScene(_interiorScene);

        // ---- CAMERAS: use ONLY the Tent camera while inside ----
        if (!worldCamera) worldCamera = FindWorldCamera();
        if (worldCamera) worldCamera.enabled = false;   // hide world visually (it still simulates)

        _tentCam = FindTentCamera(_interiorScene);      // expects a camera tagged "MainCamera" in the Tent scene
        if (_tentCam)
        {
            _tentCam.enabled     = true;
            _tentCam.clearFlags  = CameraClearFlags.SolidColor;        // full frame from tent
            _tentCam.depth       = 0;                                  // only camera while inside
            _tentCam.cullingMask = LayerMaskFrom(tentLayers);

            // Keep zoom consistent with outside (optional)
            if (worldCamera && worldCamera.orthographic)
            {
                _tentCam.orthographic = true;
                _tentCam.orthographicSize = worldCamera.orthographicSize;
            }

            // Snap to an interior anchor if present (optional)
            var anchor = GameObject.Find("TentCameraAnchor");
            if (anchor)
            {
                var p = anchor.transform.position;
                _tentCam.transform.position = new Vector3(p.x, p.y, _tentCam.transform.position.z);
            }
        }
        else
        {
            Debug.LogWarning("[TentInteriorLoader] No tent camera found in interior scene. " +
                             "Add a camera tagged 'MainCamera'.");
        }

        // Optional: move player to a spawn point inside (tag a transform as "TentSpawn")
        var spawn = GameObject.FindWithTag("TentSpawn");
        if (spawn && ctx?.Player) ctx.Player.position = spawn.transform.position;

        // Fade IN
        yield return FadeTo(0f, fadeIn, fadeInCurve);

        IsTransitioning = false;
        onFinished?.Invoke();
    }

    IEnumerator CoExitTent(Action onFinished)
    {
        if (!_interiorScene.IsValid()) { onFinished?.Invoke(); yield break; }
        IsTransitioning = true;

        EnsureFader(); PrepFader(0f);
        yield return FadeTo(1f, fadeOut, fadeOutCurve);

        // Back to World as active
        var world = SceneManager.GetSceneByName(worldSceneName);
        if (world.IsValid()) SceneManager.SetActiveScene(world);

        // Place player at exit spawn (if provided)
        if (Current?.WorldExitSpawn && Current.Player)
            Current.Player.position = Current.WorldExitSpawn.position;

        // Unload interior
        if (_interiorScene.isLoaded)
        {
            var unload = SceneManager.UnloadSceneAsync(_interiorScene);
            while (!unload.isDone) yield return null;
        }
        _interiorScene = default;
        _tentCam = null;

        // Restore World camera
        if (!worldCamera) worldCamera = FindWorldCamera();
        if (worldCamera)
        {
            worldCamera.cullingMask = LayerMaskFrom(worldLayers);
            worldCamera.clearFlags  = CameraClearFlags.SolidColor;  // or your usual
            worldCamera.depth       = 0;
            worldCamera.enabled     = true;
        }

        // Fade IN
        yield return FadeTo(0f, fadeIn, fadeInCurve);

        Current = null;
        IsTransitioning = false;
        onFinished?.Invoke();
    }

    // ---------- Helpers ----------
    static void EnsureInstance()
    {
        if (_inst) return;
        var go = new GameObject("~TentInteriorLoader");
        _inst = go.AddComponent<TentInteriorLoader>();
    }

    void EnsureFader()
    {
        if (fader) return;

        var root = new GameObject("ScreenFader");
        DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;
        root.AddComponent<UnityEngine.UI.CanvasScaler>();
        root.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var imgGO = new GameObject("Fade");
        imgGO.transform.SetParent(root.transform, false);
        var img = imgGO.AddComponent<UnityEngine.UI.Image>();
        img.color = Color.black; img.raycastTarget = true;

        var rt = img.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        fader = imgGO.AddComponent<CanvasGroup>();
    }

    void PrepFader(float a)
    {
        fader.alpha = a;
        fader.blocksRaycasts = a > 0f;
        fader.interactable   = a > 0f;
    }

    IEnumerator FadeTo(float target, float dur, AnimationCurve curve)
    {
        if (!fader || dur <= 0f) { if (fader) fader.alpha = target; yield break; }
        fader.blocksRaycasts = true; fader.interactable = true;

        float start = fader.alpha, t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = curve != null ? curve.Evaluate(u) : u;
            fader.alpha = Mathf.Lerp(start, target, e);
            yield return null;
        }
        fader.alpha = target;

        if (Mathf.Approximately(target, 0f))
        {
            fader.blocksRaycasts = false;
            fader.interactable   = false;
        }
    }

    Camera FindWorldCamera()
    {
        var world = SceneManager.GetSceneByName(worldSceneName);
       var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);

        foreach (var c in cams)
            if (c && c.gameObject.scene == world) return c;
        return Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
    }

    Camera FindTentCamera(Scene s)
    {
        var byTag = GameObject.FindGameObjectWithTag(tentCameraTag);
        if (byTag && byTag.scene == s) return byTag.GetComponent<Camera>();

        var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var c in cams)
            if (c && c.gameObject.scene == s) return c;
        return null;
    }

    static int LayerMaskFrom(string[] names)
    {
        if (names == null || names.Length == 0) return ~0;
        int mask = 0;
        foreach (var n in names) mask |= 1 << LayerMask.NameToLayer(n);
        return mask;
    }
}
