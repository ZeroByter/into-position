using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace ZeroByterGames.GetIntoPosition.UI {
    public class HoldableButtonController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField]
        public UnityEvent onHoldDown;

        private bool isHeldDown = false;
        private float heldDownStart = 0;

        private void Update()
        {
            if(isHeldDown && Time.time - heldDownStart > 0.15f)
            {
                onHoldDown?.Invoke();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isHeldDown = true;
            heldDownStart = Time.time;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isHeldDown = false;
        }
    }
}