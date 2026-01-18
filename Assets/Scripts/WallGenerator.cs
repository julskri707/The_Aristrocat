using System.Collections.Generic;
using UnityEngine;

public class WallGenerator : MonoBehaviour
{
    public GameObject wallSegmentPrefab;
    public float wallHeight = 2.5f;
    public float segmentLength = 1.0f;

    public void GenerateWalls(List<Vector3> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[i + 1];

            float distance = Vector3.Distance(a, b);
            int count = Mathf.CeilToInt(distance / segmentLength);

            for (int j = 0; j < count; j++)
            {
                float t = (float)j / count;
                Vector3 pos = Vector3.Lerp(a, b, t);

                GameObject seg = Instantiate(wallSegmentPrefab, pos, Quaternion.identity, transform);

                Vector3 dir = (b - a).normalized;
                seg.transform.rotation = Quaternion.LookRotation(dir);

                seg.transform.localScale = new Vector3(
                    seg.transform.localScale.x,
                    wallHeight,
                    seg.transform.localScale.z
                );
            }
        }
    }
}
