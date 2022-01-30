using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroByterGames.GetIntoPosition {
    public class CameraController : MonoBehaviour
	{
        private float targetSize = 0.2f;
        private Vector2 draggingStartCameraPosition;
        private Vector2 draggingStartMousePosition;
        private float minSize = 2;
        private float maxSize = 300;

        new private Camera camera;

        private void Awake()
        {
            camera = GetComponent<Camera>();
        }

        private void Update()
        {
            if (EventSystem.current.IsPointerOverGameObject()) return;

#if UNITY_ANDROID
            //zooming
            if (Input.touchCount == 2)
            {
                Touch touchZero = Input.GetTouch(0);
                Touch touchOne = Input.GetTouch(1);

                Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
                Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;

                float prevMagnitude = (touchZeroPrevPos - touchOnePrevPos).magnitude;
                float currentMagnitude = (touchZero.position - touchOne.position).magnitude;

                float difference = currentMagnitude - prevMagnitude;

                camera.orthographicSize += difference * -0.1f;
                camera.orthographicSize = Mathf.Max(3, camera.orthographicSize);
            }

            //moving camera
            if (Input.GetMouseButtonDown(1))
            {
                draggingStartCameraPosition = transform.position;
                draggingStartMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(1))
            {
                Vector2 mousePos = camera.ScreenToWorldPoint(Input.mousePosition);

                transform.position = new Vector3(0, 0, -30) + (Vector3)(draggingStartCameraPosition + ((Vector2)camera.ScreenToWorldPoint(draggingStartMousePosition) - mousePos));
            }
#else
            if (Input.GetMouseButtonDown(1))
            {
                draggingStartCameraPosition = transform.position;
                draggingStartMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(1))
            {
                Vector2 mousePos = camera.ScreenToWorldPoint(Input.mousePosition);

                transform.position = new Vector3(0, 0, -30) + (Vector3)(draggingStartCameraPosition + ((Vector2)camera.ScreenToWorldPoint(draggingStartMousePosition) - mousePos));
            }

            //detecting when to zoom the camera
            float mouseScrollwheel = Input.GetAxis("Mouse ScrollWheel");
            if (mouseScrollwheel > 0)
            {
                targetSize -= 0.1f;
            }
            else if (mouseScrollwheel < 0)
            {
                targetSize += 0.1f;
            }

            //standard boundary checks
            if (targetSize > 1) targetSize = 1;
            if (targetSize < 0) targetSize = 0;

            float orthoSize = Mathf.Lerp(minSize, maxSize, targetSize);
            camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, orthoSize, 0.65f);
            if (Mathf.Approximately(camera.orthographicSize, orthoSize)) camera.orthographicSize = orthoSize;
#endif
        }
    }
}