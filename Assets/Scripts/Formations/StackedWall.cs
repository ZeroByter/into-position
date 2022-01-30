using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations {
    [Serializable]
    public class StackedWall : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);
            float distanceBetweenCubes = 2;

            var wallDirection = Quaternion.AngleAxis(90, Vector3.forward) * (direction - origin).normalized;
            var dir = wallDirection * (index % 6) * distanceBetweenCubes;

            float distanceFromOrigin = 3f * Mathf.CeilToInt((index + 1) / 6f);

            var point = origin + (direction - origin).normalized * distanceFromOrigin + (-wallDirection * distanceBetweenCubes * (Mathf.Min(5, cubes.Count - 1) * 0.5f));

            return point + dir;
        }
    }
}