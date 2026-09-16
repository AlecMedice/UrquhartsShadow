using UnityEngine;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Boat
{
    /// <summary>Boarding ladder on the hull. A swimming player interacts to climb back aboard.</summary>
    public class Ladder : MonoBehaviour, IInteractable
    {
        [SerializeField] private Transform topPoint;
        public float HoldSeconds => 1.0f;
        public string GetPrompt(PlayerCharacter p) => "Climb aboard";
        public bool CanInteract(PlayerCharacter p) => p.Movement != null && p.Movement.IsSwimming;
        public void Interact(PlayerCharacter p)
        {
            if (topPoint != null) p.Movement.Teleport(topPoint.position, topPoint.rotation);
        }
    }
}
