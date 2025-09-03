using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class MoveScript : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Sprites")]
    [SerializeField] private Sprite sideSprite;   // right-facing (flip for left)
    [SerializeField] private Sprite frontSprite;  // hero-idle-front (down)
    [SerializeField] private Sprite backSprite;   // hero-idle-back (up)

    private Rigidbody2D rb;
    private Vector2 input;
    private SpriteRenderer sr;
    private Vector2 lastLook = Vector2.down;

    private Vector3 originalScale;
void Awake()
{
    rb = GetComponent<Rigidbody2D>();
    sr = GetComponentInChildren<SpriteRenderer>();
    if (sr != null) originalScale = sr.transform.localScale;
}

    void Update()
    {
        // gather input
        input = Vector2.zero;
        if (Keyboard.current.upArrowKey.isPressed)    input += Vector2.up;
        if (Keyboard.current.downArrowKey.isPressed)  input += Vector2.down;
        if (Keyboard.current.leftArrowKey.isPressed)  input += Vector2.left;
        if (Keyboard.current.rightArrowKey.isPressed) input += Vector2.right;

        if (input.sqrMagnitude > 1e-6f)
        {
            input = input.normalized;
            lastLook = input;
        }

        UpdateVisual(lastLook);
    }

    void FixedUpdate()
    {
        Vector2 target = (Vector2)transform.position + input * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(target);
        rb.linearVelocity = Vector2.zero;
    }

    void UpdateVisual(Vector2 dir)
{
    if (!sr) return;

    // save base scale from Awake
    Vector3 baseScale = sr.transform.localScale;

    // vertical movement
    if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
    {
        if (dir.y > 0) sr.sprite = backSprite;   // up
        else           sr.sprite = frontSprite;  // down

        // reset to original scale (don’t force 1,1,1)
        sr.transform.localScale = new Vector3(Mathf.Abs(baseScale.x), baseScale.y, baseScale.z);
    }
    // horizontal movement
    else
    {
        sr.sprite = sideSprite;
        // flip X relative to original scale
        float flipX = dir.x < 0 ? -Mathf.Abs(baseScale.x) : Mathf.Abs(baseScale.x);
        sr.transform.localScale = new Vector3(flipX, baseScale.y, baseScale.z);
    }
}

}
