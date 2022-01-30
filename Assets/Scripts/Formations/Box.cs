using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations
{
    [Serializable]
    public class Box : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);
            float distanceBetweenCubes = 1.5f;

            int rows = Mathf.RoundToInt(Mathf.Sqrt(cubes.Count));

            var wallDirection = Quaternion.AngleAxis(90, Vector3.forward) * (direction - origin).normalized;
            var dir = wallDirection * (index % rows) * distanceBetweenCubes;

            float distanceFromOrigin = 3f + 1.5f * Mathf.CeilToInt((index + 1) / (float)rows);

            var point = origin + (direction - origin).normalized * distanceFromOrigin + (-wallDirection * distanceBetweenCubes * (Mathf.Min(rows - 1, cubes.Count - 1) * 0.5f));

            return point + dir;
        }
    }
}