using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;

namespace ZeroByterGames.GetIntoPosition.Formations {
    [Serializable]
    public abstract class Formation
    {
        public abstract Vector2 GetCubePosition(CubeController controller);
    }
}