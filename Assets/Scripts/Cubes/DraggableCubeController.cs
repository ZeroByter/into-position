using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroByterGames.GetIntoPosition.Cubes {
    public class DraggableCubeController : MonoBehaviour
	{
        public bool isGettingDragged = false;

        private bool isMouseOver;

        new private Camera camera;

        private void Awake()
        {
            camera = Camera.main;
        }

        private void OnMouseOver()
        {
            isMouseOver = true;
        }

        private void OnMouseExit()
        {
            if (isMouseOver && Input.GetMouseButton(0)) return;

            isMouseOver = false;
        }

        private void Update()
        {
            if(isMouseOver && Input.GetMouseButtonDown(0))
            {
                isGettingDragged = true;
            }

            if (isGettingDragged && !EventSystem.current.IsPointerOverGameObject())
            {
                transform.position = (Vector2)camera.ScreenToWorldPoint(Input.mousePosition);
            }

#if UNITY_ANDROID
            if (Input.GetMouseButtonUp(0) || Input.touchCount == 0/* && (isMouseOver || isGettingDragged)*/)
#else
            if (Input.GetMouseButtonUp(0) && (isMouseOver || isGettingDragged))
#endif
            {
                isMouseOver = false;
                isGettingDragged = false;
            }
        }
    }
}