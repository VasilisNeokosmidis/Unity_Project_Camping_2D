using UnityEngine;

public class SignInteract : MonoBehaviour
{
    [SerializeField] MapPanelController panelController;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            panelController.Open();
    }
}
