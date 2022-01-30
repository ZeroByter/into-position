using System;
using UnityEngine;

namespace ZeroByterGames.GetIntoPosition.Cubes {
    public class CubeController : MonoBehaviour
	{
        public static Action<float> NewSpeedSet;

        [Range(0f,100f)]
        public float speed;

        private bool added = false;
        private Vector2 targetPosition;
        private DraggableCubeController draggableController;

        private float test = 0;

        private void Start()
        {
            draggableController = GetComponent<DraggableCubeController>();
            
            FormationsManager.AddCube(this);
            added = true;

            NewSpeedSet += OnNewSpeedSet;
        }

        private void OnDestroy()
        {
            FormationsManager.RemoveCube(this);

            NewSpeedSet -= OnNewSpeedSet;
        }

        private void Update()
        {
            if (draggableController.isGettingDragged) return;
            if (!added) return;

            var currentFormation = FormationsManager.GetCurrentFormation();
            
            if (currentFormation != null)
            {
                targetPosition = currentFormation.GetCubePosition(this);
            }
            else //lua dynamic formation!
            {
                test = Time.time;

                var func = LuaStateManager.GetGetCubePositionFunc();

                if (func != null)
                {
                    var cubes = FormationsManager.GetCubes();
                    int index = cubes.IndexOf(this);

                    try
                    {
                        targetPosition = func.Invoke<Vector2, Vector2, int, int, Vector2>(FormationsManager.GetOriginCube().position, FormationsManager.GetDirectionCube().position, index, cubes.Count);
                    }
                    catch (Exception)
                    {
                        targetPosition = transform.position;
                    }
                }
                else
                {
                    targetPosition = transform.position;
                }
            }

            Vector2 directionToPosition = ((Vector2)transform.position - targetPosition).normalized * Mathf.Min(speed, Vector2.Distance(transform.position, targetPosition));

            transform.position = (Vector2)transform.position - directionToPosition;
            transform.rotation = Quaternion.LookRotation(FormationsManager.GetDirectionCube().position - FormationsManager.GetOriginCube().position);
        }

        private void OnNewSpeedSet(float newSpeed)
        {
            speed = newSpeed;
        }
    }
}