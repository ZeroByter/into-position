using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations {
    [Serializable]
    public class Het : Formation
    {
        public override Vector2 GetCubePosition(CubeController controller)
        {
            var origin = FormationsManager.GetOriginCube().position;
            var direction = FormationsManager.GetDirectionCube().position;
            var cubes = FormationsManager.GetCubes();
            int index = cubes.IndexOf(controller);
            float distanceBetweenCubes = 2;

            if (cubes.Count < 4) return new Box().GetCubePosition(controller);

            int numberOfCubesInBase = Mathf.RoundToInt(cubes.Count * 0.5f);
            if ((cubes.Count - numberOfCubesInBase) % 2 != 0) numberOfCubesInBase++;

            var wallDirection = Quaternion.AngleAxis(90, Vector3.forward) * (direction - origin).normalized;

            if (index < numberOfCubesInBase)
            {
                var dir = wallDirection * index * distanceBetweenCubes;

                var point = origin + (direction - origin).normalized * 3f + (-wallDirection * distanceBetweenCubes * ((Mathf.Min(cubes.Count, numberOfCubesInBase) - 1) * 0.5f));

                return point + dir;
            }
            else
            {
                int newIndex = index - numberOfCubesInBase;

                float indexOffset = 0;
                if (newIndex % 2 == 0) indexOffset -= 1f;

                if (index % 2 == 0)
                {
                    var dir = (direction - origin).normalized * (cubes.Count - index + 3.5f + indexOffset) * (distanceBetweenCubes / 2);
                    //calculating the end position of the base
                    var point = origin + (-wallDirection * distanceBetweenCubes * ((Mathf.Min(cubes.Count, numberOfCubesInBase) + 1.5f) * 0.5f));

                    return point + dir;
                }
                else
                {
                    var dir = (direction - origin).normalized * (cubes.Count - index + 3.5f + indexOffset) * (distanceBetweenCubes / 2);
                    //calculating the start position of the base
                    var point = origin + (-wallDirection * distanceBetweenCubes * ((Mathf.Min(cubes.Count, numberOfCubesInBase) + 1.5f) * -0.5f));

                    return point + dir;
                }
            }
        }
    }
}