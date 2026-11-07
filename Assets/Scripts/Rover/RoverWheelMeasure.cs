using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class RoverWheelMeasure : MonoBehaviour
{
    [ContextMenu("Measure World Bounds")]
    private void MeasureWorldBounds()
    {
        Vector3 size = GetComponent<MeshRenderer>().bounds.size;

        Debug.Log(
            $"{name} — dimensions monde : " +
            $"X = {size.x:F4}, Y = {size.y:F4}, Z = {size.z:F4} unités",
            this
        );
    }
}