using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations {
    [Serializable]
    public class RotatingCircle : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);
            float inc = FormationsManager.IncreasingInterval * 5;

            var directionInCircle = Quaternion.AngleAxis(360f / cubes.Count * index + inc, Vector3.forward) * (direction - origin).normalized;
            var dir = directionInCircle * (Vector2.Distance(origin, direction) * 0.4f);// Mathf.Pow(cubes.Count + 1, 0.675f);

            return Vector3.Lerp(origin, direction, 0.5f) + dir;// new Vector2(Mathf.Sin(inc) * cubes.Count, Mathf.Cos(inc) * cubes.Count);
        }
    }
}