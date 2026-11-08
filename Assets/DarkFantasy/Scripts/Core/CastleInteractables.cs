using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Something the local player can use with Interact (E / A). The UI shows the prompt.</summary>
    public interface IInteractable
    {
        /// <summary>True if Interact does something now; prompt != null shows a hint even when it can't.</summary>
        bool LocalCanInteract(out string prompt);
    }

    public static class Interactables
    {
        public static readonly List<IInteractable> All = new List<IInteractable>();
    }
}
