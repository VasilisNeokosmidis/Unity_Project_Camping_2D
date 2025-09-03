using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DayNightTopHolds : MonoBehaviour
{
    [Header("Strips (containers)")]
    public RectTransform leftStrip;    // Sun path (left blue area)
    public RectTransform rightStrip;   // Moon path (right blue area)

    [Header("Icons (children of strips)")]
    public RectTransform sunIcon;      // child of leftStrip
    public RectTransform moonIcon;     // child of rightStrip

    [Header("Fullscreen tint (black Image)")]
    public Image nightTint;

    [Header("Timing (seconds)")]
    public float transitionDuration = 30f; // time for one cross move (down+up)
    public float holdTopSeconds     = 2f;  // pause at top (day/night indicator)

    [Header("Brightness (alpha)")]
    [Range(0,1)] public float dayAlpha   = 0.05f; // bright
    [Range(0,1)] public float nightAlpha = 0.85f; // dark

    [Header("Motion")]
    public AnimationCurve curve = AnimationCurve.EaseInOut(0,0,1,1);
    public float verticalMarginPx = 0f; // padding from strip edges

    void OnEnable()
    {
        // Force both icons to bottom-center anchoring
        ForceBottomAnchor(sunIcon);
        ForceBottomAnchor(moonIcon);

        // INITIAL STATE: Sun top (visible), Moon bottom (hidden)
        PlaceTopInside(leftStrip, sunIcon);
        PlaceBottomOff(rightStrip, moonIcon);
        sunIcon.gameObject.SetActive(true);
        moonIcon.gameObject.SetActive(false);

        if (nightTint) SetTint(dayAlpha); // it's day at start

        StartCoroutine(RunLoop());
    }

    IEnumerator RunLoop()
    {
        while (true)
        {
            // -------- HOLD DAY: Sun top visible, Moon hidden --------
            sunIcon.gameObject.SetActive(true);
            moonIcon.gameObject.SetActive(false);
            PlaceTopInside(leftStrip, sunIcon);
            SetTint(dayAlpha);
            yield return new WaitForSeconds(holdTopSeconds);

            // -------- TRANSITION Day -> Night --------
            // Sun goes top -> bottom (disappear), Moon goes bottom -> top (appear)
            moonIcon.gameObject.SetActive(true);
            yield return CrossMove(
                sunStrip:  leftStrip,  sun:  sunIcon,  sunFromTopToBottom: true,
                moonStrip: rightStrip, moon: moonIcon, moonFromBottomToTop: true,
                tintFrom: dayAlpha, tintTo: nightAlpha
            );
            // End of transition: Sun hidden, Moon top visible
            sunIcon.gameObject.SetActive(false);
            PlaceTopInside(rightStrip, moonIcon);
            SetTint(nightAlpha);

            // -------- HOLD NIGHT --------
            yield return new WaitForSeconds(holdTopSeconds);

            // -------- TRANSITION Night -> Day --------
            // Moon goes top -> bottom, Sun goes bottom -> top
            sunIcon.gameObject.SetActive(true);
            yield return CrossMove(
                sunStrip:  leftStrip,  sun:  sunIcon,  sunFromTopToBottom: false, // bottom->top
                moonStrip: rightStrip, moon: moonIcon, moonFromBottomToTop: false, // top->bottom
                tintFrom: nightAlpha, tintTo: dayAlpha
            );
            // End of transition: Moon hidden, Sun top visible
            moonIcon.gameObject.SetActive(false);
            PlaceTopInside(leftStrip, sunIcon);
            SetTint(dayAlpha);
        }
    }

    // One cross transition: move sun & moon simultaneously and blend tint.
    IEnumerator CrossMove(RectTransform sunStrip, RectTransform sun, bool sunFromTopToBottom,
                          RectTransform moonStrip, RectTransform moon, bool moonFromBottomToTop,
                          float tintFrom, float tintTo)
    {
        // Prepare endpoints
        float sunStart = sunFromTopToBottom ? TopInsideY(sunStrip, sun) : BottomOffY(sun);
        float sunEnd   = sunFromTopToBottom ? BottomOffY(sun)          : TopInsideY(sunStrip, sun);

        float moonStart = moonFromBottomToTop ? BottomOffY(moon)           : TopInsideY(moonStrip, moon);
        float moonEnd   = moonFromBottomToTop ? TopInsideY(moonStrip, moon) : BottomOffY(moon);

        // Set starting positions
        sun.anchoredPosition  = new Vector2(0f, sunStart);
        moon.anchoredPosition = new Vector2(0f, moonStart);

        float t = 0f;
        while (t < transitionDuration)
        {
            t += Time.deltaTime;
            float u = curve.Evaluate(Mathf.Clamp01(t / transitionDuration));

            // Move
            sun.anchoredPosition  = new Vector2(0f, Mathf.Lerp(sunStart,  sunEnd,  u));
            moon.anchoredPosition = new Vector2(0f, Mathf.Lerp(moonStart, moonEnd, u));

            // Tint
            if (nightTint)
            {
                float a = Mathf.Lerp(tintFrom, tintTo, u);
                SetTint(a);
            }

            yield return null;
        }

        // Clamp to exact endpoints
        sun.anchoredPosition  = new Vector2(0f, sunEnd);
        moon.anchoredPosition = new Vector2(0f, moonEnd);

        // If one should be hidden at bottom, disable it now
        if (sunFromTopToBottom) // sun ended at bottom off
            sun.gameObject.SetActive(false);
        if (!moonFromBottomToTop) // moon ended at bottom off
            moon.gameObject.SetActive(false);
    }

    // ----- Helpers -----

    void ForceBottomAnchor(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); // bottom-center
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    // Fully visible top position (inside the strip)
    float TopInsideY(RectTransform strip, RectTransform icon)
    {
        float h = icon.rect.height;
        float pivot = icon.pivot.y;
        return strip.rect.height - verticalMarginPx - h * (1f - pivot);
    }

    // Fully hidden below the strip
    float BottomOffY(RectTransform icon)
    {
        return -icon.rect.height;
    }

    void PlaceTopInside(RectTransform strip, RectTransform icon)
    {
        icon.anchoredPosition = new Vector2(0f, TopInsideY(strip, icon));
    }

    void PlaceBottomOff(RectTransform strip, RectTransform icon)
    {
        icon.anchoredPosition = new Vector2(0f, BottomOffY(icon));
    }

    void SetTint(float a)
    {
        var c = nightTint.color;
        c.a = a;
        nightTint.color = c;
    }
}
