using UnityEngine;
using TMPro;                  // remove/change to UnityEngine.UI.Text if not using TMP

public class RainTipUI : MonoBehaviour
{
    [Header("UI")]
    public CanvasGroup group;               // add CanvasGroup to your tip panel
    public TextMeshProUGUI label;           // the text element (or use Text)
    [TextArea] public string message = "It’s raining! Consider adding sheds on your tents.";

    [Header("Timing (seconds)")]
    public float fadeIn = 0.5f;
    public float hold   = 2.0f;
    public float fadeOut= 0.8f;

    Coroutine routine;

    void Reset()
    {
        group = GetComponent<CanvasGroup>();
        label = GetComponentInChildren<TextMeshProUGUI>();
    }

    public void ShowTip()                   // call this from RandomRain.onRainStarted
    {
        if (routine != null) StopCoroutine(routine);
        gameObject.SetActive(true);
        if (label) label.text = message;
        routine = StartCoroutine(FadeSequence());
    }

    System.Collections.IEnumerator FadeSequence()
    {
        group.interactable = false;
        group.blocksRaycasts = false;

        // fade in
        yield return Fade(0f, 1f, fadeIn);
        // hold
        yield return new WaitForSeconds(hold);
        // fade out
        yield return Fade(1f, 0f, fadeOut);

        gameObject.SetActive(false);
        routine = null;
    }

    System.Collections.IEnumerator Fade(float a, float b, float t)
    {
        float time = 0f;
        while (time < t)
        {
            time += Time.deltaTime;
            group.alpha = Mathf.Lerp(a, b, time / t);
            yield return null;
        }
        group.alpha = b;
    }
}
