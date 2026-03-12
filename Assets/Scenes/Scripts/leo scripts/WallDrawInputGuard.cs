using UnityEngine;
using UnityEngine.EventSystems;

public class WallDrawInputGuard : MonoBehaviour
{
    [Header("Assign")]
    public MonoBehaviour wallDrawInputBehaviour; // ton WallDrawInput (ou autre script de dessin)

    void Awake()
    {
        if (wallDrawInputBehaviour == null)
        {
            // auto : prend le premier MonoBehaviour qui contient "WallDrawInput"
            var monos = GetComponents<MonoBehaviour>();
            foreach (var m in monos)
            {
                if (m == null) continue;
                if (m.GetType().Name.Contains("WallDrawInput"))
                {
                    wallDrawInputBehaviour = m;
                    break;
                }
            }
        }
    }

    void Update()
    {
        if (wallDrawInputBehaviour == null) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool draggingHandle = ControlPointHandleUI.IsDraggingAnyHandle;

        // ✅ bloque le dessin si tu interagis avec l'UI ou si tu drags un handle
        bool shouldBlock = overUI || draggingHandle;

        if (wallDrawInputBehaviour.enabled == shouldBlock)
            wallDrawInputBehaviour.enabled = !shouldBlock;
    }
}
