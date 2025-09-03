using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class RandomRain : MonoBehaviour
{
    [Header("Assign your Particle System")]
    public ParticleSystem rainSystem;

    [Header("Timing (seconds)")]
    public Vector2 delayRange = new Vector2(5f, 20f);     // wait before rain starts
    public Vector2 durationRange = new Vector2(8f, 18f);  // time it rains
    public float fadeDuration = 2f;                       // fade in/out time

    [Header("Rain Intensity")]
    public float targetRateOverTime = 700f;               // peak emission rate

    [Header("Events")]
    public UnityEvent onRainStarted;   // hook UI tip here
    public UnityEvent onRainStopped;

    private ParticleSystem.EmissionModule emission;

    void Awake()
    {
        if (!rainSystem) { Debug.LogWarning("RandomRain: assign a ParticleSystem."); return; }
        emission = rainSystem.emission;

        // Start idle (no visible drops)
        rainSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        SetRate(0f);
        rainSystem.Play(); // keep "playing" so rate changes take effect immediately
    }

    void Start()
    {
        StartCoroutine(RainCycle());
    }

    IEnumerator RainCycle()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(delayRange.x, delayRange.y));

            // announce rain starting (right before fade-in)
            onRainStarted?.Invoke();

            // fade in
            yield return FadeRate(0f, targetRateOverTime, fadeDuration);

            // hold
            yield return new WaitForSeconds(Random.Range(durationRange.x, durationRange.y));

            // fade out
            yield return FadeRate(targetRateOverTime, 0f, fadeDuration);

            onRainStopped?.Invoke();

            // clear leftover particles; return to idle
            rainSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rainSystem.Play();
        }
    }

    IEnumerator FadeRate(float from, float to, float time)
    {
        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            SetRate(Mathf.Lerp(from, to, t / time));
            yield return null;
        }
        SetRate(to);
    }

    void SetRate(float value)
    {
        var curve = emission.rateOverTime;
        curve.constant = value;
        emission.rateOverTime = curve;
    }
}
