using Unity.Netcode;
using UnityEngine;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Base for tools carried in the hand (phone camera, eDNA sampler). Lives on the player prefab
    /// under HandSocket; only the owner runs input, the server validates results.
    /// </summary>
    public abstract class HandheldTool : NetworkBehaviour
    {
        [SerializeField] protected GameObject viewModel;
        protected PlayerCharacter Player { get; private set; }
        public bool IsEquipped { get; private set; }
        public abstract string ToolName { get; }

        protected virtual void Awake()
        {
            Player = GetComponentInParent<PlayerCharacter>();
        }

        public virtual void Equip()
        {
            IsEquipped = true;
            if (viewModel != null) viewModel.SetActive(true);
            if (Player != null) Player.HeldTool = this;
        }

        public virtual void Unequip()
        {
            IsEquipped = false;
            if (viewModel != null) viewModel.SetActive(false);
            if (Player != null && Player.HeldTool == this) Player.HeldTool = null;
        }

        protected bool OwnerCanUse => IsOwner && Player != null && Player.Vitals.IsAlive && !Player.IsSeated && IsEquipped;
    }
}
