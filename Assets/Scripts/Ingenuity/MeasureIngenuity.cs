using UnityEngine;

public class MeasureIngenuity : MonoBehaviour
{
    void Start()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            Debug.Log(
                r.name +
                " | Dimensions : " +
                r.bounds.size
            );
        }
    }
}