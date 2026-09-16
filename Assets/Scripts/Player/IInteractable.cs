using UnityEngine;

namespace UrquhartsShadow.Player
{
    /// <summary>Anything a player can use with the Interact key: tools, stations, ladders, supplies.</summary>
    public interface IInteractable
    {
        /// <summary>Text shown on the HUD prompt, e.g. "Take 35mm camera".</summary>
        string GetPrompt(PlayerCharacter player);
        bool CanInteract(PlayerCharacter player);
        /// <summary>Seconds the key must be held; 0 for instant.</summary>
        float HoldSeconds { get; }
        void Interact(PlayerCharacter player);
        Transform transform { get; }
    }
}
