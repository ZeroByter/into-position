using System.Collections.Generic;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;
using ZeroByterGames.GetIntoPosition.Formations;

namespace ZeroByterGames.GetIntoPosition {
    public class FormationsManager : MonoBehaviour
	{
        public static float IncreasingInterval = 0;
        public static FormationsManager Singleton;

        public static Formation GetCurrentFormation()
        {
            if (Singleton == null) return null;

            if(Singleton.currentFormationIndex == -1) //lua dynamic formation
            {
                return null;
            }
            else
            {
                return Singleton.formations[Singleton.currentFormationIndex];
            }
        }

        public static void SetCurrentFormation(int index)
        {
            if (Singleton == null) return;

            Singleton.currentFormationIndex = index;
        }

        public static List<Formation> GetAllFormations()
        {
            if (Singleton == null) return null;

            return Singleton.formations;
        }

        public static void AddCube(CubeController controller)
        {
            if (Singleton == null) return;

            Singleton.cubes.Add(controller);
        }

        public static void RemoveCube(CubeController controller)
        {
            if (Singleton == null) return;

            Singleton.cubes.Remove(controller);
        }

        public static List<CubeController> GetCubes()
        {
            if (Singleton == null) return null;

            return Singleton.cubes;
        }

        public static Transform GetOriginCube()
        {
            if (Singleton == null) return null;

            return Singleton.originCube;
        }

        public static Transform GetDirectionCube()
        {
            if (Singleton == null) return null;

            return Singleton.directionCube;
        }

        private List<Formation> formations = new List<Formation>();
        [SerializeField]
        private Material cubeMaterial;
        public int currentFormationIndex = 0;

        private List<CubeController> cubes = new List<CubeController>();

        private Transform originCube;
        private Transform directionCube;

        private void Awake()
        {
            Singleton = this;

            //Create origin cube;
            originCube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            originCube.parent = transform;
            originCube.position = new Vector2(-20, 20);
            originCube.name = "Origin Cube";
            var originRenderer = originCube.GetComponent<Renderer>();
            originRenderer.material = cubeMaterial;
            originRenderer.material.color = Color.green;
            originCube.gameObject.AddComponent<DraggableCubeController>();

            //Create direction cube;
            directionCube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            directionCube.parent = transform;
            directionCube.position = new Vector2(20, 20);
            directionCube.name = "Direction Cube";
            var directionRenderer = directionCube.GetComponent<Renderer>();
            directionRenderer.material = cubeMaterial;
            directionRenderer.material.color = Color.red;
            directionCube.gameObject.AddComponent<DraggableCubeController>();

            //Automatically add all formations
            foreach (var formation in ReflectiveEnumerator.GetEnumerableOfType<Formation>())
            {
                formations.Add(formation);
            }
        }

        private void Update()
        {
            IncreasingInterval += 0.1f;
        }
    }
}