using LuaInterface;
using System;
using UnityEngine;
using ZeroByterGames.GetIntoPosition.UI;

namespace ZeroByterGames.GetIntoPosition
{
    public class LuaStateManager : MonoBehaviour
    {
        public static LuaState GetLuaState()
        {
            if (Singleton == null) return null;

            return Singleton.luaState;
        }

        public static void RunNotepadString()
        {
            if (Singleton == null) return;

            string code = UIController.GetLuaCodeString();

            try
            {
                Singleton.luaState = new LuaState();
                Singleton.luaState.Start();
                Singleton.luaState.DoString(code);
                Singleton.getCubePositionLuaFunc = Singleton.luaState.GetFunction("GetCubePosition");
            }
            catch (LuaException e)
            {
                Debug.LogError(e);
            }
        }

        public static LuaFunction GetGetCubePositionFunc()
        {
            if (Singleton == null) return null;

            return Singleton.getCubePositionLuaFunc;
        }

        private static LuaStateManager Singleton;

        private LuaState luaState;
        private LuaFunction getCubePositionLuaFunc;

        private void Awake()
        {
#if UNITY_STANDALONE_WIN
            if (Singleton != null)
            {
                Destroy(null);
                return;
            }

            Singleton = this;
            DontDestroyOnLoad(gameObject);
#endif
        }

        private void OnDestroy()
        {
            if (Singleton != null) return;

#if UNITY_STANDALONE_WIN
            if (luaState != null)
            {
                luaState.Dispose();
                luaState = null;
            }
#endif
        }
    }
}