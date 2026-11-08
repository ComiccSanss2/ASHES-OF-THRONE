using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Esegue GameInput.Tick() in LateUpdate (dopo che tutti hanno letto l'input del frame).</summary>
    [DefaultExecutionOrder(10000)]
    public class InputUpdater : MonoBehaviour
    {
        void LateUpdate() => GameInput.Tick();
    }
}
