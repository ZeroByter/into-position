using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations
{
    [Serializable]
    public class Random : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);

            float distance = Vector2.Distance(origin, direction);

            return Vector2.Lerp(origin, direction, 0.5f) + new Vector2(UnityEngine.Random.Range(-distance, distance), UnityEngine.Random.Range(-distance, distance));
        }
    }
}