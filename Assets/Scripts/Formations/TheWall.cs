using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations {
    [Serializable]
    public class TheWall : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);
            float distanceBetweenCubes = 2;

            var wallDirection = Quaternion.AngleAxis(90, Vector3.forward) * (direction - origin).normalized;
            var dir = wallDirection * index * distanceBetweenCubes;

            var point = origin + (direction - origin).normalized * 3f + (-wallDirection * distanceBetweenCubes * ((cubes.Count - 1) * 0.5f));

            return point + dir;
        }
    }
}