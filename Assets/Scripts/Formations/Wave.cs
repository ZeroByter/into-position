using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations
{
    [Serializable]
    public class Wave : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);

            Vector3 forwardDir = (direction - origin).normalized;
            Vector3 leftDir = Quaternion.AngleAxis(90, Vector3.forward) * forwardDir;

            return origin + forwardDir * Mathf.Lerp(0, Vector2.Distance(origin, direction), (index + 1f) / (cubes.Count + 1f)) + leftDir * Mathf.Sin(FormationsManager.IncreasingInterval + index);
        }
    }
}