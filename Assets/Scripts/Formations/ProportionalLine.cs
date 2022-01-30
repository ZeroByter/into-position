using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations {
    [Serializable]
    public class ProportionalLine : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);
            
            return Vector2.Lerp(origin, direction, (index + 1f) / (cubes.Count + 1f));
        }
    }
}