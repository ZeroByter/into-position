using UnityEngine;

namespace ZeroByterGames.GetIntoPosition.Cubes {
    public class DestroyableCubeController : MonoBehaviour
	{
        private bool isMouseOver;

        private void OnMouseOver()
        {
            isMouseOver = true;
        }

        private void OnMouseExit()
        {
            isMouseOver = false;
        }

        private void Update()
        {
            if(Input.GetMouseButtonDown(1) && isMouseOver)
            {
                Destroy(gameObject);
            }
        }
    }
}