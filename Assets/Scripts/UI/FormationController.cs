using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZeroByterGames.GetIntoPosition.Formations;

namespace ZeroByterGames.GetIntoPosition.UI
{
    public class FormationController : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text text;

        private Formation formation;
        private Image image;

        public void Setup(Formation formation)
        {
            image = GetComponent<Image>();
            this.formation = formation;
            
            text.text = $"{formation.GetType().Name}";

            gameObject.SetActive(true);
        }

        public void Setup()
        {
            image = GetComponent<Image>();
            this.formation = null;

            text.text = $"Dynamic Lua Formation";

            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (image == null) return;

            if (FormationsManager.GetCurrentFormation() == formation)
            {
                image.color = new Color(1, 0, 0, image.color.a);
            }
            else
            {
                image.color = new Color(0, 0, 0, image.color.a);
            }
        }

        public void Click()
        {
            if(formation == null) //lua formation!
            {
                FormationsManager.SetCurrentFormation(-1);

                LuaNotepadController.Open();
            }
            else
            {
#if UNITY_STANDALONE_WIN
                FormationsManager.SetCurrentFormation(transform.GetSiblingIndex() - 2);
#else
                FormationsManager.SetCurrentFormation(transform.GetSiblingIndex() - 1);
#endif

                LuaNotepadController.Close();
            }
        }
    }
}