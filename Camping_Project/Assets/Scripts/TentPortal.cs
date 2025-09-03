using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TentPortal : MonoBehaviour
{
    [Header("Identity")]
    public string tentId;                         // unique per tent (e.g., GUID or "Tent_A_03")

    [Header("Interior scene name")]
    public string interiorSceneName = "TentInterior";

    [Header("Return (optional)")]
    public Transform exitSpawnInWorld;           // where player appears when leaving the tent

    void Reset() { GetComponent<Collider2D>().isTrigger = true; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        // Enter this tent; world keeps running
        TentInteriorLoader.EnterTent(
            interiorSceneName,
            new TentInteriorLoader.Context
            {
                TentId = string.IsNullOrWhiteSpace(tentId) ? gameObject.name : tentId,
                WorldTent = gameObject,
                Player = other.transform,
                WorldExitSpawn = exitSpawnInWorld
            }
        );
    }
}
