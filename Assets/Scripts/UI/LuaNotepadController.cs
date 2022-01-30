using UnityEngine;

namespace ZeroByterGames.GetIntoPosition.UI {
    public class LuaNotepadController : MonoBehaviour
	{
        public static void Open()
        {
            if (Singleton == null) return;

            Singleton._Open();
        }

        public static void Close()
        {
            if (Singleton == null) return;

            Singleton._Close();
        }

        private static LuaNotepadController Singleton;

        private RectTransform rect;

        [SerializeField]
        private bool isOpen = false;

        private void Awake()
        {
            Singleton = this;

            rect = GetComponent<RectTransform>();

            rect.anchoredPosition = new Vector3(rect.sizeDelta.x, 0, 0);
        }

        private void Update()
        {
            if (isOpen)
            {
                rect.anchoredPosition = Vector3.Lerp(rect.anchoredPosition, Vector3.zero, 0.25f);
            }
            else
            {
                rect.anchoredPosition = Vector3.Lerp(rect.anchoredPosition, new Vector3(rect.sizeDelta.x, 0, 0), 0.25f);
            }
        }

        public void _Open()
        {
            if (isOpen) return;

            isOpen = true;
        }

        public void _Close()
        {
            if (!isOpen) return;

            isOpen = false;
        }
    }
}