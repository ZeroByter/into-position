using System.Collections;
using TMPro;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.Cubes;
using ZeroByterGames.GetIntoPosition.Formations;

namespace ZeroByterGames.GetIntoPosition.UI {
    public class UIController : MonoBehaviour
	{
        public static string GetLuaCodeString()
        {
            if (Singleton == null) return "";

            return Singleton.luaCodeInput.text;
        }

        private static UIController Singleton;

        [SerializeField]
        private FormationController formationTemplate;
        [SerializeField]
        private Transform cubesParent;
        [SerializeField]
        private TMP_Text cubesCounter;
        [SerializeField]
        private Material cubeMaterial;
        [SerializeField]
        private TMP_InputField luaCodeInput;

        private CubeController lastCube;
        private float currentSpeed = 0.1f;

        private bool ranNewLuaCode;
        private float lastNewLuaCode;

        private void Awake()
        {
            Singleton = this;
        }

        private void Start()
        {
            formationTemplate.gameObject.SetActive(false);

#if UNITY_STANDALONE_WIN
            var dynamicFormationController = Instantiate(formationTemplate, formationTemplate.transform.parent);
            dynamicFormationController.Setup();
#endif

            foreach (Formation formation in FormationsManager.GetAllFormations())
            {
                var controller = Instantiate(formationTemplate, formationTemplate.transform.parent);
                controller.Setup(formation);
            }

            StartCoroutine(UpdateCubesCounter());
        }

        private void Update()
        {
            if(Time.time - lastNewLuaCode > 0.125f && !ranNewLuaCode)
            {
                ranNewLuaCode = true;
                LuaStateManager.RunNotepadString();
            }
        }

        public void NewCubeSpeeds(float newSpeed)
        {
            currentSpeed = newSpeed;
            CubeController.NewSpeedSet?.Invoke(newSpeed * 10);
        }

        public void AddNewCube()
        {
            lastCube = GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<CubeController>();
            lastCube.transform.parent = cubesParent;
            lastCube.speed = currentSpeed * 10;
            lastCube.gameObject.AddComponent<DraggableCubeController>();
            lastCube.gameObject.AddComponent<DestroyableCubeController>();
            lastCube.GetComponent<Renderer>().material = cubeMaterial;

            StartCoroutine(UpdateCubesCounter());
        }

        private IEnumerator UpdateCubesCounter()
        {
            yield return new WaitForFixedUpdate();

            int count = FormationsManager.GetCubes().Count;
            if (count == 1)
            {
                cubesCounter.text = $"{count} cube";
            }
            else
            {
                cubesCounter.text = $"{count} cubes";
            }
        }

        public void RemoveLastCube()
        {
            var cubes = FormationsManager.GetCubes();

            if (cubes.Count == 0) return;

            Destroy(cubes[cubes.Count - 1].gameObject);

            StartCoroutine(UpdateCubesCounter());
        }

        public void LuaCodeChanged(string newLoadCode)
        {
            ranNewLuaCode = false;
            lastNewLuaCode = Time.time;
        }
    }
}