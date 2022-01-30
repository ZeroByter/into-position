/*
Copyright (c) 2015-2017 topameng(topameng@qq.com)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
#define MISS_WARNING

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LuaInterface
{
    public class LuaState : LuaStatePtr, IDisposable
    {
        public ObjectTranslator translator = new ObjectTranslator();
        public LuaReflection reflection = new LuaReflection();

        public int ArrayMetatable { get; private set; }
        public int DelegateMetatable { get; private set; }
        public int TypeMetatable { get; private set; }
        public int EnumMetatable { get; private set; }
        public int IterMetatable { get; private set; }        
        public int EventMetatable { get; private set; }

        //function ref                
        public int PackBounds { get; private set; }
        public int UnpackBounds { get; private set; }
        public int PackRay { get; private set; }
        public int UnpackRay { get; private set; }
        public int PackRaycastHit { get; private set; }        
        public int PackTouch { get; private set; }

        public bool LogGC 
        {
            get
            {
                return beLogGC;
            }

            set
            {
                beLogGC = value;
                translator.LogGC = value;
            }
        }

        public Action OnDestroy = delegate { };
        
        Dictionary<string, WeakReference> funcMap = new Dictionary<string, WeakReference>();
        Dictionary<int, WeakReference> funcRefMap = new Dictionary<int, WeakReference>();
        Dictionary<long, WeakReference> delegateMap = new Dictionary<long, WeakReference>();

        List<GCRef> gcList = new List<GCRef>();
        List<LuaBaseRef> subList = new List<LuaBaseRef>();

        Dictionary<Type, int> metaMap = new Dictionary<Type, int>();        
        Dictionary<Enum, object> enumMap = new Dictionary<Enum, object>();
        Dictionary<Type, LuaCSFunction> preLoadMap = new Dictionary<Type, LuaCSFunction>();

        Dictionary<int, Type> typeMap = new Dictionary<int, Type>();
        HashSet<Type> genericSet = new HashSet<Type>();
        HashSet<string> moduleSet = null;

        private static LuaState mainState = null;
        private static LuaState injectionState = null;
        private static Dictionary<IntPtr, LuaState> stateMap = new Dictionary<IntPtr, LuaState>();

        private int beginCount = 0;
        private bool beLogGC = false;
        private bool bInjectionInited = false;
#if UNITY_EDITOR
        private bool beStart = false;
#endif

#if MISS_WARNING
        HashSet<Type> missSet = new HashSet<Type>();
#endif

        public LuaState()            
        {
            if (mainState == null)
            {
                mainState = this;
                // MULTI_STATE Not Support
                injectionState = mainState;
            }

            float time = Time.realtimeSinceStartup;
            InitTypeTraits();
            InitStackTraits();
            L = LuaNewState();            
            LuaException.Init(L);
            stateMap.Add(L, this);                        
            OpenToLuaLibs();            
            ToLua.OpenLibs(L);
            OpenBaseLibs();
            LuaSetTop(0);
            InitLuaPath();
            Debugger.Log("Init lua state cost: {0}", Time.realtimeSinceStartup - time);
        }        

        void OpenBaseLibs()
        {            
            BeginModule(null);

            BeginModule("System");
            System_ObjectWrap.Register(this);
            System_NullObjectWrap.Register(this);            
            System_StringWrap.Register(this);
            System_DelegateWrap.Register(this);
            System_EnumWrap.Register(this);
            System_ArrayWrap.Register(this);
            System_TypeWrap.Register(this);                                               
            BeginModule("Collections");
            System_Collections_IEnumeratorWrap.Register(this);

            BeginModule("ObjectModel");
            System_Collections_ObjectModel_ReadOnlyCollectionWrap.Register(this);
            EndModule();//ObjectModel

            BeginModule("Generic");
            System_Collections_Generic_ListWrap.Register(this);
            System_Collections_Generic_DictionaryWrap.Register(this);
            System_Collections_Generic_KeyValuePairWrap.Register(this);

            BeginModule("Dictionary");
            System_Collections_Generic_Dictionary_KeyCollectionWrap.Register(this);
            System_Collections_Generic_Dictionary_ValueCollectionWrap.Register(this);
            EndModule();//Dictionary
            EndModule();//Generic
            EndModule();//Collections     
            EndModule();//end System

            BeginModule("LuaInterface");
            LuaInterface_LuaOutWrap.Register(this);
            LuaInterface_EventObjectWrap.Register(this);
            EndModule();//end LuaInterface

            BeginModule("UnityEngine");
            UnityEngine_ObjectWrap.Register(this);            
            UnityEngine_CoroutineWrap.Register(this);
            EndModule(); //end UnityEngine

            EndModule(); //end global
                        
            LuaUnityLibs.OpenLibs(L);            
            LuaReflection.OpenLibs(L);
            ArrayMetatable = metaMap[typeof(System.Array)];
            TypeMetatable = metaMap[typeof(System.Type)];
            DelegateMetatable = metaMap[typeof(System.Delegate)];
            EnumMetatable = metaMap[typeof(System.Enum)];
            IterMetatable = metaMap[typeof(IEnumerator)];
            EventMetatable = metaMap[typeof(EventObject)];
        }

        void InitLuaPath()
        {
            InitPackagePath();

            if (!LuaFileUtils.Instance.beZip)
            {
#if UNITY_EDITOR
                if (!Directory.Exists(LuaConst.luaDir))
                {
                    string msg = string.Format("luaDir path not exists: {0}, configer it in LuaConst.cs", LuaConst.luaDir);
                    throw new LuaException(msg);
                }

                if (!Directory.Exists(LuaConst.toluaDir))
                {
                    string msg = string.Format("toluaDir path not exists: {0}, configer it in LuaConst.cs", LuaConst.toluaDir);
                    throw new LuaException(msg);
                }

                AddSearchPath(LuaConst.toluaDir);
                AddSearchPath(LuaConst.luaDir);
#endif
                if (LuaFileUtils.Instance.GetType() == typeof(LuaFileUtils))
                {
                    AddSearchPath(LuaConst.luaResDir);
                }
            }
        }

        void OpenBaseLuaLibs()
        {
            DoFile("tolua.lua");            //tolua table名字已经存在了,不能用require

            /*this["Mathf"] = DoString<object>(LoadMathf);
            this["Vector3"] = DoString<object>(LoadVector3);
            this["Quaternion"] = DoString<object>(LoadQuaternion);
            this["Vector2"] = DoString<object>(LoadVector2);
            this["Vector4"] = DoString<object>(LoadVector4);
            this["Color"] = DoString<object>(LoadColor);
            this["Ray"] = DoString<object>(LoadRay);
            this["Bounds"] = DoString<object>(LoadBounds);
            this["RaycastHit"] = DoString<object>(LoadRaycastHit);
            this["Touch"] = DoString<object>(LoadTouch);
            this["LayerMask"] = DoString<object>(LoadLayerMask);
            this["Plane"] = DoString<object>(LoadPlane);
            this["Time"] = DoString<object>(LoadTime);
            this["list"] = DoString<object>(LoadList);
            this["utf8"] = DoString<object>(LoadUTF8);
            DoString(LoadEvent);
            DoString(LoadTypeof);
            DoString(LoadSlot);
            DoString(LoadTimer);
            DoString(LoadCoroutine);
            DoString(LoadValueType);
            DoString(LoadBindingFlags);*/

            LuaUnityLibs.OpenLuaLibs(L);
        }

        public void Start()
        {
#if UNITY_EDITOR
            beStart = true;
#endif
            Debugger.Log("LuaState start");
            OpenBaseLuaLibs();
#if ENABLE_LUA_INJECTION
            Push(LuaDLL.tolua_tag());
            LuaSetGlobal("tolua_tag");
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetInt(Application.dataPath + "InjectStatus") == 1)
            { 
#endif
                DoFile("System/Injection/LuaInjectionStation.lua");
                bInjectionInited = true;
#if UNITY_EDITOR
            }
#endif
#endif
            PackBounds = GetFuncRef("Bounds.New");
            UnpackBounds = GetFuncRef("Bounds.Get");
            PackRay = GetFuncRef("Ray.New");
            UnpackRay = GetFuncRef("Ray.Get");
            PackRaycastHit = GetFuncRef("RaycastHit.New");
            PackTouch = GetFuncRef("Touch.New");
        }

        public int OpenLibs(LuaCSFunction open)
        {
            int ret = open(L);            
            return ret;
        }

        public void BeginPreLoad()
        {
            LuaGetGlobal("package");
            LuaGetField(-1, "preload");
            moduleSet = new HashSet<string>();
        }

        public void EndPreLoad()
        {
            LuaPop(2);
            moduleSet = null;
        }

        public void AddPreLoad(string name, LuaCSFunction func, Type type)
        {            
            if (!preLoadMap.ContainsKey(type))
            {
                LuaDLL.tolua_pushcfunction(L, func);
                LuaSetField(-2, name);
                preLoadMap[type] = func;
                string module = type.Namespace;

                if (!string.IsNullOrEmpty(module) && !moduleSet.Contains(module))
                {
                    LuaDLL.tolua_addpreload(L, module);
                    moduleSet.Add(module);
                }
            }            
        }

        //慎用，需要自己保证不会重复Add相同的name,并且上面函数没有使用过这个name
        public void AddPreLoad(string name, LuaCSFunction func)
        {
            LuaDLL.tolua_pushcfunction(L, func);
            LuaSetField(-2, name);
        }

        public int BeginPreModule(string name)
        {
            int top = LuaGetTop();

            if (string.IsNullOrEmpty(name))
            {
                LuaDLL.lua_pushvalue(L, LuaIndexes.LUA_GLOBALSINDEX);
                ++beginCount;
                return top;
            }
            else if (LuaDLL.tolua_beginpremodule(L, name))
            {
                ++beginCount;
                return top;
            }
            
            throw new LuaException(string.Format("create table {0} fail", name));            
        }

        public void EndPreModule(int reference)
        {
            --beginCount;            
            LuaDLL.tolua_endpremodule(L, reference);
        }

        public void EndPreModule(IntPtr L, int reference)
        {
            --beginCount;
            LuaDLL.tolua_endpremodule(L, reference);
        }

        public void BindPreModule(Type t, LuaCSFunction func)
        {
            preLoadMap[t] = func;
        }

        public LuaCSFunction GetPreModule(Type t)
        {
            LuaCSFunction func = null;
            preLoadMap.TryGetValue(t, out func);
            return func;
        }

        public bool BeginModule(string name)
        {
#if UNITY_EDITOR
            if (name != null)
            {                
                LuaTypes type = LuaType(-1);

                if (type != LuaTypes.LUA_TTABLE)
                {                    
                    throw new LuaException("open global module first");
                }
            }
#endif
            if (LuaDLL.tolua_beginmodule(L, name))
            {
                ++beginCount;
                return true;
            }

            LuaSetTop(0);
            throw new LuaException(string.Format("create table {0} fail", name));            
        }

        public void EndModule()
        {
            --beginCount;            
            LuaDLL.tolua_endmodule(L);
        }

        void BindTypeRef(int reference, Type t)
        {
            metaMap.Add(t, reference);
            typeMap.Add(reference, t);

            if (t.IsGenericTypeDefinition)
            {
                genericSet.Add(t);
            }
        }

        public Type GetClassType(int reference)
        {
            Type t = null;
            typeMap.TryGetValue(reference, out t);
            return t;
        }

        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        public static int Collect(IntPtr L)
        {
            int udata = LuaDLL.tolua_rawnetobj(L, 1);

            if (udata != -1)
            {
                ObjectTranslator translator = GetTranslator(L);
                translator.RemoveObject(udata);
            }

            return 0;
        }

        public static bool GetInjectInitState(int index)
        {
            if (injectionState != null && injectionState.bInjectionInited)
            {
                return true;
            }

            return false;
        }

        string GetToLuaTypeName(Type t)
        {
            if (t.IsGenericType)
            {
                string str = t.Name;
                int pos = str.IndexOf('`');

                if (pos > 0)
                {
                    str = str.Substring(0, pos);
                }

                return str;
            }

            return t.Name;
        }

        public int BeginClass(Type t, Type baseType, string name = null)
        {
            if (beginCount == 0)
            {
                throw new LuaException("must call BeginModule first");
            }

            int baseMetaRef = 0;
            int reference = 0;            

            if (name == null)
            {
                name = GetToLuaTypeName(t);
            }

            if (baseType != null && !metaMap.TryGetValue(baseType, out baseMetaRef))
            {
                LuaCreateTable();
                baseMetaRef = LuaRef(LuaIndexes.LUA_REGISTRYINDEX);                
                BindTypeRef(baseMetaRef, baseType);
            }

            if (metaMap.TryGetValue(t, out reference))
            {
                LuaDLL.tolua_beginclass(L, name, baseMetaRef, reference);
                RegFunction("__gc", Collect);
            }
            else
            {
                reference = LuaDLL.tolua_beginclass(L, name, baseMetaRef);
                RegFunction("__gc", Collect);                
                BindTypeRef(reference, t);
            }

            return reference;
        }

        public void EndClass()
        {
            LuaDLL.tolua_endclass(L);
        }

        public int BeginEnum(Type t)
        {
            if (beginCount == 0)
            {
                throw new LuaException("must call BeginModule first");
            }

            int reference = LuaDLL.tolua_beginenum(L, t.Name);
            RegFunction("__gc", Collect);            
            BindTypeRef(reference, t);
            return reference;
        }

        public void EndEnum()
        {
            LuaDLL.tolua_endenum(L);
        }

        public void BeginStaticLibs(string name)
        {
            if (beginCount == 0)
            {
                throw new LuaException("must call BeginModule first");
            }

            LuaDLL.tolua_beginstaticclass(L, name);
        }

        public void EndStaticLibs()
        {
            LuaDLL.tolua_endstaticclass(L);
        }

        public void RegFunction(string name, LuaCSFunction func)
        {
            IntPtr fn = Marshal.GetFunctionPointerForDelegate(func);
            LuaDLL.tolua_function(L, name, fn);            
        }

        public void RegVar(string name, LuaCSFunction get, LuaCSFunction set)
        {            
            IntPtr fget = IntPtr.Zero;
            IntPtr fset = IntPtr.Zero;

            if (get != null)
            {
                fget = Marshal.GetFunctionPointerForDelegate(get);
            }

            if (set != null)
            {
                fset = Marshal.GetFunctionPointerForDelegate(set);
            }

            LuaDLL.tolua_variable(L, name, fget, fset);
        }

        public void RegConstant(string name, double d)
        {
            LuaDLL.tolua_constant(L, name, d);
        }

        public void RegConstant(string name, bool flag)
        {
            LuaDLL.lua_pushstring(L, name);
            LuaDLL.lua_pushboolean(L, flag);
            LuaDLL.lua_rawset(L, -3);
        }

        int GetFuncRef(string name)
        {
            if (PushLuaFunction(name, false))
            {
                return LuaRef(LuaIndexes.LUA_REGISTRYINDEX);
            }

            throw new LuaException("get lua function reference failed: " + name);                         
        }

        public static LuaState Get(IntPtr ptr)
        {
#if !MULTI_STATE
            return mainState;
#else

            if (mainState != null && mainState.L == ptr)
            {
                return mainState;
            }            

            LuaState state = null;

            if (stateMap.TryGetValue(ptr, out state))
            {
                return state;
            }
            else
            {
                return Get(LuaDLL.tolua_getmainstate(ptr));
            }
#endif
        }

        public static ObjectTranslator GetTranslator(IntPtr ptr)
        {
#if !MULTI_STATE
            return mainState.translator;
#else
            if (mainState != null && mainState.L == ptr)
            {
                return mainState.translator;
            }

            return Get(ptr).translator;
#endif
        }

        public static LuaReflection GetReflection(IntPtr ptr)
        {
#if !MULTI_STATE
            return mainState.reflection;
#else
            if (mainState != null && mainState.L == ptr)
            {
                return mainState.reflection;
            }

            return Get(ptr).reflection;
#endif
        }

        public void DoString(string chunk, string chunkName = "LuaState.cs")
        {
#if UNITY_EDITOR
            if (!beStart)
            {
                throw new LuaException("you must call Start() first to initialize LuaState");
            }
#endif
            byte[] buffer = Encoding.UTF8.GetBytes(chunk);
            LuaLoadBuffer(buffer, chunkName);
        }

        public T DoString<T>(string chunk, string chunkName = "LuaState.cs")
        {
            byte[] buffer = Encoding.UTF8.GetBytes(chunk);
            return LuaLoadBuffer<T>(buffer, chunkName);
        }

        byte[] LoadFileBuffer(string fileName)
        {
#if UNITY_EDITOR
            if (!beStart)
            {
                throw new LuaException("you must call Start() first to initialize LuaState");
            }
#endif
            byte[] buffer = LuaFileUtils.Instance.ReadFile(fileName);

            if (buffer == null)
            {
                string error = string.Format("cannot open {0}: No such file or directory", fileName);
                error += LuaFileUtils.Instance.FindFileError(fileName);
                throw new LuaException(error);
            }

            return buffer;
        }

        string LuaChunkName(string name)
        {
            if (LuaConst.openLuaDebugger)
            {
                name = LuaFileUtils.Instance.FindFile(name);
            }

            return "@" + name;
        }

        public void DoFile(string fileName)
        {
            byte[] buffer = LoadFileBuffer(fileName);
            fileName = LuaChunkName(fileName);
            LuaLoadBuffer(buffer, fileName);
        }

        public T DoFile<T>(string fileName)
        {
            byte[] buffer = LoadFileBuffer(fileName);
            fileName = LuaChunkName(fileName);
            return LuaLoadBuffer<T>(buffer, fileName);
        }

        //注意fileName与lua文件中require一致。
        public void Require(string fileName)
        {
            int top = LuaGetTop();
            int ret = LuaRequire(fileName);

            if (ret != 0)
            {                
                string err = LuaToString(-1);
                LuaSetTop(top);
                throw new LuaException(err, LuaException.GetLastError());
            }

            LuaSetTop(top);            
        }

        public T Require<T>(string fileName)
        {
            int top = LuaGetTop();
            int ret = LuaRequire(fileName);

            if (ret != 0)
            {
                string err = LuaToString(-1);
                LuaSetTop(top);
                throw new LuaException(err, LuaException.GetLastError());
            }

            T o = CheckValue<T>(-1);
            LuaSetTop(top);
            return o;
        }

        public void InitPackagePath()
        {
            LuaGetGlobal("package");
            LuaGetField(-1, "path");
            string current = LuaToString(-1);
            string[] paths = current.Split(';');

            for (int i = 0; i < paths.Length; i++)
            {
                if (!string.IsNullOrEmpty(paths[i]))
                {
                    string path = paths[i].Replace('\\', '/');
                    LuaFileUtils.Instance.AddSearchPath(path);
                }
            }

            LuaPushString("");            
            LuaSetField(-3, "path");
            LuaPop(2);
        }

        string ToPackagePath(string path)
        {
            using (CString.Block())
            {
                CString sb = CString.Alloc(256);
                sb.Append(path);
                sb.Replace('\\', '/');

                if (sb.Length > 0 && sb[sb.Length - 1] != '/')
                {
                    sb.Append('/');
                }

                sb.Append("?.lua");
                return sb.ToString();
            }
        }

        public void AddSearchPath(string fullPath)
        {
            if (!Path.IsPathRooted(fullPath))
            {
                throw new LuaException(fullPath + " is not a full path");
            }

            fullPath = ToPackagePath(fullPath);
            LuaFileUtils.Instance.AddSearchPath(fullPath);        
        }

        public void RemoveSeachPath(string fullPath)
        {
            if (!Path.IsPathRooted(fullPath))
            {
                throw new LuaException(fullPath + " is not a full path");
            }

            fullPath = ToPackagePath(fullPath);
            LuaFileUtils.Instance.RemoveSearchPath(fullPath);
        }        

        public int BeginPCall(int reference)
        {
            return LuaDLL.tolua_beginpcall(L, reference);
        }

        public void PCall(int args, int oldTop)
        {            
            if (LuaDLL.lua_pcall(L, args, LuaDLL.LUA_MULTRET, oldTop) != 0)
            {
                string error = LuaToString(-1);
                throw new LuaException(error, LuaException.GetLastError());
            }            
        }

        public void EndPCall(int oldTop)
        {
            LuaDLL.lua_settop(L, oldTop - 1);            
        }

        public void PushArgs(object[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                PushVariant(args[i]);
            }
        }

        void CheckNull(LuaBaseRef lbr, string fmt, object arg0)
        {
            if (lbr == null)
            {
                string error = string.Format(fmt, arg0);
                throw new LuaException(error, null, 2);
            }            
        }

        //压入一个存在的或不存在的table, 但不增加引用计数
        bool PushLuaTable(string fullPath, bool checkMap = true)
        {
            if (checkMap)
            {
                WeakReference weak = null;

                if (funcMap.TryGetValue(fullPath, out weak))
                {
                    if (weak.IsAlive)
                    {
                        LuaTable table = weak.Target as LuaTable;
                        CheckNull(table, "{0} not a lua table", fullPath);
                        Push(table);
                        return true;
                    }
                    else
                    {
                        funcMap.Remove(fullPath);
                    }
                }
            }

            if (!LuaDLL.tolua_pushluatable(L, fullPath))
            {                
                return false;
            }

            return true;
        }

        bool PushLuaFunction(string fullPath, bool checkMap = true)
        {
            if (checkMap)
            {
                WeakReference weak = null;

                if (funcMap.TryGetValue(fullPath, out weak))
                {
                    if (weak.IsAlive)
                    {
                        LuaFunction func = weak.Target as LuaFunction;
                        CheckNull(func, "{0} not a lua function", fullPath);

                        if (func.IsAlive)
                        {
                            func.AddRef();
                            return true;
                        }
                    }

                    funcMap.Remove(fullPath);
                }
            }

            int oldTop = LuaDLL.lua_gettop(L);
            int pos = fullPath.LastIndexOf('.');

            if (pos > 0)
            {
                string tableName = fullPath.Substring(0, pos);

                if (PushLuaTable(tableName, checkMap))
                {
                    string funcName = fullPath.Substring(pos + 1);
                    LuaDLL.lua_pushstring(L, funcName);
                    LuaDLL.lua_rawget(L, -2);

                    LuaTypes type = LuaDLL.lua_type(L, -1);

                    if (type == LuaTypes.LUA_TFUNCTION)
                    {
                        LuaDLL.lua_insert(L, oldTop + 1);
                        LuaDLL.lua_settop(L, oldTop + 1);
                        return true;
                    }
                }

                LuaDLL.lua_settop(L, oldTop);
                return false;
            }
            else
            {
                LuaDLL.lua_getglobal(L, fullPath);
                LuaTypes type = LuaDLL.lua_type(L, -1);

                if (type != LuaTypes.LUA_TFUNCTION)
                {
                    LuaDLL.lua_settop(L, oldTop);
                    return false;
                }
            }

            return true;
        }

        void RemoveFromGCList(int reference)
        {            
            lock (gcList)
            {                
                for (int i = 0; i < gcList.Count; i++)
                {
                    if (gcList[i].reference == reference)
                    {
                        gcList.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        public LuaFunction GetFunction(string name, bool beLogMiss = true)
        {
            WeakReference weak = null;

            if (funcMap.TryGetValue(name, out weak))
            {
                if (weak.IsAlive)
                {
                    LuaFunction func = weak.Target as LuaFunction;
                    CheckNull(func, "{0} not a lua function", name);

                    if (func.IsAlive)
                    {
                        func.AddRef();
                        RemoveFromGCList(func.GetReference());
                        return func;
                    }
                }

                funcMap.Remove(name);
            }

            if (PushLuaFunction(name, false))
            {
                int reference = ToLuaRef();

                if (funcRefMap.TryGetValue(reference, out weak))
                {
                    if (weak.IsAlive)
                    {
                        LuaFunction func = weak.Target as LuaFunction;
                        CheckNull(func, "{0} not a lua function", name);

                        if (func.IsAlive)
                        {
                            funcMap.Add(name, weak);
                            func.AddRef();
                            RemoveFromGCList(reference);
                            return func;
                        }
                    }

                    funcRefMap.Remove(reference);
                    delegateMap.Remove(reference);
                }
                
                LuaFunction fun = new LuaFunction(reference, this);
                fun.name = name;
                funcMap.Add(name, new WeakReference(fun));
                funcRefMap.Add(reference, new WeakReference(fun));
                RemoveFromGCList(reference);
                if (LogGC) Debugger.Log("Alloc LuaFunction name {0}, id {1}", name, reference);                
                return fun;
            }

            if (beLogMiss)
            {
                Debugger.Log("Lua function {0} not exists", name);                
            }

            return null;
        }

        LuaBaseRef TryGetLuaRef(int reference)
        {            
            WeakReference weak = null;

            if (funcRefMap.TryGetValue(reference, out weak))
            {
                if (weak.IsAlive)
                {
                    LuaBaseRef luaRef = (LuaBaseRef)weak.Target;

                    if (luaRef.IsAlive)
                    {
                        luaRef.AddRef();
                        return luaRef;
                    }
                }                

                funcRefMap.Remove(reference);                
            }

            return null;
        }

        public LuaFunction GetFunction(int reference)
        {
            LuaFunction func = TryGetLuaRef(reference) as LuaFunction;

            if (func == null)
            {                
                func = new LuaFunction(reference, this);
                funcRefMap.Add(reference, new WeakReference(func));
                if (LogGC) Debugger.Log("Alloc LuaFunction name , id {0}", reference);      
            }

            RemoveFromGCList(reference);
            return func;
        }

        public LuaTable GetTable(string fullPath, bool beLogMiss = true)
        {
            WeakReference weak = null;

            if (funcMap.TryGetValue(fullPath, out weak))
            {
                if (weak.IsAlive)
                {
                    LuaTable table = weak.Target as LuaTable;
                    CheckNull(table, "{0} not a lua table", fullPath);

                    if (table.IsAlive)
                    {
                        table.AddRef();
                        RemoveFromGCList(table.GetReference());
                        return table;
                    }
                }

                funcMap.Remove(fullPath);
            }

            if (PushLuaTable(fullPath, false))
            {
                int reference = ToLuaRef();
                LuaTable table = null;

                if (funcRefMap.TryGetValue(reference, out weak))
                {
                    if (weak.IsAlive)
                    {
                        table = weak.Target as LuaTable;
                        CheckNull(table, "{0} not a lua table", fullPath);

                        if (table.IsAlive)
                        {
                            funcMap.Add(fullPath, weak);
                            table.AddRef();
                            RemoveFromGCList(reference);
                            return table;
                        }
                    }

                    funcRefMap.Remove(reference);
                }

                table = new LuaTable(reference, this);
                table.name = fullPath;
                funcMap.Add(fullPath, new WeakReference(table));
                funcRefMap.Add(reference, new WeakReference(table));
                if (LogGC) Debugger.Log("Alloc LuaTable name {0}, id {1}", fullPath, reference);     
                RemoveFromGCList(reference);
                return table;
            }

            if (beLogMiss)
            {
                Debugger.LogWarning("Lua table {0} not exists", fullPath);
            }

            return null;
        }

        public LuaTable GetTable(int reference)
        {
            LuaTable table = TryGetLuaRef(reference) as LuaTable;

            if (table == null)
            {                
                table = new LuaTable(reference, this);
                funcRefMap.Add(reference, new WeakReference(table));
            }

            RemoveFromGCList(reference);
            return table;
        }

        public LuaThread GetLuaThread(int reference)
        {
            LuaThread thread = TryGetLuaRef(reference) as LuaThread;

            if (thread == null)
            {                
                thread = new LuaThread(reference, this);
                funcRefMap.Add(reference, new WeakReference(thread));
            }

            RemoveFromGCList(reference);
            return thread;
        }

        public LuaDelegate GetLuaDelegate(LuaFunction func)
        {
            WeakReference weak = null;
            int reference = func.GetReference();            
            delegateMap.TryGetValue(reference, out weak);

            if (weak != null)
            {
                if (weak.IsAlive)
                {
                    return weak.Target as LuaDelegate;
                }

                delegateMap.Remove(reference);
            }

            return null;
        }

        public LuaDelegate GetLuaDelegate(LuaFunction func, LuaTable self)
        {
            WeakReference weak = null;
            long high = func.GetReference();
            long low = self == null ? 0 : self.GetReference();
            low = low >= 0 ? low : 0;
            long key = high << 32 | low;            
            delegateMap.TryGetValue(key, out weak);

            if (weak != null)
            {
                if (weak.IsAlive)
                {
                    return weak.Target as LuaDelegate;
                }

                delegateMap.Remove(key);
            }

            return null;
        }

        public void AddLuaDelegate(LuaDelegate target, LuaFunction func)
        {            
            int key = func.GetReference();

            if (key > 0)
            {
                delegateMap[key] = new WeakReference(target);
            }
        }

        public void AddLuaDelegate(LuaDelegate target, LuaFunction func, LuaTable self)
        {
            long high = func.GetReference();
            long low = self == null ? 0 : self.GetReference();
            low = low >= 0 ? low : 0;
            long key = high << 32 | low;

            if (key > 0)
            {
                delegateMap[key] = new WeakReference(target);
            }
        }

        public bool CheckTop()
        {
            int n = LuaGetTop();

            if (n != 0)
            {
                Debugger.LogWarning("Lua stack top is {0}", n);
                return false;
            }

            return true;
        }

        public void Push(bool b)
        {
            LuaDLL.lua_pushboolean(L, b);
        }

        public void Push(double d)
        {
            LuaDLL.lua_pushnumber(L, d);
        }

        public void Push(uint un)
        {
            LuaDLL.lua_pushnumber(L, un);
        }

        public void Push(int n)
        {
            LuaDLL.lua_pushinteger(L, n);
        }

        public void Push(short s)
        {
            LuaDLL.lua_pushnumber(L, s);
        }

        public void Push(ushort us)
        {
            LuaDLL.lua_pushnumber(L, us);
        }

        public void Push(long l)
        {
            LuaDLL.tolua_pushint64(L, l);
        }

        public void Push(ulong ul)
        {
            LuaDLL.tolua_pushuint64(L, ul);
        }

        public void Push(string str)
        {
            LuaDLL.lua_pushstring(L, str);
        }

        public void Push(IntPtr p)
        {
            LuaDLL.lua_pushlightuserdata(L, p);
        }

        public void Push(Vector3 v3)
        {            
            LuaDLL.tolua_pushvec3(L, v3.x, v3.y, v3.z);
        }

        public void Push(Vector2 v2)
        {
            LuaDLL.tolua_pushvec2(L, v2.x, v2.y);
        }

        public void Push(Vector4 v4)
        {
            LuaDLL.tolua_pushvec4(L, v4.x, v4.y, v4.z, v4.w);
        }

        public void Push(Color clr)
        {
            LuaDLL.tolua_pushclr(L, clr.r, clr.g, clr.b, clr.a);
        }

        public void Push(Quaternion q)
        {
            LuaDLL.tolua_pushquat(L, q.x, q.y, q.z, q.w);
        }          

        public void Push(Ray ray)
        {
            ToLua.Push(L, ray);
        }

        public void Push(Bounds bound)
        {
            ToLua.Push(L, bound);
        }

        public void Push(RaycastHit hit)
        {
            ToLua.Push(L, hit);
        }

        public void Push(Touch touch)
        {
            ToLua.Push(L, touch);
        }

        public void PushLayerMask(LayerMask mask)
        {
            LuaDLL.tolua_pushlayermask(L, mask.value);
        }

        public void Push(LuaByteBuffer bb)
        {
            LuaDLL.lua_pushlstring(L, bb.buffer, bb.Length);
        }

        public void PushByteBuffer(byte[] buffer)
        {
            LuaDLL.lua_pushlstring(L, buffer, buffer.Length);
        }

        public void PushByteBuffer(byte[] buffer, int len)
        {
            LuaDLL.lua_pushlstring(L, buffer, len);
        }

        public void Push(LuaBaseRef lbr)
        {
            if (lbr == null)
            {                
                LuaPushNil();
            }
            else
            {
                LuaGetRef(lbr.GetReference());
            }
        }

        void PushUserData(object o, int reference)
        {
            int index;

            if (translator.Getudata(o, out index))
            {
                if (LuaDLL.tolua_pushudata(L, index))
                {
                    return;
                }

                translator.Destroyudata(index);
            }

            index = translator.AddObject(o);
            LuaDLL.tolua_pushnewudata(L, reference, index);
        }

        public void Push(Array array)
        {
            if (array == null)
            {                
                LuaPushNil();
            }
            else
            {
                PushUserData(array, ArrayMetatable);
            }
        }

        public void Push(Type t)
        {
            if (t == null)
            {
                LuaPushNil();
            }
            else
            {
                PushUserData(t, TypeMetatable);
            }
        }

        public void Push(Delegate ev)
        {
            if (ev == null)
            {                
                LuaPushNil();
            }
            else
            {
                PushUserData(ev, DelegateMetatable);
            }
        }

        public object GetEnumObj(Enum e)
        {
            object o = null;

            if (!enumMap.TryGetValue(e, out o))
            {
                o = e;
                enumMap.Add(e, o);
            }

            return o;
        }

        public void Push(Enum e)
        {
            if (e == null)
            {                
                LuaPushNil();
            }
            else
            {
                object o = GetEnumObj(e);
                PushUserData(o, EnumMetatable);
            }
        }

        public void Push(IEnumerator iter)
        {
            ToLua.Push(L, iter);
        }

        public void Push(UnityEngine.Object obj)
        {
            ToLua.Push(L, obj);
        }

        public void Push(UnityEngine.TrackedReference tracker)
        {
            ToLua.Push(L, tracker);
        }

        public void PushVariant(object obj)
        {
            ToLua.Push(L, obj);
        }

        public void PushObject(object obj)
        {
            ToLua.PushObject(L, obj);
        }

        public void PushSealed<T>(T o)
        {
            ToLua.PushSealed<T>(L, o);
        }

        public void PushValue<T>(T v) where T : struct
        {
            StackTraits<T>.Push(L, v);
        }

        public void PushGeneric<T>(T o)
        {
            StackTraits<T>.Push(L, o);
        }

        Vector3 ToVector3(int stackPos)
        {            
            float x, y, z;
            stackPos = LuaDLL.abs_index(L, stackPos);
            LuaDLL.tolua_getvec3(L, stackPos, out x, out y, out z);
            return new Vector3(x, y, z);
        }

        public Vector3 CheckVector3(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.Vector3)
            {
                LuaTypeError(stackPos, "Vector3",  LuaValueTypeName.Get(type));
                return Vector3.zero;
            }
            
            float x, y, z;
            stackPos = LuaDLL.abs_index(L, stackPos);
            LuaDLL.tolua_getvec3(L, stackPos, out x, out y, out z);
            return new Vector3(x, y, z);
        }

        public Quaternion CheckQuaternion(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.Quaternion)
            {
                LuaTypeError(stackPos, "Quaternion", LuaValueTypeName.Get(type));
                return Quaternion.identity;
            }

            float x, y, z, w;
            stackPos = LuaDLL.abs_index(L, stackPos);
            LuaDLL.tolua_getquat(L, stackPos, out x, out y, out z, out w);
            return new Quaternion(x, y, z, w);
        }

        public Vector2 CheckVector2(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.Vector2)
            {
                LuaTypeError(stackPos, "Vector2", LuaValueTypeName.Get(type));                
                return Vector2.zero;
            }

            float x, y;
            stackPos = LuaDLL.abs_index(L, stackPos);
            LuaDLL.tolua_getvec2(L, stackPos, out x, out y);
            return new Vector2(x, y);
        }

        public Vector4 CheckVector4(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.Vector4)
            {
                LuaTypeError(stackPos, "Vector4", LuaValueTypeName.Get(type));
                return Vector4.zero;
            }

            float x, y, z, w;
            stackPos = LuaDLL.abs_index(L, stackPos);
            LuaDLL.tolua_getvec4(L, stackPos, out x, out y, out z, out w);
            return new Vector4(x, y, z, w);
        }

        public Color CheckColor(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.Color)
            {
                LuaTypeError(stackPos, "Color", LuaValueTypeName.Get(type));    
                return Color.black;
            }

            float r, g, b, a;
            stackPos = LuaDLL.abs_index(L, stackPos);
            LuaDLL.tolua_getclr(L, stackPos, out r, out g, out b, out a);
            return new Color(r, g, b, a);
        }

        public Ray CheckRay(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.Ray)
            {
                LuaTypeError(stackPos, "Ray", LuaValueTypeName.Get(type));
                return new Ray();
            }

            stackPos = LuaDLL.abs_index(L, stackPos);
            int oldTop = BeginPCall(UnpackRay);
            LuaPushValue(stackPos);

            try
            {
                PCall(1, oldTop);
                Vector3 origin = ToVector3(oldTop + 1);
                Vector3 dir = ToVector3(oldTop + 2);
                EndPCall(oldTop);                
                return new Ray(origin, dir);
            }
            catch(Exception e)
            {
                EndPCall(oldTop);
                throw e;
            }
        }

        public Bounds CheckBounds(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.Bounds)
            {
                LuaTypeError(stackPos, "Bounds", LuaValueTypeName.Get(type));    
                return new Bounds();
            }

            stackPos = LuaDLL.abs_index(L, stackPos);
            int oldTop = BeginPCall(UnpackBounds);
            LuaPushValue(stackPos);

            try
            {
                PCall(1, oldTop);
                Vector3 center = ToVector3(oldTop + 1);
                Vector3 size = ToVector3(oldTop + 2);
                EndPCall(oldTop);
                return new Bounds(center, size);
            }
            catch(Exception e)
            {
                EndPCall(oldTop);
                throw e;
            }
        }

        public LayerMask CheckLayerMask(int stackPos)
        {            
            int type = LuaDLL.tolua_getvaluetype(L, stackPos);

            if (type != LuaValueType.LayerMask)
            {
                LuaTypeError(stackPos, "LayerMask", LuaValueTypeName.Get(type));
                return 0;
            }

            stackPos = LuaDLL.abs_index(L, stackPos);
            return LuaDLL.tolua_getlayermask(L, stackPos);
        }

        public long CheckLong(int stackPos)
        {
            stackPos = LuaDLL.abs_index(L, stackPos);
            return LuaDLL.tolua_checkint64(L, stackPos);
        }

        public ulong CheckULong(int stackPos)
        {
            stackPos = LuaDLL.abs_index(L, stackPos);
            return LuaDLL.tolua_checkuint64(L, stackPos);
        }

        public string CheckString(int stackPos)
        {
            return ToLua.CheckString(L, stackPos);
        }

        public Delegate CheckDelegate(int stackPos)
        {            
            int udata = LuaDLL.tolua_rawnetobj(L, stackPos);

            if (udata != -1)
            {
                object obj = translator.GetObject(udata);

                if (obj != null)
                {                                                  
                    if (obj is Delegate)
                    {
                        return (Delegate)obj;
                    }

                    LuaTypeError(stackPos, "Delegate", obj.GetType().FullName);
                }

                return null;
            }
            else if (LuaDLL.lua_isnil(L,stackPos))
            {
                return null;
            }

            LuaTypeError(stackPos, "Delegate");
            return null;
        }

        public char[] CheckCharBuffer(int stackPos)
        {
            return ToLua.CheckCharBuffer(L, stackPos);
        }

        public byte[] CheckByteBuffer(int stackPos)
        {
            return ToLua.CheckByteBuffer(L, stackPos);
        }

        public T[] CheckNumberArray<T>(int stackPos) where T : struct
        {
            return ToLua.CheckNumberArray<T>(L, stackPos);
        }

        public object CheckObject(int stackPos, Type type)
        {
            return ToLua.CheckObject(L, stackPos, type);
        }

        public object CheckVarObject(int stackPos, Type type)
        {
            return ToLua.CheckVarObject(L, stackPos, type);
        }

        public object[] CheckObjects(int oldTop)
        {
            int newTop = LuaGetTop();

            if (oldTop == newTop)
            {
                return null;
            }
            else
            {
                List<object> returnValues = new List<object>();

                for (int i = oldTop + 1; i <= newTop; i++)
                {
                    returnValues.Add(ToVariant(i));
                }

                return returnValues.ToArray();
            }
        }

        public LuaFunction CheckLuaFunction(int stackPos)
        {            
            return ToLua.CheckLuaFunction(L, stackPos);
        }

        public LuaTable CheckLuaTable(int stackPos)
        {            
            return ToLua.CheckLuaTable(L, stackPos);
        }

        public LuaThread CheckLuaThread(int stackPos)
        {            
            return ToLua.CheckLuaThread(L, stackPos);
        }

        //从堆栈读取一个值类型
        public T CheckValue<T>(int stackPos)
        {            
            return StackTraits<T>.Check(L, stackPos);
        }

        public object ToVariant(int stackPos)
        {
            return ToLua.ToVarObject(L, stackPos);
        }

        public void CollectRef(int reference, string name, bool isGCThread = false)
        {
            if (!isGCThread)
            {                
                Collect(reference, name, false);
            }
            else
            {
                lock (gcList)
                {
                    gcList.Add(new GCRef(reference, name));
                }
            }
        }

        //在委托调用中减掉一个LuaFunction, 此lua函数在委托中还会执行一次, 所以必须延迟删除，委托值类型表现之一
        public void DelayDispose(LuaBaseRef br)
        {
            if (br != null)
            {
                subList.Add(br);
            }
        }

        public int Collect()
        {
            int count = gcList.Count;

            if (count > 0)
            {
                lock (gcList)
                {
                    for (int i = 0; i < gcList.Count; i++)
                    {
                        int reference = gcList[i].reference;
                        string name = gcList[i].name;
                        Collect(reference, name, true);
                    }

                    gcList.Clear();
                    return count;
                }
            }

            for (int i = 0; i < subList.Count; i++)
            {
                subList[i].Dispose();
            }

            subList.Clear();
            translator.Collect();
            return 0;
        }

        public void StepCollect()
        {
            translator.StepCollect();
        }

        public void RefreshDelegateMap()
        {
            List<long> list = new List<long>();
            var iter = delegateMap.GetEnumerator();

            while (iter.MoveNext())
            {
                if (!iter.Current.Value.IsAlive)
                {
                    list.Add(iter.Current.Key);
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                delegateMap.Remove(list[i]);
            }
        }

        public object this[string fullPath]
        {
            get
            {
                int oldTop = LuaGetTop();
                int pos = fullPath.LastIndexOf('.');
                object obj = null;

                if (pos > 0)
                {
                    string tableName = fullPath.Substring(0, pos);

                    if (PushLuaTable(tableName))
                    {
                        string name = fullPath.Substring(pos + 1);
                        LuaPushString(name);
                        LuaRawGet(-2);
                        obj = ToVariant(-1);
                    }    
                    else
                    {
                        LuaSetTop(oldTop);
                        return null;
                    }
                }
                else
                {
                    LuaGetGlobal(fullPath);
                    obj = ToVariant(-1);
                }

                LuaSetTop(oldTop);
                return obj;
            }

            set
            {
                int oldTop = LuaGetTop();
                int pos = fullPath.LastIndexOf('.');

                if (pos > 0)
                {
                    string tableName = fullPath.Substring(0, pos);
                    IntPtr p = LuaFindTable(LuaIndexes.LUA_GLOBALSINDEX, tableName);

                    if (p == IntPtr.Zero)
                    {
                        string name = fullPath.Substring(pos + 1);
                        LuaPushString(name);
                        PushVariant(value);
                        LuaSetTable(-3);
                    }
                    else
                    {
                        LuaSetTop(oldTop);
                        int len = LuaDLL.tolua_strlen(p);
                        string str = LuaDLL.lua_ptrtostring(p, len);
                        throw new LuaException(string.Format("{0} not a Lua table", str));
                    }
                }
                else
                {
                    PushVariant(value);
                    LuaSetGlobal(fullPath);                    
                }

                LuaSetTop(oldTop);
            }
        }

        public void NewTable(string fullPath)
        {
            string[] path = fullPath.Split(new char[] { '.' });
            int oldTop = LuaDLL.lua_gettop(L);

            if (path.Length == 1)
            {
                LuaDLL.lua_newtable(L);
                LuaDLL.lua_setglobal(L, fullPath);
            }
            else
            {
                LuaDLL.lua_getglobal(L, path[0]);

                for (int i = 1; i < path.Length - 1; i++)
                {
                    LuaDLL.lua_pushstring(L, path[i]);
                    LuaDLL.lua_gettable(L, -2);
                }

                LuaDLL.lua_pushstring(L, path[path.Length - 1]);
                LuaDLL.lua_newtable(L);
                LuaDLL.lua_settable(L, -3);
            }

            LuaDLL.lua_settop(L, oldTop);
        }


        public LuaTable NewTable(int narr = 0, int nrec = 0)
        {
            int oldTop = LuaDLL.lua_gettop(L);

            LuaDLL.lua_createtable(L, 0, 0);
            LuaTable table = ToLua.ToLuaTable(L, oldTop + 1);

            LuaDLL.lua_settop(L, oldTop);
            return table;
        }

        //慎用
        public void ReLoad(string moduleFileName)
        {
            LuaGetGlobal("package");
            LuaGetField(-1, "loaded");
            LuaPushString(moduleFileName);
            LuaGetTable(-2);                          

            if (!LuaIsNil(-1))
            {
                LuaPushString(moduleFileName);                        
                LuaPushNil();
                LuaSetTable(-4);                      
            }

            LuaPop(3);
            string require = string.Format("require '{0}'", moduleFileName);
            DoString(require, "ReLoad");
        }

        public int GetMetaReference(Type t)
        {
            int reference = -1;
            metaMap.TryGetValue(t, out reference);
            return reference;
        }

        public int GetMissMetaReference(Type t)
        {       
            int reference = -1;
            Type type = GetBaseType(t);

            while (type != null)
            {
                if (metaMap.TryGetValue(type, out reference))
                {
#if MISS_WARNING
                    if (!missSet.Contains(t))
                    {
                        missSet.Add(t);
                        Debugger.LogWarning("Type {0} not wrap to lua, push as {1}, the warning is only raised once", LuaMisc.GetTypeName(t), LuaMisc.GetTypeName(type));
                    }
#endif                    
                    return reference;              
                }

                type = GetBaseType(type);
            }            

            if (reference <= 0)
            {
                type = typeof(object);
                reference = LuaStatic.GetMetaReference(L, type);                
            }

#if MISS_WARNING
            if (!missSet.Contains(t))
            {
                missSet.Add(t);
                Debugger.LogWarning("Type {0} not wrap to lua, push as {1}, the warning is only raised once", LuaMisc.GetTypeName(t), LuaMisc.GetTypeName(type));
            }            
#endif

            return reference;
        }

        Type GetBaseType(Type t)
        {
            if (t.IsGenericType)
            {
                return GetSpecialGenericType(t);
            }

            return LuaMisc.GetExportBaseType(t);
        }

        Type GetSpecialGenericType(Type t)
        {
            Type generic = t.GetGenericTypeDefinition();

            if (genericSet.Contains(generic))
            {
                return t == generic ? t.BaseType : generic;
            }

            return t.BaseType;
        }

        void CloseBaseRef()
        {
            LuaUnRef(PackBounds);
            LuaUnRef(UnpackBounds);
            LuaUnRef(PackRay);
            LuaUnRef(UnpackRay);
            LuaUnRef(PackRaycastHit);
            LuaUnRef(PackTouch);   
        }
        
        public void Dispose()
        {
            if (IntPtr.Zero != L)
            {
                Collect();

                foreach (KeyValuePair<Type, int> kv in metaMap)
                {
                    LuaUnRef(kv.Value);
                }

                List<LuaBaseRef> list = new List<LuaBaseRef>();

                foreach (KeyValuePair<int, WeakReference> kv in funcRefMap)
                {
                    if (kv.Value.IsAlive)
                    {
                        list.Add((LuaBaseRef)kv.Value.Target);
                    }
                }

                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Dispose(true);
                }

                CloseBaseRef();
                delegateMap.Clear();
                funcRefMap.Clear();
                funcMap.Clear();
                metaMap.Clear();                
                typeMap.Clear();
                enumMap.Clear();
                preLoadMap.Clear();
                genericSet.Clear();                                
                LuaDLL.lua_close(L);                
                translator.Dispose();
                stateMap.Remove(L);
                translator = null;
                L = IntPtr.Zero;
#if MISS_WARNING
                missSet.Clear();
#endif
                OnDestroy();
                Debugger.Log("LuaState destroy");
            }

            if (mainState == this)
            {
                mainState = null;
            }
            if (injectionState == this)
            {
                injectionState = null;
                LuaInjectionStation.Clear();
            }

#if UNITY_EDITOR
            beStart = false;
#endif

            LuaFileUtils.Instance.Dispose();
            System.GC.SuppressFinalize(this);            
        }

        //public virtual void Dispose(bool dispose)
        //{
        //}

        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(this);          
        }

        public override bool Equals(object o)
        {
            if (o == null) return L == IntPtr.Zero;
            LuaState state = o as LuaState;

            if (state == null || state.L != L)
            {
                return false;
            }

            return L != IntPtr.Zero;
        }

        public static bool operator == (LuaState a, LuaState b)
        {
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }

            object l = a;
            object r = b;

            if (l == null && r != null)
            {
                return b.L == IntPtr.Zero;
            }

            if (l != null && r == null)
            {
                return a.L == IntPtr.Zero;
            }

            if (a.L != b.L)
            {
                return false;
            }

            return a.L != IntPtr.Zero;
        }

        public static bool operator != (LuaState a, LuaState b)
        {
            return !(a == b);
        }

        public void PrintTable(string name)
        {
            LuaTable table = GetTable(name);
            LuaDictTable dict = table.ToDictTable();
            table.Dispose();
            var iter2 = dict.GetEnumerator();

            while (iter2.MoveNext())
            {
                Debugger.Log("map item, k,v is {0}:{1}", iter2.Current.Key, iter2.Current.Value);
            }

            iter2.Dispose();
            dict.Dispose();
        }

        protected void Collect(int reference, string name, bool beThread)
        {
            if (beThread)
            {
                WeakReference weak = null;

                if (name != null)
                {
                    funcMap.TryGetValue(name, out weak);

                    if (weak != null && !weak.IsAlive)
                    {
                        funcMap.Remove(name);
                        weak = null;
                    }
                }
                
                funcRefMap.TryGetValue(reference, out weak);

                if (weak != null && !weak.IsAlive)
                {
                    ToLuaUnRef(reference);
                    funcRefMap.Remove(reference);
                    delegateMap.Remove(reference);

                    if (LogGC)
                    {
                        string str = name == null ? "null" : name;
                        Debugger.Log("collect lua reference name {0}, id {1} in thread", str, reference);
                    }
                }
            }
            else
            {
                if (name != null)
                {
                    WeakReference weak = null;
                    funcMap.TryGetValue(name, out weak);
                    
                    if (weak != null && weak.IsAlive)
                    {
                        LuaBaseRef lbr = (LuaBaseRef)weak.Target;

                        if (reference == lbr.GetReference())
                        {
                            funcMap.Remove(name);
                        }
                    }
                }

                ToLuaUnRef(reference);
                funcRefMap.Remove(reference);
                delegateMap.Remove(reference);

                if (LogGC)
                {
                    string str = name == null ? "null" : name;
                    Debugger.Log("collect lua reference name {0}, id {1} in main", str, reference);
                }
            }
        }

        protected void LuaLoadBuffer(byte[] buffer, string chunkName)
        {
            LuaDLL.tolua_pushtraceback(L);
            int oldTop = LuaGetTop();

            if (LuaLoadBuffer(buffer, buffer.Length, chunkName) == 0)
            {
                if (LuaPCall(0, LuaDLL.LUA_MULTRET, oldTop) == 0)
                {                    
                    LuaSetTop(oldTop - 1);
                    return;
                }
            }

            string err = LuaToString(-1);
            LuaSetTop(oldTop - 1);                        
            throw new LuaException(err, LuaException.GetLastError());
        }

        protected T LuaLoadBuffer<T>(byte[] buffer, string chunkName)
        {
            LuaDLL.tolua_pushtraceback(L);
            int oldTop = LuaGetTop();

            if (LuaLoadBuffer(buffer, buffer.Length, chunkName) == 0)
            {
                if (LuaPCall(0, LuaDLL.LUA_MULTRET, oldTop) == 0)
                {
                    T result = CheckValue<T>(oldTop + 1);
                    LuaSetTop(oldTop - 1);
                    return result;
                }
            }

            string err = LuaToString(-1);
            LuaSetTop(oldTop - 1);
            throw new LuaException(err, LuaException.GetLastError());
        }

        public bool BeginCall(string name, int top, bool beLogMiss)
        {            
            LuaDLL.tolua_pushtraceback(L);

            if (PushLuaFunction(name, false))
            {
                return true;
            }
            else
            {
                LuaDLL.lua_settop(L, top);
                if (beLogMiss)
                {
                    Debugger.Log("Lua function {0} not exists", name);
                }
                
                return false;
            }
        }

        public void Call(int nArgs, int errfunc, int top)
        {
            if (LuaDLL.lua_pcall(L, nArgs, LuaDLL.LUA_MULTRET, errfunc) != 0)
            {
                string error = LuaDLL.lua_tostring(L, -1);                
                throw new LuaException(error, LuaException.GetLastError());
            }
        }

        public void Call(string name, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {                
                if (BeginCall(name, top, beLogMiss))
                {
                    Call(0, top + 1, top);
                    LuaDLL.lua_settop(L, top);
                }
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public void Call<T>(string name, T arg1, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    Call(1, top + 1, top);                    
                    LuaDLL.lua_settop(L, top);
                }
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public void Call<T1, T2>(string name, T1 arg1, T2 arg2, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    Call(2, top + 1, top);
                    LuaDLL.lua_settop(L, top);
                }
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public void Call<T1, T2, T3>(string name, T1 arg1, T2 arg2, T3 arg3, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    Call(3, top + 1, top);
                    LuaDLL.lua_settop(L, top);
                }
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public void Call<T1, T2, T3, T4>(string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    PushGeneric(arg4);
                    Call(4, top + 1, top);
                    LuaDLL.lua_settop(L, top);
                }
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public void Call<T1, T2, T3, T4, T5>(string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    PushGeneric(arg4);
                    PushGeneric(arg5);
                    Call(5, top + 1, top);
                    LuaDLL.lua_settop(L, top);
                }
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public void Call<T1, T2, T3, T4, T5, T6>(string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    PushGeneric(arg4);
                    PushGeneric(arg5);
                    PushGeneric(arg6);
                    Call(6, top + 1, top);
                    LuaDLL.lua_settop(L, top);
                }
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public R1 Invoke<R1>(string name, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    Call(0, top + 1, top);
                    R1 ret1 = CheckValue<R1>(top + 2);
                    LuaDLL.lua_settop(L, top);
                    return ret1;
                }

                return default(R1);
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public R1 Invoke<T1, R1>(string name, T1 arg1, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    Call(1, top + 1, top);
                    R1 ret1 = CheckValue<R1>(top + 2);
                    LuaDLL.lua_settop(L, top);
                    return ret1;
                }

                return default(R1);
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public R1 Invoke<T1, T2, R1>(string name, T1 arg1, T2 arg2, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    Call(2, top + 1, top);
                    R1 ret1 = CheckValue<R1>(top + 2);
                    LuaDLL.lua_settop(L, top);
                    return ret1;
                }

                return default(R1);
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public R1 Invoke<T1, T2, T3, R1>(string name, T1 arg1, T2 arg2, T3 arg3, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    Call(3, top + 1, top);
                    R1 ret1 = CheckValue<R1>(top + 2);
                    LuaDLL.lua_settop(L, top);
                    return ret1;
                }

                return default(R1);
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public R1 Invoke<T1, T2, T3, T4, R1>(string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    PushGeneric(arg4);
                    Call(4, top + 1, top);
                    R1 ret1 = CheckValue<R1>(top + 2);
                    LuaDLL.lua_settop(L, top);
                    return ret1;
                }

                return default(R1);
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public R1 Invoke<T1, T2, T3, T4, T5, R1>(string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    PushGeneric(arg4);
                    PushGeneric(arg5);
                    Call(5, top + 1, top);
                    R1 ret1 = CheckValue<R1>(top + 2);
                    LuaDLL.lua_settop(L, top);
                    return ret1;
                }

                return default(R1);
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        public R1 Invoke<T1, T2, T3, T4, T5, T6, R1>(string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, bool beLogMiss)
        {
            int top = LuaDLL.lua_gettop(L);

            try
            {
                if (BeginCall(name, top, beLogMiss))
                {
                    PushGeneric(arg1);
                    PushGeneric(arg2);
                    PushGeneric(arg3);
                    PushGeneric(arg4);
                    PushGeneric(arg5);
                    PushGeneric(arg6);
                    Call(6, top + 1, top);
                    R1 ret1 = CheckValue<R1>(top + 2);
                    LuaDLL.lua_settop(L, top);
                    return ret1;
                }

                return default(R1);
            }
            catch (Exception e)
            {
                LuaDLL.lua_settop(L, top);
                throw e;
            }
        }

        void InitTypeTraits()
        {
            LuaMatchType _ck = new LuaMatchType();
            TypeTraits<sbyte>.Init(_ck.CheckNumber);
            TypeTraits<byte>.Init(_ck.CheckNumber);
            TypeTraits<short>.Init(_ck.CheckNumber);
            TypeTraits<ushort>.Init(_ck.CheckNumber);
            TypeTraits<char>.Init(_ck.CheckNumber);
            TypeTraits<int>.Init(_ck.CheckNumber);
            TypeTraits<uint>.Init(_ck.CheckNumber);
            TypeTraits<decimal>.Init(_ck.CheckNumber);
            TypeTraits<float>.Init(_ck.CheckNumber);
            TypeTraits<double>.Init(_ck.CheckNumber);
            TypeTraits<bool>.Init(_ck.CheckBool);
            TypeTraits<long>.Init(_ck.CheckLong);
            TypeTraits<ulong>.Init(_ck.CheckULong);
            TypeTraits<string>.Init(_ck.CheckString);

            TypeTraits<Nullable<sbyte>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<byte>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<short>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<ushort>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<char>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<int>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<uint>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<decimal>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<float>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<double>>.Init(_ck.CheckNullNumber);
            TypeTraits<Nullable<bool>>.Init(_ck.CheckNullBool);
            TypeTraits<Nullable<long>>.Init(_ck.CheckNullLong);
            TypeTraits<Nullable<ulong>>.Init(_ck.CheckNullULong);

            TypeTraits<byte[]>.Init(_ck.CheckByteArray);            
            TypeTraits<char[]>.Init(_ck.CheckCharArray);            
            TypeTraits<bool[]>.Init(_ck.CheckBoolArray);            
            TypeTraits<sbyte[]>.Init(_ck.CheckSByteArray);
            TypeTraits<short[]>.Init(_ck.CheckInt16Array);
            TypeTraits<ushort[]>.Init(_ck.CheckUInt16Array);
            TypeTraits<decimal[]>.Init(_ck.CheckDecimalArray);
            TypeTraits<float[]>.Init(_ck.CheckSingleArray);
            TypeTraits<double[]>.Init(_ck.CheckDoubleArray);
            TypeTraits<int[]>.Init(_ck.CheckInt32Array);
            TypeTraits<uint[]>.Init(_ck.CheckUInt32Array);
            TypeTraits<long[]>.Init(_ck.CheckInt64Array);
            TypeTraits<ulong[]>.Init(_ck.CheckUInt64Array);
            TypeTraits<string[]>.Init(_ck.CheckStringArray);

            TypeTraits<Vector3>.Init(_ck.CheckVec3);
            TypeTraits<Quaternion>.Init(_ck.CheckQuat);
            TypeTraits<Vector2>.Init(_ck.CheckVec2);
            TypeTraits<Color>.Init(_ck.CheckColor);
            TypeTraits<Vector4>.Init(_ck.CheckVec4);
            TypeTraits<Ray>.Init(_ck.CheckRay);
            TypeTraits<Bounds>.Init(_ck.CheckBounds);
            TypeTraits<Touch>.Init(_ck.CheckTouch);
            TypeTraits<LayerMask>.Init(_ck.CheckLayerMask);
            TypeTraits<RaycastHit>.Init(_ck.CheckRaycastHit);

            TypeTraits<Nullable<Vector3>>.Init(_ck.CheckNullVec3);
            TypeTraits<Nullable<Quaternion>>.Init(_ck.CheckNullQuat);
            TypeTraits<Nullable<Vector2>>.Init(_ck.CheckNullVec2);
            TypeTraits<Nullable<Color>>.Init(_ck.CheckNullColor);
            TypeTraits<Nullable<Vector4>>.Init(_ck.CheckNullVec4);
            TypeTraits<Nullable<Ray>>.Init(_ck.CheckNullRay);
            TypeTraits<Nullable<Bounds>>.Init(_ck.CheckNullBounds);
            TypeTraits<Nullable<Touch>>.Init(_ck.CheckNullTouch);
            TypeTraits<Nullable<LayerMask>>.Init(_ck.CheckNullLayerMask);
            TypeTraits<Nullable<RaycastHit>>.Init(_ck.CheckNullRaycastHit);

            TypeTraits<Vector3[]>.Init(_ck.CheckVec3Array);
            TypeTraits<Quaternion[]>.Init(_ck.CheckQuatArray);
            TypeTraits<Vector2[]>.Init(_ck.CheckVec2Array);
            TypeTraits<Color[]>.Init(_ck.CheckColorArray);
            TypeTraits<Vector4[]>.Init(_ck.CheckVec4Array);

            TypeTraits<IntPtr>.Init(_ck.CheckPtr);
            TypeTraits<UIntPtr>.Init(_ck.CheckPtr);
            TypeTraits<LuaFunction>.Init(_ck.CheckLuaFunc);
            TypeTraits<LuaTable>.Init(_ck.CheckLuaTable);
            TypeTraits<LuaThread>.Init(_ck.CheckLuaThread);
            TypeTraits<LuaBaseRef>.Init(_ck.CheckLuaBaseRef);

            TypeTraits<LuaByteBuffer>.Init(_ck.CheckByteBuffer);
            TypeTraits<EventObject>.Init(_ck.CheckEventObject);
            TypeTraits<IEnumerator>.Init(_ck.CheckEnumerator);
            TypeTraits<Type>.Init(_ck.CheckMonoType);
            TypeTraits<GameObject>.Init(_ck.CheckGameObject);
            TypeTraits<Transform>.Init(_ck.CheckTransform);
            TypeTraits<Type[]>.Init(_ck.CheckTypeArray);
            TypeTraits<object>.Init(_ck.CheckVariant);
            TypeTraits<object[]>.Init(_ck.CheckObjectArray);
        }

        void InitStackTraits()
        {
            LuaStackOp op = new LuaStackOp();
            StackTraits<sbyte>.Init(op.Push, op.CheckSByte, op.ToSByte);
            StackTraits<byte>.Init(op.Push, op.CheckByte, op.ToByte);
            StackTraits<short>.Init(op.Push, op.CheckInt16, op.ToInt16);
            StackTraits<ushort>.Init(op.Push, op.CheckUInt16, op.ToUInt16);
            StackTraits<char>.Init(op.Push, op.CheckChar, op.ToChar);
            StackTraits<int>.Init(op.Push, op.CheckInt32, op.ToInt32);
            StackTraits<uint>.Init(op.Push, op.CheckUInt32, op.ToUInt32);
            StackTraits<decimal>.Init(op.Push, op.CheckDecimal, op.ToDecimal);
            StackTraits<float>.Init(op.Push, op.CheckFloat, op.ToFloat);
            StackTraits<double>.Init(LuaDLL.lua_pushnumber, LuaDLL.luaL_checknumber, LuaDLL.lua_tonumber);
            StackTraits<bool>.Init(LuaDLL.lua_pushboolean, LuaDLL.luaL_checkboolean, LuaDLL.lua_toboolean);
            StackTraits<long>.Init(LuaDLL.tolua_pushint64, LuaDLL.tolua_checkint64, LuaDLL.tolua_toint64);
            StackTraits<ulong>.Init(LuaDLL.tolua_pushuint64, LuaDLL.tolua_checkuint64, LuaDLL.tolua_touint64);
            StackTraits<string>.Init(LuaDLL.lua_pushstring, ToLua.CheckString, ToLua.ToString);

            StackTraits<Nullable<sbyte>>.Init(op.Push, op.CheckNullSByte, op.ToNullSByte);
            StackTraits<Nullable<byte>>.Init(op.Push, op.CheckNullByte, op.ToNullByte);
            StackTraits<Nullable<short>>.Init(op.Push, op.CheckNullInt16, op.ToNullInt16);
            StackTraits<Nullable<ushort>>.Init(op.Push, op.CheckNullUInt16, op.ToNullUInt16);
            StackTraits<Nullable<char>>.Init(op.Push, op.CheckNullChar, op.ToNullChar);
            StackTraits<Nullable<int>>.Init(op.Push, op.CheckNullInt32, op.ToNullInt32);
            StackTraits<Nullable<uint>>.Init(op.Push, op.CheckNullUInt32, op.ToNullUInt32);
            StackTraits<Nullable<decimal>>.Init(op.Push, op.CheckNullDecimal, op.ToNullDecimal);
            StackTraits<Nullable<float>>.Init(op.Push, op.CheckNullFloat, op.ToNullFloat);
            StackTraits<Nullable<double>>.Init(op.Push, op.CheckNullNumber, op.ToNullNumber);
            StackTraits<Nullable<bool>>.Init(op.Push, op.CheckNullBool, op.ToNullBool);
            StackTraits<Nullable<long>>.Init(op.Push, op.CheckNullInt64, op.ToNullInt64);
            StackTraits<Nullable<ulong>>.Init(op.Push, op.CheckNullUInt64, op.ToNullUInt64);

            StackTraits<byte[]>.Init(ToLua.Push, ToLua.CheckByteBuffer, ToLua.ToByteBuffer);
            StackTraits<char[]>.Init(ToLua.Push, ToLua.CheckCharBuffer, ToLua.ToCharBuffer);
            StackTraits<bool[]>.Init(ToLua.Push, ToLua.CheckBoolArray, ToLua.ToBoolArray);
            StackTraits<sbyte[]>.Init(ToLua.Push, op.CheckSByteArray, op.ToSByteArray);
            StackTraits<short[]>.Init(ToLua.Push, op.CheckInt16Array, op.ToInt16Array);
            StackTraits<ushort[]>.Init(ToLua.Push, op.CheckUInt16Array, op.ToUInt16Array);
            StackTraits<decimal[]>.Init(ToLua.Push, op.CheckDecimalArray, op.ToDecimalArray);
            StackTraits<float[]>.Init(ToLua.Push, op.CheckFloatArray, op.ToFloatArray);
            StackTraits<double[]>.Init(ToLua.Push, op.CheckDoubleArray, op.ToDoubleArray);
            StackTraits<int[]>.Init(ToLua.Push, op.CheckInt32Array, op.ToInt32Array);
            StackTraits<uint[]>.Init(ToLua.Push, op.CheckUInt32Array, op.ToUInt32Array);
            StackTraits<long[]>.Init(ToLua.Push, op.CheckInt64Array, op.ToInt64Array);
            StackTraits<ulong[]>.Init(ToLua.Push, op.CheckUInt64Array, op.ToUInt64Array);
            StackTraits<string[]>.Init(ToLua.Push, ToLua.CheckStringArray, ToLua.ToStringArray);

            StackTraits<Vector3>.Init(ToLua.Push, ToLua.CheckVector3, ToLua.ToVector3);
            StackTraits<Quaternion>.Init(ToLua.Push, ToLua.CheckQuaternion, ToLua.ToQuaternion);
            StackTraits<Vector2>.Init(ToLua.Push, ToLua.CheckVector2, ToLua.ToVector2);
            StackTraits<Color>.Init(ToLua.Push, ToLua.CheckColor, ToLua.ToColor);
            StackTraits<Vector4>.Init(ToLua.Push, ToLua.CheckVector4, ToLua.ToVector4);
            StackTraits<Ray>.Init(ToLua.Push, ToLua.CheckRay, ToLua.ToRay);
            StackTraits<Touch>.Init(ToLua.Push, null, null);
            StackTraits<Bounds>.Init(ToLua.Push, ToLua.CheckBounds, ToLua.ToBounds);
            StackTraits<LayerMask>.Init(ToLua.PushLayerMask, ToLua.CheckLayerMask, ToLua.ToLayerMask);
            StackTraits<RaycastHit>.Init(ToLua.Push, null, null);

            StackTraits<Nullable<Vector3>>.Init(op.Push, op.CheckNullVec3, op.ToNullVec3);
            StackTraits<Nullable<Quaternion>>.Init(op.Push, op.CheckNullQuat, op.ToNullQuat);
            StackTraits<Nullable<Vector2>>.Init(op.Push, op.CheckNullVec2, op.ToNullVec2);
            StackTraits<Nullable<Color>>.Init(op.Push, op.CheckNullColor, op.ToNullColor);
            StackTraits<Nullable<Vector4>>.Init(op.Push, op.CheckNullVec4, op.ToNullVec4);
            StackTraits<Nullable<Ray>>.Init(op.Push, op.CheckNullRay, op.ToNullRay);
            StackTraits<Nullable<Touch>>.Init(op.Push, null, null);
            StackTraits<Nullable<Bounds>>.Init(op.Push, op.CheckNullBounds, op.ToNullBounds);
            StackTraits<Nullable<LayerMask>>.Init(op.Push, op.CheckNullLayerMask, op.ToNullLayerMask);
            StackTraits<Nullable<RaycastHit>>.Init(op.Push, null, null);

            StackTraits<Vector3[]>.Init(ToLua.Push, op.CheckVec3Array, op.ToVec3Array);
            StackTraits<Quaternion[]>.Init(ToLua.Push, op.CheckQuatArray, op.ToQuatArray);
            StackTraits<Vector2[]>.Init(ToLua.Push, op.CheckVec2Array, op.ToVec2Array);
            StackTraits<Color[]>.Init(ToLua.Push, op.CheckColorArray, op.ToColorArray);
            StackTraits<Vector4[]>.Init(ToLua.Push, op.CheckVec4Array, op.ToVec4Array);

            StackTraits<UIntPtr>.Init(op.Push, op.CheckUIntPtr, op.CheckUIntPtr); //"NYI"
            StackTraits<IntPtr>.Init(LuaDLL.lua_pushlightuserdata, ToLua.CheckIntPtr, ToLua.CheckIntPtr);
            StackTraits<LuaFunction>.Init(ToLua.Push, ToLua.CheckLuaFunction, ToLua.ToLuaFunction);
            StackTraits<LuaTable>.Init(ToLua.Push, ToLua.CheckLuaTable, ToLua.ToLuaTable);
            StackTraits<LuaThread>.Init(ToLua.Push, ToLua.CheckLuaThread, ToLua.ToLuaThread);
            StackTraits<LuaBaseRef>.Init(ToLua.Push, ToLua.CheckLuaBaseRef, ToLua.CheckLuaBaseRef);

            StackTraits<LuaByteBuffer>.Init(ToLua.Push, op.CheckLuaByteBuffer, op.ToLuaByteBuffer);
            StackTraits<EventObject>.Init(ToLua.Push, op.CheckEventObject, op.ToEventObject);
            StackTraits<IEnumerator>.Init(ToLua.Push, ToLua.CheckIter, op.ToIter);
            StackTraits<Type>.Init(ToLua.Push, ToLua.CheckMonoType, op.ToType);
            StackTraits<Type[]>.Init(ToLua.Push, op.CheckTypeArray, op.ToTypeArray);
            StackTraits<GameObject>.Init(op.Push, op.CheckGameObject, op.ToGameObject);
            StackTraits<Transform>.Init(op.Push, op.CheckTransform, op.ToTransform);
            StackTraits<object>.Init(ToLua.Push, ToLua.ToVarObject, ToLua.ToVarObject);
            StackTraits<object[]>.Init(ToLua.Push, ToLua.CheckObjectArray, ToLua.ToObjectArray);

            StackTraits<nil>.Init(ToLua.Push, null, null);
        }

        private string LoadMathf = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local math = math
local floor = math.floor
local abs = math.abs
local Mathf = Mathf

Mathf.Deg2Rad = math.rad(1)
Mathf.Epsilon = 1.4013e-45
Mathf.Infinity = math.huge
Mathf.NegativeInfinity = -math.huge
Mathf.PI = math.pi
Mathf.Rad2Deg = math.deg(1)
		
Mathf.Abs = math.abs
Mathf.Acos = math.acos
Mathf.Asin = math.asin
Mathf.Atan = math.atan
Mathf.Atan2 = math.atan2
Mathf.Ceil = math.ceil
Mathf.Cos = math.cos
Mathf.Exp = math.exp
Mathf.Floor = math.floor
Mathf.Log = math.log
Mathf.Log10 = math.log10
Mathf.Max = math.max
Mathf.Min = math.min
Mathf.Pow = math.pow
Mathf.Sin = math.sin
Mathf.Sqrt = math.sqrt
Mathf.Tan = math.tan
Mathf.Deg = math.deg
Mathf.Rad = math.rad
Mathf.Random = math.random

function Mathf.Approximately(a, b)
	return abs(b - a) < math.max(1e-6 * math.max(abs(a), abs(b)), 1.121039e-44)
end

function Mathf.Clamp(value, min, max)
	if value < min then
		value = min
	elseif value > max then
		value = max    
	end
	
	return value
end

function Mathf.Clamp01(value)
	if value < 0 then
		return 0
	elseif value > 1 then
		return 1   
	end
	
	return value
end

function Mathf.DeltaAngle(current, target)    
	local num = Mathf.Repeat(target - current, 360)

	if num > 180 then
		num = num - 360
	end

	return num
end 

function Mathf.Gamma(value, absmax, gamma) 
	local flag = false
	
    if value < 0 then    
        flag = true
    end
	
    local num = abs(value)
	
    if num > absmax then    
        return (not flag) and num or -num
    end
	
    local num2 = math.pow(num / absmax, gamma) * absmax
    return (not flag) and num2 or -num2
end

function Mathf.InverseLerp(from, to, value)
	if from < to then      
		if value < from then 
			return 0
		end

		if value > to then      
			return 1
		end

		value = value - from
		value = value/(to - from)
		return value
	end

	if from <= to then
		return 0
	end

	if value < to then
		return 1
	end

	if value > from then
        return 0
	end

	return 1 - ((value - to) / (from - to))
end

function Mathf.Lerp(from, to, t)
	return from + (to - from) * Mathf.Clamp01(t)
end

function Mathf.LerpAngle(a, b, t)
	local num = Mathf.Repeat(b - a, 360)

	if num > 180 then
		num = num - 360
	end

	return a + num * Mathf.Clamp01(t)
end

function Mathf.LerpUnclamped(a, b, t)
    return a + (b - a) * t;
end

function Mathf.MoveTowards(current, target, maxDelta)
	if abs(target - current) <= maxDelta then
		return target
	end

	return current + Mathf.Sign(target - current) * maxDelta
end

function Mathf.MoveTowardsAngle(current, target, maxDelta)
	target = current + Mathf.DeltaAngle(current, target)
	return Mathf.MoveTowards(current, target, maxDelta)
end

function Mathf.PingPong(t, length)
    t = Mathf.Repeat(t, length * 2)
    return length - abs(t - length)
end

function Mathf.Repeat(t, length)    
	return t - (floor(t / length) * length)
end  

function Mathf.Round(num)
	return floor(num + 0.5)
end

function Mathf.Sign(num)  
	if num > 0 then
		num = 1
	elseif num < 0 then
		num = -1
	else 
		num = 0
	end

	return num
end

function Mathf.SmoothDamp(current, target, currentVelocity, smoothTime, maxSpeed, deltaTime)
	maxSpeed = maxSpeed or Mathf.Infinity
	deltaTime = deltaTime or Time.deltaTime
    smoothTime = Mathf.Max(0.0001, smoothTime)
    local num = 2 / smoothTime
    local num2 = num * deltaTime
    local num3 = 1 / (1 + num2 + 0.48 * num2 * num2 + 0.235 * num2 * num2 * num2)
    local num4 = current - target
    local num5 = target
    local max = maxSpeed * smoothTime
    num4 = Mathf.Clamp(num4, -max, max)
    target = current - num4
    local num7 = (currentVelocity + (num * num4)) * deltaTime
    currentVelocity = (currentVelocity - num * num7) * num3
    local num8 = target + (num4 + num7) * num3
	
    if (num5 > current) == (num8 > num5)  then    
        num8 = num5
        currentVelocity = (num8 - num5) / deltaTime		
    end
	
    return num8,currentVelocity
end

function Mathf.SmoothDampAngle(current, target, currentVelocity, smoothTime, maxSpeed, deltaTime)
	deltaTime = deltaTime or Time.deltaTime
	maxSpeed = maxSpeed or Mathf.Infinity	
	target = current + Mathf.DeltaAngle(current, target)
    return Mathf.SmoothDamp(current, target, currentVelocity, smoothTime, maxSpeed, deltaTime)
end


function Mathf.SmoothStep(from, to, t)
    t = Mathf.Clamp01(t)
    t = -2 * t * t * t + 3 * t * t
    return to * t + from * (1 - t)
end

function Mathf.HorizontalAngle(dir) 
	return math.deg(math.atan2(dir.x, dir.z))
end

function Mathf.IsNan(number)
	return not (number == number)
end

UnityEngine.Mathf = Mathf
return Mathf";

        private string LoadVector3 = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local math  = math
local acos	= math.acos
local sqrt 	= math.sqrt
local max 	= math.max
local min 	= math.min
local clamp = Mathf.Clamp
local cos	= math.cos
local sin	= math.sin
local abs	= math.abs
local sign	= Mathf.Sign
local setmetatable = setmetatable
local rawset = rawset
local rawget = rawget
local type = type

local rad2Deg = 57.295779513082
local deg2Rad = 0.017453292519943

local Vector3 = Vector3
local get = tolua.initget(Vector3)

Vector3.__index = function(t,k)
	local var = rawget(Vector3, k)
	
	if var == nil then						
		var = rawget(get, k)		
		
		if var ~= nil then
			return var(t)				
		end		
	end
	
	return var
end

function Vector3.New(x, y, z)				
	local t = {x = x or 0, y = y or 0, z = z or 0}
	setmetatable(t, Vector3)						
	return t
end

local _new = Vector3.New

Vector3.__call = function(t,x,y,z)
	local t = {x = x or 0, y = y or 0, z = z or 0}
	setmetatable(t, Vector3)					
	return t
end
	
function Vector3:Set(x,y,z)	
	self.x = x or 0
	self.y = y or 0
	self.z = z or 0
end

function Vector3.Get(v)		
	return v.x, v.y, v.z	
end

function Vector3:Clone()
	return setmetatable({x = self.x, y = self.y, z = self.z}, Vector3)
end

function Vector3.Distance(va, vb)
	return sqrt((va.x - vb.x)^2 + (va.y - vb.y)^2 + (va.z - vb.z)^2)
end

function Vector3.Dot(lhs, rhs)
	return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z
end

function Vector3.Lerp(from, to, t)	
	t = clamp(t, 0, 1)
	return _new(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t, from.z + (to.z - from.z) * t)
end

function Vector3:Magnitude()
	return sqrt(self.x * self.x + self.y * self.y + self.z * self.z)
end

function Vector3.Max(lhs, rhs)
	return _new(max(lhs.x, rhs.x), max(lhs.y, rhs.y), max(lhs.z, rhs.z))
end

function Vector3.Min(lhs, rhs)
	return _new(min(lhs.x, rhs.x), min(lhs.y, rhs.y), min(lhs.z, rhs.z))
end

function Vector3.Normalize(v)
	local x,y,z = v.x, v.y, v.z		
	local num = sqrt(x * x + y * y + z * z)	
	
	if num > 1e-5 then		
		return setmetatable({x = x / num, y = y / num, z = z / num}, Vector3)
    end
	  
	return setmetatable({x = 0, y = 0, z = 0}, Vector3)
end

function Vector3:SetNormalize()
	local num = sqrt(self.x * self.x + self.y * self.y + self.z * self.z)
	
	if num > 1e-5 then    
        self.x = self.x / num
		self.y = self.y / num
		self.z = self.z /num
    else    
		self.x = 0
		self.y = 0
		self.z = 0
	end 

	return self
end
	
function Vector3:SqrMagnitude()
	return self.x * self.x + self.y * self.y + self.z * self.z
end

local dot = Vector3.Dot

function Vector3.Angle(from, to)
	return acos(clamp(dot(from:Normalize(), to:Normalize()), -1, 1)) * rad2Deg
end

function Vector3:ClampMagnitude(maxLength)	
	if self:SqrMagnitude() > (maxLength * maxLength) then    
		self:SetNormalize()
		self:Mul(maxLength)        
    end
	
    return self
end


function Vector3.OrthoNormalize(va, vb, vc)	
	va:SetNormalize()
	vb:Sub(vb:Project(va))
	vb:SetNormalize()
	
	if vc == nil then
		return va, vb
	end
	
	vc:Sub(vc:Project(va))
	vc:Sub(vc:Project(vb))
	vc:SetNormalize()		
	return va, vb, vc
end
	
function Vector3.MoveTowards(current, target, maxDistanceDelta)	
	local delta = target - current	
    local sqrDelta = delta:SqrMagnitude()
	local sqrDistance = maxDistanceDelta * maxDistanceDelta
	
    if sqrDelta > sqrDistance then    
		local magnitude = sqrt(sqrDelta)
		
		if magnitude > 1e-6 then
			delta:Mul(maxDistanceDelta / magnitude)
			delta:Add(current)
			return delta
		else
			return current:Clone()
		end
    end
	
    return target:Clone()
end

function ClampedMove(lhs, rhs, clampedDelta)
	local delta = rhs - lhs
	
	if delta > 0 then
		return lhs + min(delta, clampedDelta)
	else
		return lhs - min(-delta, clampedDelta)
	end
end

local overSqrt2 = 0.7071067811865475244008443621048490

local function OrthoNormalVector(vec)
	local res = _new()
	
	if abs(vec.z) > overSqrt2 then			
		local a = vec.y * vec.y + vec.z * vec.z
		local k = 1 / sqrt (a)
		res.x = 0
		res.y = -vec.z * k
		res.z = vec.y * k
	else			
		local a = vec.x * vec.x + vec.y * vec.y
		local k = 1 / sqrt (a)
		res.x = -vec.y * k
		res.y = vec.x * k
		res.z = 0
	end
	
	return res
end

function Vector3.RotateTowards(current, target, maxRadiansDelta, maxMagnitudeDelta)
	local len1 = current:Magnitude()
	local len2 = target:Magnitude()
	
	if len1 > 1e-6 and len2 > 1e-6 then	
		local from = current / len1
		local to = target / len2		
		local cosom = dot(from, to)
				
		if cosom > 1 - 1e-6 then		
			return Vector3.MoveTowards (current, target, maxMagnitudeDelta)		
		elseif cosom < -1 + 1e-6 then		
			local axis = OrthoNormalVector(from)						
			local q = Quaternion.AngleAxis(maxRadiansDelta * rad2Deg, axis)	
			local rotated = q:MulVec3(from)
			local delta = ClampedMove(len1, len2, maxMagnitudeDelta)
			rotated:Mul(delta)
			return rotated
		else		
			local angle = acos(cosom)
			local axis = Vector3.Cross(from, to)
			axis:SetNormalize ()
			local q = Quaternion.AngleAxis(min(maxRadiansDelta, angle) * rad2Deg, axis)			
			local rotated = q:MulVec3(from)
			local delta = ClampedMove(len1, len2, maxMagnitudeDelta)
			rotated:Mul(delta)
			return rotated
		end
	end
		
	return Vector3.MoveTowards(current, target, maxMagnitudeDelta)
end
	
function Vector3.SmoothDamp(current, target, currentVelocity, smoothTime)
	local maxSpeed = Mathf.Infinity
	local deltaTime = Time.deltaTime
    smoothTime = max(0.0001, smoothTime)
    local num = 2 / smoothTime
    local num2 = num * deltaTime
    local num3 = 1 / (1 + num2 + 0.48 * num2 * num2 + 0.235 * num2 * num2 * num2)    
    local vector2 = target:Clone()
    local maxLength = maxSpeed * smoothTime
	local vector = current - target
    vector:ClampMagnitude(maxLength)
    target = current - vector
    local vec3 = (currentVelocity + (vector * num)) * deltaTime
    currentVelocity = (currentVelocity - (vec3 * num)) * num3
    local vector4 = target + (vector + vec3) * num3	
	
    if Vector3.Dot(vector2 - current, vector4 - vector2) > 0 then    
        vector4 = vector2
        currentVelocity:Set(0,0,0)
    end
	
    return vector4, currentVelocity
end	
	
function Vector3.Scale(a, b)
	local x = a.x * b.x
	local y = a.y * b.y
	local z = a.z * b.z	
	return _new(x, y, z)
end
	
function Vector3.Cross(lhs, rhs)
	local x = lhs.y * rhs.z - lhs.z * rhs.y
	local y = lhs.z * rhs.x - lhs.x * rhs.z
	local z = lhs.x * rhs.y - lhs.y * rhs.x
	return _new(x,y,z)	
end
	
function Vector3:Equals(other)
	return self.x == other.x and self.y == other.y and self.z == other.z
end
		
function Vector3.Reflect(inDirection, inNormal)
	local num = -2 * dot(inNormal, inDirection)
	inNormal = inNormal * num
	inNormal:Add(inDirection)
	return inNormal
end

	
function Vector3.Project(vector, onNormal)
	local num = onNormal:SqrMagnitude()
	
	if num < 1.175494e-38 then	
		return _new(0,0,0)
	end
	
	local num2 = dot(vector, onNormal)
	local v3 = onNormal:Clone()
	v3:Mul(num2/num)	
	return v3
end
	
function Vector3.ProjectOnPlane(vector, planeNormal)
	local v3 = Vector3.Project(vector, planeNormal)
	v3:Mul(-1)
	v3:Add(vector)
	return v3
end		

function Vector3.Slerp(from, to, t)
	local omega, sinom, scale0, scale1

	if t <= 0 then		
		return from:Clone()
	elseif t >= 1 then		
		return to:Clone()
	end
	
	local v2 	= to:Clone()
	local v1 	= from:Clone()
	local len2 	= to:Magnitude()
	local len1 	= from:Magnitude()	
	v2:Div(len2)
	v1:Div(len1)

	local len 	= (len2 - len1) * t + len1
	local cosom = v1.x * v2.x + v1.y * v2.y + v1.z * v2.z
	
	if cosom > 1 - 1e-6 then
		scale0 = 1 - t
		scale1 = t
	elseif cosom < -1 + 1e-6 then		
		local axis = OrthoNormalVector(from)		
		local q = Quaternion.AngleAxis(180.0 * t, axis)		
		local v = q:MulVec3(from)
		v:Mul(len)				
		return v
	else
		omega 	= acos(cosom)
		sinom 	= sin(omega)
		scale0 	= sin((1 - t) * omega) / sinom
		scale1 	= sin(t * omega) / sinom	
	end

	v1:Mul(scale0)
	v2:Mul(scale1)
	v2:Add(v1)
	v2:Mul(len)
	return v2
end


function Vector3:Mul(q)
	if type(q) == 'number' then
		self.x = self.x * q
		self.y = self.y * q
		self.z = self.z * q
	else
		self:MulQuat(q)
	end
	
	return self
end

function Vector3:Div(d)
	self.x = self.x / d
	self.y = self.y / d
	self.z = self.z / d
	
	return self
end

function Vector3:Add(vb)
	self.x = self.x + vb.x
	self.y = self.y + vb.y
	self.z = self.z + vb.z
	
	return self
end

function Vector3:Sub(vb)
	self.x = self.x - vb.x
	self.y = self.y - vb.y
	self.z = self.z - vb.z
	
	return self
end

function Vector3:MulQuat(quat)	   
	local num 	= quat.x * 2
	local num2 	= quat.y * 2
	local num3 	= quat.z * 2
	local num4 	= quat.x * num
	local num5 	= quat.y * num2
	local num6 	= quat.z * num3
	local num7 	= quat.x * num2
	local num8 	= quat.x * num3
	local num9 	= quat.y * num3
	local num10 = quat.w * num
	local num11 = quat.w * num2
	local num12 = quat.w * num3
	
	local x = (((1 - (num5 + num6)) * self.x) + ((num7 - num12) * self.y)) + ((num8 + num11) * self.z)
	local y = (((num7 + num12) * self.x) + ((1 - (num4 + num6)) * self.y)) + ((num9 - num10) * self.z)
	local z = (((num8 - num11) * self.x) + ((num9 + num10) * self.y)) + ((1 - (num4 + num5)) * self.z)
	
	self:Set(x, y, z)	
	return self
end

function Vector3.AngleAroundAxis (from, to, axis)	 	 
	from = from - Vector3.Project(from, axis)
	to = to - Vector3.Project(to, axis) 	    
	local angle = Vector3.Angle (from, to)	   	    
	return angle * (Vector3.Dot (axis, Vector3.Cross (from, to)) < 0 and -1 or 1)
end


Vector3.__tostring = function(self)
	return '['..self.x..','..self.y..','..self.z..']'
end

Vector3.__div = function(va, d)
	return _new(va.x / d, va.y / d, va.z / d)
end

Vector3.__mul = function(va, d)
	if type(d) == 'number' then
		return _new(va.x * d, va.y * d, va.z * d)
	else
		local vec = va:Clone()
		vec:MulQuat(d)
		return vec
	end	
end

Vector3.__add = function(va, vb)
	return _new(va.x + vb.x, va.y + vb.y, va.z + vb.z)
end

Vector3.__sub = function(va, vb)
	return _new(va.x - vb.x, va.y - vb.y, va.z - vb.z)
end

Vector3.__unm = function(va)
	return _new(-va.x, -va.y, -va.z)
end

Vector3.__eq = function(a,b)
	local v = a - b
	local delta = v:SqrMagnitude()
	return delta < 1e-10
end

get.up 		= function() return _new(0,1,0) end
get.down 	= function() return _new(0,-1,0) end
get.right	= function() return _new(1,0,0) end
get.left	= function() return _new(-1,0,0) end
get.forward = function() return _new(0,0,1) end
get.back	= function() return _new(0,0,-1) end
get.zero	= function() return _new(0,0,0) end
get.one		= function() return _new(1,1,1) end

get.magnitude	= Vector3.Magnitude
get.normalized	= Vector3.Normalize
get.sqrMagnitude= Vector3.SqrMagnitude

UnityEngine.Vector3 = Vector3
setmetatable(Vector3, Vector3)
return Vector3";

        private string LoadQuaternion = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local math	= math
local sin 	= math.sin
local cos 	= math.cos
local acos 	= math.acos
local asin 	= math.asin
local sqrt 	= math.sqrt
local min	= math.min
local max 	= math.max
local sign	= math.sign
local atan2 = math.atan2
local clamp = Mathf.Clamp
local abs	= math.abs
local setmetatable = setmetatable
local getmetatable = getmetatable
local rawget = rawget
local rawset = rawset
local Vector3 = Vector3

local rad2Deg = Mathf.Rad2Deg
local halfDegToRad = 0.5 * Mathf.Deg2Rad
local _forward = Vector3.forward
local _up = Vector3.up
local _next = { 2, 3, 1 }

local Quaternion = {}
local get = tolua.initget(Quaternion)

Quaternion.__index = function(t, k)		
	local var = rawget(Quaternion, k)
	
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)	
		end
	end

	return var
end

Quaternion.__newindex = function(t, name, k)	
	if name == 'eulerAngles' then
		t:SetEuler(k)
	else
		rawset(t, name, k)
	end	
end

function Quaternion.New(x, y, z, w)	
	local t = {x = x or 0, y = y or 0, z = z or 0, w = w or 0}
	setmetatable(t, Quaternion)	
	return t
end

local _new = Quaternion.New

Quaternion.__call = function(t, x, y, z, w)
	local t = {x = x or 0, y = y or 0, z = z or 0, w = w or 0}
	setmetatable(t, Quaternion)	
	return t
end

function Quaternion:Set(x,y,z,w)
	self.x = x or 0
	self.y = y or 0
	self.z = z or 0
	self.w = w or 0
end

function Quaternion:Clone()
	return _new(self.x, self.y, self.z, self.w)
end

function Quaternion:Get()
	return self.x, self.y, self.z, self.w
end

function Quaternion.Dot(a, b)
	return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w
end

function Quaternion.Angle(a, b)
	local dot = Quaternion.Dot(a, b)
	if dot < 0 then dot = -dot end
	return acos(min(dot, 1)) * 2 * 57.29578	
end

function Quaternion.AngleAxis(angle, axis)
	local normAxis = axis:Normalize()
    angle = angle * halfDegToRad
    local s = sin(angle)    
    
    local w = cos(angle)
    local x = normAxis.x * s
    local y = normAxis.y * s
    local z = normAxis.z * s
	
	return _new(x,y,z,w)
end

function Quaternion.Equals(a, b)
	return a.x == b.x and a.y == b.y and a.z == b.z and a.w == b.w
end

function Quaternion.Euler(x, y, z)
	if y == nil and z == nil then		
		y = x.y
		z = x.z	
		x = x.x
	end
	
	x = x * 0.0087266462599716
    y = y * 0.0087266462599716
    z = z * 0.0087266462599716

	local sinX = sin(x)
    x = cos(x)
    local sinY = sin(y)
    y = cos(y)
    local sinZ = sin(z)
    z = cos(z)

    local q = {x = y * sinX * z + sinY * x * sinZ, y = sinY * x * z - y * sinX * sinZ, z = y * x * sinZ - sinY * sinX * z, w = y * x * z + sinY * sinX * sinZ}
	setmetatable(q, Quaternion)
	return q
end

function Quaternion:SetEuler(x, y, z)		
	if y == nil and z == nil then		
		y = x.y
		z = x.z	
		x = x.x
	end
		
	x = x * 0.0087266462599716
    y = y * 0.0087266462599716
    z = z * 0.0087266462599716
	
	local sinX = sin(x)
    local cosX = cos(x)
    local sinY = sin(y)
    local cosY = cos(y)
    local sinZ = sin(z)
    local cosZ = cos(z)
    
    self.w = cosY * cosX * cosZ + sinY * sinX * sinZ
    self.x = cosY * sinX * cosZ + sinY * cosX * sinZ
    self.y = sinY * cosX * cosZ - cosY * sinX * sinZ
    self.z = cosY * cosX * sinZ - sinY * sinX * cosZ
	
	return self
end

function Quaternion:Normalize()
	local quat = self:Clone()
	quat:SetNormalize()
	return quat
end

function Quaternion:SetNormalize()
	local n = self.x * self.x + self.y * self.y + self.z * self.z + self.w * self.w
	
	if n ~= 1 and n > 0 then
		n = 1 / sqrt(n)
		self.x = self.x * n
		self.y = self.y * n
		self.z = self.z * n
		self.w = self.w * n		
	end
end

--产生一个新的从from到to的四元数
function Quaternion.FromToRotation(from, to)
	local quat = Quaternion.New()
	quat:SetFromToRotation(from, to)
	return quat
end

--设置当前四元数为 from 到 to的旋转, 注意from和to同 forward平行会同unity不一致
function Quaternion:SetFromToRotation1(from, to)
	local v0 = from:Normalize()
	local v1 = to:Normalize()
	local d = Vector3.Dot(v0, v1)

	if d > -1 + 1e-6 then	
		local s = sqrt((1+d) * 2)
		local invs = 1 / s
		local c = Vector3.Cross(v0, v1) * invs
		self:Set(c.x, c.y, c.z, s * 0.5)	
	elseif d > 1 - 1e-6 then
		return _new(0, 0, 0, 1)
	else
		local axis = Vector3.Cross(Vector3.right, v0)
		
		if axis:SqrMagnitude() < 1e-6 then
			axis = Vector3.Cross(Vector3.forward, v0)
		end

		self:Set(axis.x, axis.y, axis.z, 0)		
		return self
	end
	
	return self
end

local function MatrixToQuaternion(rot, quat)
	local trace = rot[1][1] + rot[2][2] + rot[3][3]
	
	if trace > 0 then		
		local s = sqrt(trace + 1)
		quat.w = 0.5 * s
		s = 0.5 / s
		quat.x = (rot[3][2] - rot[2][3]) * s
		quat.y = (rot[1][3] - rot[3][1]) * s
		quat.z = (rot[2][1] - rot[1][2]) * s
		quat:SetNormalize()
	else
		local i = 1		
		local q = {0, 0, 0}
		
		if rot[2][2] > rot[1][1] then			
			i = 2			
		end
		
		if rot[3][3] > rot[i][i] then
			i = 3			
		end
		
		local j = _next[i]
		local k = _next[j]
		
		local t = rot[i][i] - rot[j][j] - rot[k][k] + 1
		local s = 0.5 / sqrt(t)
		q[i] = s * t
		local w = (rot[k][j] - rot[j][k]) * s
		q[j] = (rot[j][i] + rot[i][j]) * s
		q[k] = (rot[k][i] + rot[i][k]) * s
		
		quat:Set(q[1], q[2], q[3], w)			
		quat:SetNormalize()		
	end
end

function Quaternion:SetFromToRotation(from, to)
	from = from:Normalize()
	to = to:Normalize()
	
	local e = Vector3.Dot(from, to)
	
	if e > 1 - 1e-6 then
		self:Set(0, 0, 0, 1)
	elseif e < -1 + 1e-6 then		
		local left = {0, from.z, from.y}	
		local mag = left[2] * left[2] + left[3] * left[3]  --+ left[1] * left[1] = 0
		
		if mag < 1e-6 then		
			left[1] = -from.z
			left[2] = 0
			left[3] = from.x
			mag = left[1] * left[1] + left[3] * left[3]
		end
				
		local invlen = 1/sqrt(mag)
		left[1] = left[1] * invlen
		left[2] = left[2] * invlen
		left[3] = left[3] * invlen
		
		local up = {0, 0, 0}
		up[1] = left[2] * from.z - left[3] * from.y
		up[2] = left[3] * from.x - left[1] * from.z
		up[3] = left[1] * from.y - left[2] * from.x
				

		local fxx = -from.x * from.x
		local fyy = -from.y * from.y
		local fzz = -from.z * from.z
		
		local fxy = -from.x * from.y
		local fxz = -from.x * from.z
		local fyz = -from.y * from.z

		local uxx = up[1] * up[1]
		local uyy = up[2] * up[2]
		local uzz = up[3] * up[3]
		local uxy = up[1] * up[2]
		local uxz = up[1] * up[3]
		local uyz = up[2] * up[3]

		local lxx = -left[1] * left[1]
		local lyy = -left[2] * left[2]
		local lzz = -left[3] * left[3]
		local lxy = -left[1] * left[2]
		local lxz = -left[1] * left[3]
		local lyz = -left[2] * left[3]
		
		local rot = 
		{
			{fxx + uxx + lxx, fxy + uxy + lxy, fxz + uxz + lxz},
			{fxy + uxy + lxy, fyy + uyy + lyy, fyz + uyz + lyz},
			{fxz + uxz + lxz, fyz + uyz + lyz, fzz + uzz + lzz},
		}
		
		MatrixToQuaternion(rot, self)		
	else
		local v = Vector3.Cross(from, to)
		local h = (1 - e) / Vector3.Dot(v, v) 
		
		local hx = h * v.x
		local hz = h * v.z
		local hxy = hx * v.y
		local hxz = hx * v.z
		local hyz = hz * v.y
		
		local rot = 
		{ 					
			{e + hx*v.x, 	hxy - v.z, 		hxz + v.y},
			{hxy + v.z,  	e + h*v.y*v.y, 	hyz-v.x},
			{hxz - v.y,  	hyz + v.x,    	e + hz*v.z},
		}
		
		MatrixToQuaternion(rot, self)
	end
end

function Quaternion:Inverse()
	local quat = Quaternion.New()
		
	quat.x = -self.x
	quat.y = -self.y
	quat.z = -self.z
	quat.w = self.w
	
	return quat
end

function Quaternion.Lerp(q1, q2, t)
	t = clamp(t, 0, 1)
	local q = {x = 0, y = 0, z = 0, w = 1}	
	
	if Quaternion.Dot(q1, q2) < 0 then
		q.x = q1.x + t * (-q2.x -q1.x)
		q.y = q1.y + t * (-q2.y -q1.y)
		q.z = q1.z + t * (-q2.z -q1.z)
		q.w = q1.w + t * (-q2.w -q1.w)
	else
		q.x = q1.x + (q2.x - q1.x) * t
		q.y = q1.y + (q2.y - q1.y) * t
		q.z = q1.z + (q2.z - q1.z) * t
		q.w = q1.w + (q2.w - q1.w) * t
	end	
	
	Quaternion.SetNormalize(q)	
	setmetatable(q, Quaternion)
	return q
end


function Quaternion.LookRotation(forward, up)
	local mag = forward:Magnitude()
	if mag < 1e-6 then
		error('error input forward to Quaternion.LookRotation'..tostring(forward))
		return nil
	end
	
	forward = forward / mag
	up = up or _up				
	local right = Vector3.Cross(up, forward)
	right:SetNormalize()    
    up = Vector3.Cross(forward, right)
    right = Vector3.Cross(up, forward)	
	
--[[	local quat = _new(0,0,0,1)
	local rot = 
	{ 					
		{right.x, up.x, forward.x},
		{right.y, up.y, forward.y},
		{right.z, up.z, forward.z},
	}
	
	MatrixToQuaternion(rot, quat)
	return quat--]]
		
	local t = right.x + up.y + forward.z
    
	if t > 0 then		
		local x, y, z, w
		t = t + 1
		local s = 0.5 / sqrt(t)		
		w = s * t
		x = (up.z - forward.y) * s		
		y = (forward.x - right.z) * s
		z = (right.y - up.x) * s
		
		local ret = _new(x, y, z, w)	
		ret:SetNormalize()
		return ret
	else
		local rot = 
		{ 					
			{right.x, up.x, forward.x},
			{right.y, up.y, forward.y},
			{right.z, up.z, forward.z},
		}
	
		local q = {0, 0, 0}
		local i = 1		
		
		if up.y > right.x then			
			i = 2			
		end
		
		if forward.z > rot[i][i] then
			i = 3			
		end
		
		local j = _next[i]
		local k = _next[j]
		
		local t = rot[i][i] - rot[j][j] - rot[k][k] + 1
		local s = 0.5 / sqrt(t)
		q[i] = s * t
		local w = (rot[k][j] - rot[j][k]) * s
		q[j] = (rot[j][i] + rot[i][j]) * s
		q[k] = (rot[k][i] + rot[i][k]) * s
		
		local ret = _new(q[1], q[2], q[3], w)			
		ret:SetNormalize()
		return ret
	end
end

function Quaternion:SetIdentity()
	self.x = 0
	self.y = 0
	self.z = 0
	self.w = 1
end

local function UnclampedSlerp(q1, q2, t)		
	local dot = q1.x * q2.x + q1.y * q2.y + q1.z * q2.z + q1.w * q2.w

    if dot < 0 then
        dot = -dot        
        q2 = setmetatable({x = -q2.x, y = -q2.y, z = -q2.z, w = -q2.w}, Quaternion)        
    end

    if dot < 0.95 then		
	    local angle = acos(dot)
        local invSinAngle = 1 / sin(angle)
        local t1 = sin((1 - t) * angle) * invSinAngle
        local t2 = sin(t * angle) * invSinAngle
        q1 = {x = q1.x * t1 + q2.x * t2, y = q1.y * t1 + q2.y * t2, z = q1.z * t1 + q2.z * t2, w = q1.w * t1 + q2.w * t2}
		setmetatable(q1, Quaternion)		
		return q1
	else
		q1 = {x = q1.x + t * (q2.x - q1.x), y = q1.y + t * (q2.y - q1.y), z = q1.z + t * (q2.z - q1.z), w = q1.w + t * (q2.w - q1.w)}		
		Quaternion.SetNormalize(q1)		
		setmetatable(q1, Quaternion)
		return q1
    end
end


function Quaternion.Slerp(from, to, t)	
	if t < 0 then
		t = 0
	elseif t > 1 then
		t = 1
	end

	return UnclampedSlerp(from, to, t)
end

function Quaternion.RotateTowards(from, to, maxDegreesDelta)   	
	local angle = Quaternion.Angle(from, to)
	
	if angle == 0 then
		return to
	end
	
	local t = min(1, maxDegreesDelta / angle)
	return UnclampedSlerp(from, to, t)
end

local function Approximately(f0, f1)
	return abs(f0 - f1) < 1e-6	
end

function Quaternion:ToAngleAxis()		
	local angle = 2 * acos(self.w)
	
	if Approximately(angle, 0) then
		return angle * 57.29578, Vector3.New(1, 0, 0)
	end
	
	local div = 1 / sqrt(1 - sqrt(self.w))
	return angle * 57.29578, Vector3.New(self.x * div, self.y * div, self.z * div)
end

local pi = Mathf.PI
local half_pi = pi * 0.5
local two_pi = 2 * pi
local negativeFlip = -0.0001
local positiveFlip = two_pi - 0.0001
	
local function SanitizeEuler(euler)	
	if euler.x < negativeFlip then
		euler.x = euler.x + two_pi
	elseif euler.x > positiveFlip then
		euler.x = euler.x - two_pi
	end

	if euler.y < negativeFlip then
		euler.y = euler.y + two_pi
	elseif euler.y > positiveFlip then
		euler.y = euler.y - two_pi
	end

	if euler.z < negativeFlip then
		euler.z = euler.z + two_pi
	elseif euler.z > positiveFlip then
		euler.z = euler.z + two_pi
	end
end

--from http://www.geometrictools.com/Documentation/EulerAngles.pdf
--Order of rotations: YXZ
function Quaternion:ToEulerAngles()
	local x = self.x
	local y = self.y
	local z = self.z
	local w = self.w
		
	local check = 2 * (y * z - w * x)
	
	if check < 0.999 then
		if check > -0.999 then
			local v = Vector3.New( -asin(check), 
						atan2(2 * (x * z + w * y), 1 - 2 * (x * x + y * y)), 
						atan2(2 * (x * y + w * z), 1 - 2 * (x * x + z * z)))
			SanitizeEuler(v)
			v:Mul(rad2Deg)
			return v
		else
			local v = Vector3.New(half_pi, atan2(2 * (x * y - w * z), 1 - 2 * (y * y + z * z)), 0)
			SanitizeEuler(v)
			v:Mul(rad2Deg)
			return v
		end
	else
		local v = Vector3.New(-half_pi, atan2(-2 * (x * y - w * z), 1 - 2 * (y * y + z * z)), 0)
		SanitizeEuler(v)
		v:Mul(rad2Deg)
		return v		
	end
end

function Quaternion:Forward()
	return self:MulVec3(_forward)
end

function Quaternion.MulVec3(self, point)
	local vec = Vector3.New()
    
	local num 	= self.x * 2
	local num2 	= self.y * 2
	local num3 	= self.z * 2
	local num4 	= self.x * num
	local num5 	= self.y * num2
	local num6 	= self.z * num3
	local num7 	= self.x * num2
	local num8 	= self.x * num3
	local num9 	= self.y * num3
	local num10 = self.w * num
	local num11 = self.w * num2
	local num12 = self.w * num3
	
	vec.x = (((1 - (num5 + num6)) * point.x) + ((num7 - num12) * point.y)) + ((num8 + num11) * point.z)
	vec.y = (((num7 + num12) * point.x) + ((1 - (num4 + num6)) * point.y)) + ((num9 - num10) * point.z)
	vec.z = (((num8 - num11) * point.x) + ((num9 + num10) * point.y)) + ((1 - (num4 + num5)) * point.z)
	
	return vec
end

Quaternion.__mul = function(lhs, rhs)
	if Quaternion == getmetatable(rhs) then
		return Quaternion.New((((lhs.w * rhs.x) + (lhs.x * rhs.w)) + (lhs.y * rhs.z)) - (lhs.z * rhs.y), (((lhs.w * rhs.y) + (lhs.y * rhs.w)) + (lhs.z * rhs.x)) - (lhs.x * rhs.z), (((lhs.w * rhs.z) + (lhs.z * rhs.w)) + (lhs.x * rhs.y)) - (lhs.y * rhs.x), (((lhs.w * rhs.w) - (lhs.x * rhs.x)) - (lhs.y * rhs.y)) - (lhs.z * rhs.z))	
	elseif Vector3 == getmetatable(rhs) then
		return lhs:MulVec3(rhs)
	end
end

Quaternion.__unm = function(q)
	return Quaternion.New(-q.x, -q.y, -q.z, -q.w)
end

Quaternion.__eq = function(lhs,rhs)
	return Quaternion.Dot(lhs, rhs) > 0.999999
end

Quaternion.__tostring = function(self)
	return '['..self.x..','..self.y..','..self.z..','..self.w..']'
end

get.identity = function() return _new(0, 0, 0, 1) end
get.eulerAngles = Quaternion.ToEulerAngles

UnityEngine.Quaternion = Quaternion
setmetatable(Quaternion, Quaternion)
return Quaternion";
        private string LoadVector2 = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------

local sqrt = math.sqrt
local setmetatable = setmetatable
local rawget = rawget
local math = math
local acos = math.acos
local max = math.max

local Vector2 = {}
local get = tolua.initget(Vector2)

Vector2.__index = function(t,k)
	local var = rawget(Vector2, k)
	
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)
		end
	end
	
	return var
end

Vector2.__call = function(t, x, y)
	return setmetatable({x = x or 0, y = y or 0}, Vector2)
end

function Vector2.New(x, y)
	return setmetatable({x = x or 0, y = y or 0}, Vector2)
end

function Vector2:Set(x,y)
	self.x = x or 0
	self.y = y or 0	
end

function Vector2:Get()
	return self.x, self.y
end

function Vector2:SqrMagnitude()
	return self.x * self.x + self.y * self.y
end

function Vector2:Clone()
	return setmetatable({x = self.x, y = self.y}, Vector2)
end


function Vector2.Normalize(v)
	local x = v.x
	local y = v.y
	local magnitude = sqrt(x * x + y * y)

	if magnitude > 1e-05 then
		x = x / magnitude
		y = y / magnitude
    else
        x = 0
		y = 0
	end

	return setmetatable({x = x, y = y}, Vector2)
end

function Vector2:SetNormalize()
	local magnitude = sqrt(self.x * self.x + self.y * self.y)

	if magnitude > 1e-05 then
		self.x = self.x / magnitude
		self.y = self.y / magnitude
    else
        self.x = 0
		self.y = 0
	end

	return self
end


function Vector2.Dot(lhs, rhs)
	return lhs.x * rhs.x + lhs.y * rhs.y
end

function Vector2.Angle(from, to)
	local x1,y1 = from.x, from.y
	local d = sqrt(x1 * x1 + y1 * y1)

	if d > 1e-5 then
		x1 = x1/d
		y1 = y1/d
	else
		x1,y1 = 0,0
	end

	local x2,y2 = to.x, to.y
	d = sqrt(x2 * x2 + y2 * y2)

	if d > 1e-5 then
		x2 = x2/d
		y2 = y2/d
	else
		x2,y2 = 0,0
	end

	d = x1 * x2 + y1 * y2

	if d < -1 then
		d = -1
	elseif d > 1 then
		d = 1
	end

	return acos(d) * 57.29578
end

function Vector2.Magnitude(v)
	return sqrt(v.x * v.x + v.y * v.y)
end

function Vector2.Reflect(dir, normal)
	local dx = dir.x
	local dy = dir.y
	local nx = normal.x
	local ny = normal.y
	local s = -2 * (dx * nx + dy * ny)

	return setmetatable({x = s * nx + dx, y = s * ny + dy}, Vector2)
end

function Vector2.Distance(a, b)
	return sqrt((a.x - b.x) ^ 2 + (a.y - b.y) ^ 2)
end

function Vector2.Lerp(a, b, t)
	if t < 0 then
		t = 0
	elseif t > 1 then
		t = 1
	end

    return setmetatable({x = a.x + (b.x - a.x) * t, y = a.y + (b.y - a.y) * t}, Vector2)
end

function Vector2.LerpUnclamped(a, b, t)
    return setmetatable({x = a.x + (b.x - a.x) * t, y = a.y + (b.y - a.y) * t}, Vector2)
end

function Vector2.MoveTowards(current, target, maxDistanceDelta)
	local cx = current.x
	local cy = current.y
	local x = target.x - cx
	local y = target.y - cy
	local s = x * x + y * y

	if s  > maxDistanceDelta * maxDistanceDelta and s ~= 0 then
		s = maxDistanceDelta / sqrt(s)
		return setmetatable({x = cx + x * s, y = cy + y * s}, Vector2)
	end

    return setmetatable({x = target.x, y = target.y}, Vector2)
end

function Vector2.ClampMagnitude(v, maxLength)
	local x = v.x
	local y = v.y
	local sqrMag = x * x + y * y

    if sqrMag > maxLength * maxLength then
		local mag = maxLength / sqrt(sqrMag)
		x = x * mag
		y = y * mag
        return setmetatable({x = x, y = y}, Vector2)
    end

    return setmetatable({x = x, y = y}, Vector2)
end

function Vector2.SmoothDamp(current, target, Velocity, smoothTime, maxSpeed, deltaTime)
	deltaTime = deltaTime or Time.deltaTime
	maxSpeed = maxSpeed or math.huge
	smoothTime = math.max(0.0001, smoothTime)

	local num = 2 / smoothTime
    local num2 = num * deltaTime
    num2 = 1 / (1 + num2 + 0.48 * num2 * num2 + 0.235 * num2 * num2 * num2)

	local tx = target.x
	local ty = target.y
	local cx = current.x
	local cy = current.y
    local vecx = cx - tx
	local vecy = cy - ty
	local m = vecx * vecx + vecy * vecy
	local n = maxSpeed * smoothTime

	if m > n * n then
		m = n / sqrt(m)
		vecx = vecx * m
		vecy = vecy * m
	end

	m = Velocity.x
	n = Velocity.y

	local vec3x = (m + num * vecx) * deltaTime
	local vec3y = (n + num * vecy) * deltaTime
	Velocity.x = (m - num * vec3x) * num2
	Velocity.y = (n - num * vec3y) * num2
	m = cx - vecx + (vecx + vec3x) * num2
	n = cy - vecy + (vecy + vec3y) * num2

	if (tx - cx) * (m - tx) + (ty - cy) * (n - ty)  > 0 then
		m = tx
		n = ty
		Velocity.x = 0
		Velocity.y = 0
	end

    return setmetatable({x = m, y = n}, Vector2), Velocity
end

function Vector2.Max(a, b)
	return setmetatable({x = math.max(a.x, b.x), y = math.max(a.y, b.y)}, Vector2)
end

function Vector2.Min(a, b)
	return setmetatable({x = math.min(a.x, b.x), y = math.min(a.y, b.y)}, Vector2)
end

function Vector2.Scale(a, b)
	return setmetatable({x = a.x * b.x, y = a.y * b.y}, Vector2)
end

function Vector2:Div(d)
	self.x = self.x / d
	self.y = self.y / d	
	
	return self
end

function Vector2:Mul(d)
	self.x = self.x * d
	self.y = self.y * d
	
	return self
end

function Vector2:Add(b)
	self.x = self.x + b.x
	self.y = self.y + b.y
	
	return self
end

function Vector2:Sub(b)
	self.x = self.x - b.x
	self.y = self.y - b.y
	
	return
end

Vector2.__tostring = function(self)
	return string.format('(%f,%f)', self.x, self.y)
end

Vector2.__div = function(va, d)
	return setmetatable({x = va.x / d, y = va.y / d}, Vector2)
end

Vector2.__mul = function(a, d)
	if type(d) == 'number' then
		return setmetatable({x = a.x * d, y = a.y * d}, Vector2)
	else
		return setmetatable({x = a * d.x, y = a * d.y}, Vector2)
	end
end

Vector2.__add = function(a, b)
	return setmetatable({x = a.x + b.x, y = a.y + b.y}, Vector2)
end

Vector2.__sub = function(a, b)
	return setmetatable({x = a.x - b.x, y = a.y - b.y}, Vector2)
end

Vector2.__unm = function(v)
	return setmetatable({x = -v.x, y = -v.y}, Vector2)
end

Vector2.__eq = function(a,b)
	return ((a.x - b.x) ^ 2 + (a.y - b.y) ^ 2) < 9.999999e-11
end

get.up 		= function() return setmetatable({x = 0, y = 1}, Vector2) end
get.right	= function() return setmetatable({x = 1, y = 0}, Vector2) end
get.zero	= function() return setmetatable({x = 0, y = 0}, Vector2) end
get.one		= function() return setmetatable({x = 1, y = 1}, Vector2) end

get.magnitude 		= Vector2.Magnitude
get.normalized 		= Vector2.Normalize
get.sqrMagnitude 	= Vector2.SqrMagnitude

UnityEngine.Vector2 = Vector2
setmetatable(Vector2, Vector2)
return Vector2";
        private string LoadVector4 = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------

local clamp	= Mathf.Clamp
local sqrt	= Mathf.Sqrt
local min	= Mathf.Min
local max 	= Mathf.Max
local setmetatable = setmetatable
local rawget = rawget

local Vector4 = {}
local get = tolua.initget(Vector4)

Vector4.__index = function(t,k)
	local var = rawget(Vector4, k)
	
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)	
		end
	end
	
	return var
end

Vector4.__call = function(t, x, y, z, w)
	return setmetatable({x = x or 0, y = y or 0, z = z or 0, w = w or 0}, Vector4)		
end

function Vector4.New(x, y, z, w)	
	return setmetatable({x = x or 0, y = y or 0, z = z or 0, w = w or 0}, Vector4)		
end

function Vector4:Set(x,y,z,w)
	self.x = x or 0
	self.y = y or 0	
	self.z = z or 0
	self.w = w or 0
end

function Vector4:Get()
	return self.x, self.y, self.z, self.w
end

function Vector4.Lerp(from, to, t)    
    t = clamp(t, 0, 1)
    return Vector4.New(from.x + ((to.x - from.x) * t), from.y + ((to.y - from.y) * t), from.z + ((to.z - from.z) * t), from.w + ((to.w - from.w) * t))
end    

function Vector4.MoveTowards(current, target, maxDistanceDelta)    
	local vector = target - current
	local magnitude = vector:Magnitude()	
	
	if magnitude > maxDistanceDelta and magnitude ~= 0 then     
		maxDistanceDelta = maxDistanceDelta / magnitude
		vector:Mul(maxDistanceDelta)   
		vector:Add(current)
		return vector
	end
	
	return target
end    

function Vector4.Scale(a, b)    
    return Vector4.New(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w)
end    

function Vector4:SetScale(scale)
	self.x = self.x * scale.x
	self.y = self.y * scale.y
	self.z = self.z * scale.z
	self.w = self.w * scale.w
end

function Vector4:Normalize()
	local v = vector4.New(self.x, self.y, self.z, self.w)
	return v:SetNormalize()
end

function Vector4:SetNormalize()
	local num = self:Magnitude()	
	
	if num == 1 then
		return self
    elseif num > 1e-05 then    
        self:Div(num)
    else    
        self:Set(0,0,0,0)
	end 

	return self
end

function Vector4:Div(d)
	self.x = self.x / d
	self.y = self.y / d	
	self.z = self.z / d
	self.w = self.w / d
	
	return self
end

function Vector4:Mul(d)
	self.x = self.x * d
	self.y = self.y * d
	self.z = self.z * d
	self.w = self.w * d	
	
	return self
end

function Vector4:Add(b)
	self.x = self.x + b.x
	self.y = self.y + b.y
	self.z = self.z + b.z
	self.w = self.w + b.w
	
	return self
end

function Vector4:Sub(b)
	self.x = self.x - b.x
	self.y = self.y - b.y
	self.z = self.z - b.z
	self.w = self.w - b.w
	
	return self
end

function Vector4.Dot(a, b)
	return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w
end

function Vector4.Project(a, b)
	local s = Vector4.Dot(a, b) / Vector4.Dot(b, b)
	return b * s
end

function Vector4.Distance(a, b)
	local v = a - b
	return Vector4.Magnitude(v)
end

function Vector4.Magnitude(a)
	return sqrt(a.x * a.x + a.y * a.y + a.z * a.z + a.w * a.w)
end

function Vector4.SqrMagnitude(a)
	return a.x * a.x + a.y * a.y + a.z * a.z + a.w * a.w
end

function Vector4.Min(lhs, rhs)
	return Vector4.New(max(lhs.x, rhs.x), max(lhs.y, rhs.y), max(lhs.z, rhs.z), max(lhs.w, rhs.w))
end

function Vector4.Max(lhs, rhs)
	return Vector4.New(min(lhs.x, rhs.x), min(lhs.y, rhs.y), min(lhs.z, rhs.z), min(lhs.w, rhs.w))
end

Vector4.__tostring = function(self)
	return string.format('[%f,%f,%f,%f]', self.x, self.y, self.z, self.w)
end

Vector4.__div = function(va, d)
	return Vector4.New(va.x / d, va.y / d, va.z / d, va.w / d)
end

Vector4.__mul = function(va, d)
	return Vector4.New(va.x * d, va.y * d, va.z * d, va.w * d)
end

Vector4.__add = function(va, vb)
	return Vector4.New(va.x + vb.x, va.y + vb.y, va.z + vb.z, va.w + vb.w)
end

Vector4.__sub = function(va, vb)
	return Vector4.New(va.x - vb.x, va.y - vb.y, va.z - vb.z, va.w - vb.w)
end

Vector4.__unm = function(va)
	return Vector4.New(-va.x, -va.y, -va.z, -va.w)
end

Vector4.__eq = function(va,vb)
	local v = va - vb
	local delta = Vector4.SqrMagnitude(v)	
	return delta < 1e-10
end

get.zero = function() return Vector4.New(0, 0, 0, 0) end
get.one	 = function() return Vector4.New(1, 1, 1, 1) end

get.magnitude 	 = Vector4.Magnitude
get.normalized 	 = Vector4.Normalize
get.sqrMagnitude = Vector4.SqrMagnitude

UnityEngine.Vector4 = Vector4
setmetatable(Vector4, Vector4)
return Vector4";
        private string LoadColor = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------

local rawget = rawget
local setmetatable = setmetatable
local type = type
local Mathf = Mathf

local Color = {}
local get = tolua.initget(Color)

Color.__index = function(t,k)
	local var = rawget(Color, k)
		
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)	
		end
	end
	
	return var
end

Color.__call = function(t, r, g, b, a)
	return setmetatable({r = r or 0, g = g or 0, b = b or 0, a = a or 1}, Color)   
end

function Color.New(r, g, b, a)
	return setmetatable({r = r or 0, g = g or 0, b = b or 0, a = a or 1}, Color)		
end

function Color:Set(r, g, b, a)
	self.r = r
	self.g = g
	self.b = b
	self.a = a or 1 
end

function Color:Get()
	return self.r, self.g, self.b, self.a
end

function Color:Equals(other)
	return self.r == other.r and self.g == other.g and self.b == other.b and self.a == other.a
end

function Color.Lerp(a, b, t)
	t = Mathf.Clamp01(t)
	return Color.New(a.r + t * (b.r - a.r), a.g + t * (b.g - a.g), a.b + t * (b.b - a.b), a.a + t * (b.a - a.a))
end

function Color.LerpUnclamped(a, b, t)
  return Color.New(a.r + t * (b.r - a.r), a.g + t * (b.g - a.g), a.b + t * (b.b - a.b), a.a + t * (b.a - a.a))
end

function Color.HSVToRGB(H, S, V, hdr)
  hdr = hdr and false or true  
  local white = Color.New(1,1,1,1)
  
  if S == 0 then    
    white.r = V
    white.g = V
    white.b = V
    return white
  end
  
  if V == 0 then    
    white.r = 0
    white.g = 0
    white.b = 0
    return white
  end
  
  white.r = 0
  white.g = 0
  white.b = 0;
  local num = S
  local num2 = V
  local f = H * 6;
  local num4 = Mathf.Floor(f)
  local num5 = f - num4
  local num6 = num2 * (1 - num)
  local num7 = num2 * (1 - (num * num5))
  local num8 = num2 * (1 - (num * (1 - num5)))
  local num9 = num4
  
  local flag = num9 + 1
  
  if flag == 0 then
    white.r = num2
    white.g = num6
    white.b = num7
  elseif flag == 1 then
    white.r = num2
    white.g = num8
    white.b = num6
  elseif flag == 2 then
    white.r = num7
    white.g = num2
    white.b = num6
  elseif flag == 3 then
    white.r = num6
    white.g = num2
    white.b = num8
  elseif flag == 4 then
    white.r = num6
    white.g = num7
    white.b = num2
  elseif flag == 5 then
    white.r = num8
    white.g = num6
    white.b = num2
  elseif flag == 6 then
    white.r = num2
    white.g = num6
    white.b = num7
  elseif flag == 7 then
    white.r = num2
    white.g = num8
    white.b = num6
  end
  
  if not hdr then    
    white.r = Mathf.Clamp(white.r, 0, 1)
    white.g = Mathf.Clamp(white.g, 0, 1)
    white.b = Mathf.Clamp(white.b, 0, 1)
  end
    
  return white
end

local function RGBToHSVHelper(offset, dominantcolor, colorone, colortwo)
  local V = dominantcolor
    
  if V ~= 0 then    
    local num = 0
        
    if colorone > colortwo then        
      num = colortwo
    else        
      num = colorone
    end
        
    local num2 = V - num
    local H = 0
    local S = 0
        
    if num2 ~= 0 then        
      S = num2 / V
      H = offset + (colorone - colortwo) / num2
    else        
      S = 0
      H = offset + (colorone - colortwo)
    end
        
    H = H / 6  
    if H < 0 then H = H + 1 end                
    return H, S, V
  end
  
  return 0, 0, V  
end

function Color.RGBToHSV(rgbColor)
    if rgbColor.b > rgbColor.g and rgbColor.b > rgbColor.r then    
        return RGBToHSVHelper(4, rgbColor.b, rgbColor.r, rgbColor.g)    
    elseif rgbColor.g > rgbColor.r then    
        return RGBToHSVHelper(2, rgbColor.g, rgbColor.b, rgbColor.r)
    else    
        return RGBToHSVHelper(0, rgbColor.r, rgbColor.g, rgbColor.b)
    end
end

function Color.GrayScale(a)
	return 0.299 * a.r + 0.587 * a.g + 0.114 * a.b
end

Color.__tostring = function(self)
	return string.format('RGBA(%f,%f,%f,%f)', self.r, self.g, self.b, self.a)
end

Color.__add = function(a, b)
	return Color.New(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a)
end

Color.__sub = function(a, b)	
	return Color.New(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a)
end

Color.__mul = function(a, b)
	if type(b) == 'number' then
		return Color.New(a.r * b, a.g * b, a.b * b, a.a * b)
	elseif getmetatable(b) == Color then
		return Color.New(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a)
	end
end

Color.__div = function(a, d)
	return Color.New(a.r / d, a.g / d, a.b / d, a.a / d)
end

Color.__eq = function(a,b)
	return a.r == b.r and a.g == b.g and a.b == b.b and a.a == b.a
end

get.red 	= function() return Color.New(1,0,0,1) end
get.green	= function() return Color.New(0,1,0,1) end
get.blue	= function() return Color.New(0,0,1,1) end
get.white	= function() return Color.New(1,1,1,1) end
get.black	= function() return Color.New(0,0,0,1) end
get.yellow	= function() return Color.New(1, 0.9215686, 0.01568628, 1) end
get.cyan	= function() return Color.New(0,1,1,1) end
get.magenta	= function() return Color.New(1,0,1,1) end
get.gray	= function() return Color.New(0.5,0.5,0.5,1) end
get.clear	= function() return Color.New(0,0,0,0) end

get.gamma = function(c) 
  return Color.New(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), c.a)  
end

get.linear = function(c)
  return Color.New(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a)
end

get.maxColorComponent = function(c)    
  return Mathf.Max(Mathf.Max(c.r, c.g), c.b)
end

get.grayscale = Color.GrayScale

UnityEngine.Color = Color
setmetatable(Color, Color)
return Color



";
        private string LoadRay = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local rawget = rawget
local setmetatable = setmetatable
local Vector3 = Vector3

local Ray = 
{
	direction = Vector3.zero,
	origin = Vector3.zero,
}

local get = tolua.initget(Ray)

Ray.__index = function(t,k)
	local var = rawget(Ray, k)
		
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)	
		end
	end
	
	return var
end

Ray.__call = function(t, direction, origin)
	return Ray.New(direction, origin)
end

function Ray.New(direction, origin)
	local ray = {}	
	ray.direction 	= direction:Normalize()
	ray.origin 		= origin
	setmetatable(ray, Ray)	
	return ray
end

function Ray:GetPoint(distance)
	local dir = self.direction * distance
	dir:Add(self.origin)
	return dir
end

function Ray:Get()		
	local o = self.origin
	local d = self.direction
	return o.x, o.y, o.z, d.x, d.y, d.z
end

Ray.__tostring = function(self)
	return string.format('Origin:(%f,%f,%f),Dir:(%f,%f, %f)', self.origin.x, self.origin.y, self.origin.z, self.direction.x, self.direction.y, self.direction.z)
end

UnityEngine.Ray = Ray
setmetatable(Ray, Ray)
return Ray";
        private string LoadBounds = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local rawget = rawget
local setmetatable = setmetatable
local type = type
local Vector3 = Vector3
local zero = Vector3.zero

local Bounds = 
{
	center = Vector3.zero,
	extents = Vector3.zero,
}

local get = tolua.initget(Bounds)

Bounds.__index = function(t,k)
	local var = rawget(Bounds, k)
	
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)	
		end
	end
	
	return var
end

Bounds.__call = function(t, center, size)
	return setmetatable({center = center, extents = size * 0.5}, Bounds)		
end

function Bounds.New(center, size)	
	return setmetatable({center = center, extents = size * 0.5}, Bounds)		
end

function Bounds:Get()
	local size = self:GetSize()	
	return self.center, size
end

function Bounds:GetSize()
	return self.extents * 2
end

function Bounds:SetSize(value)
	self.extents = value * 0.5
end

function Bounds:GetMin()
	return self.center - self.extents
end

function Bounds:SetMin(value)
	self:SetMinMax(value, self:GetMax())
end

function Bounds:GetMax()
	return self.center + self.extents
end

function Bounds:SetMax(value)
	self:SetMinMax(self:GetMin(), value)
end

function Bounds:SetMinMax(min, max)
	self.extents = (max - min) * 0.5
	self.center = min + self.extents
end

function Bounds:Encapsulate(point)
	self:SetMinMax(Vector3.Min(self:GetMin(), point), Vector3.Max(self:GetMax(), point))
end

function Bounds:Expand(amount)	
	if type(amount) == 'number' then
		amount = amount * 0.5
		self.extents:Add(Vector3.New(amount, amount, amount))
	else
		self.extents:Add(amount * 0.5)
	end
end

function Bounds:Intersects(bounds)
	local min = self:GetMin()
	local max = self:GetMax()
	
	local min2 = bounds:GetMin()
	local max2 = bounds:GetMax()
	
	return min.x <= max2.x and max.x >= min2.x and min.y <= max2.y and max.y >= min2.y and min.z <= max2.z and max.z >= min2.z
end    

function Bounds:Contains(p)
	local min = self:GetMin()
	local max = self:GetMax()
	
	if p.x < min.x or p.y < min.y or p.z < min.z or p.x > max.x or p.y > max.y or p.z > max.z then
		return false
	end
	
	return true
end

function Bounds:IntersectRay(ray)
	local tmin = -Mathf.Infinity
	local tmax = Mathf.Infinity
	
	local t0, t1, f
	local t = self:GetCenter () - ray:GetOrigin()
	local p = {t.x, t.y, t.z}
	t = self.extents
	local extent = {t.x, t.y, t.z}
	t = ray:GetDirection()
	local dir = {t.x, t.y, t.z}
  
	for i = 1, 3 do	
		f = 1 / dir[i]
		t0 = (p[i] + extent[i]) * f
		t1 = (p[i] - extent[i]) * f
			
		if t0 < t1 then			
			if t0 > tmin then tmin = t0 end				
			if t1 < tmax then tmax = t1 end				
			if tmin > tmax then return false end				
			if tmax < 0 then return false end        
		else			
			if t1 > tmin then tmin = t1 end				
			if t0 < tmax then tmax = t0 end				
			if tmin > tmax then return false end				
			if tmax < 0 then return false end
		end
	end
	
	return true, tmin
end

function Bounds:ClosestPoint(point)
	local t = point - self:GetCenter()
	local closest = {t.x, t.y, t.z}
	local et = self.extents
	local extent = {et.x, et.y, et.z}
	local distance = 0
	local delta
	
	for i = 1, 3 do	
		if  closest[i] < - extent[i] then		
			delta = closest[i] + extent[i]
			distance = distance + delta * delta
			closest[i] = -extent[i]
		elseif closest[i] > extent[i]  then
			delta = closest[i] - extent[i]
			distance = distance + delta * delta
			closest[i] = extent[i]
		end
	end
		
	if distance == 0 then	    
		return point, 0
	else	
		outPoint = closest + self:GetCenter()
		return outPoint, distance
	end
end

function Bounds:Destroy()
	self.center	= nil
	self.size	= nil
end

Bounds.__tostring = function(self)	
	return string.format('Center: %s, Extents %s', tostring(self.center), tostring(self.extents))
end

Bounds.__eq = function(a, b)
	return a.center == b.center and a.extents == b.extents
end

get.size = Bounds.GetSize
get.min = Bounds.GetMin
get.max = Bounds.GetMax

UnityEngine.Bounds = Bounds
setmetatable(Bounds, Bounds)
return Bounds
";
        private string LoadRaycastHit = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local rawget = rawget
local setmetatable = setmetatable

RaycastBits = 
{
	Collider = 1,
    Normal = 2,
    Point = 4,
    Rigidbody = 8,
    Transform = 16,
    ALL = 31,
}
	
local RaycastBits = RaycastBits
local RaycastHit = {}
local get = tolua.initget(RaycastHit)

RaycastHit.__index = function(t,k)
	local var = rawget(RaycastHit, k)
		
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)	
		end
	end
	
	return var
end

--c# 创建
function RaycastHit.New(collider, distance, normal, point, rigidbody, transform)
	local hit = {collider = collider, distance = distance, normal = normal, point = point, rigidbody = rigidbody, transform = transform}
	setmetatable(hit, RaycastHit)
	return hit
end

function RaycastHit:Init(collider, distance, normal, point, rigidbody, transform)
	self.collider 	= collider
	self.distance 	= distance
	self.normal 	= normal
	self.point 		= point
	self.rigidbody 	= rigidbody
	self.transform 	= transform
end

function RaycastHit:Get()
	return self.collider, self.distance, self.normal, self.point, self.rigidbody, self.transform
end

function RaycastHit:Destroy()				
	self.collider 	= nil			
	self.rigidbody 	= nil					
	self.transform 	= nil		
end

function RaycastHit.GetMask(...)
	local arg = {...}
	local value = 0	

	for i = 1, #arg do		
		local n = RaycastBits[arg[i]] or 0
		
		if n ~= 0 then
			value = value + n				
		end
	end	
		
	if value == 0 then value = RaycastBits['all'] end
	return value
end

UnityEngine.RaycastHit = RaycastHit
setmetatable(RaycastHit, RaycastHit)
return RaycastHit";
        private string LoadTouch = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local zero = Vector2.zero
local rawget = rawget
local setmetatable = setmetatable

TouchPhase =
{
	Began = 0,
	Moved = 1,
	Stationary = 2,
	Ended = 3,
	Canceled = 4,
}

TouchBits = 
{
	DeltaPosition = 1,
	Position = 2,
	RawPosition = 4,
	ALL = 7,
}

local TouchPhase = TouchPhase
local TouchBits = TouchBits
local Touch = {}
local get = tolua.initget(Touch)

Touch.__index = function(t,k)
	local var = rawget(Touch, k)
	
	if var == nil then							
		var = rawget(get, k)
		
		if var ~= nil then
			return var(t)	
		end
	end
	
	return var
end

--c# 创建
function Touch.New(fingerId, position, rawPosition, deltaPosition, deltaTime, tapCount, phase)	
	return setmetatable({fingerId = fingerId or 0, position = position or zero, rawPosition = rawPosition or zero, deltaPosition = deltaPosition or zero, deltaTime = deltaTime or 0, tapCount = tapCount or 0, phase = phase or 0}, Touch)	
end

function Touch:Init(fingerId, position, rawPosition, deltaPosition, deltaTime, tapCount, phase)
	self.fingerId = fingerId
	self.position = position
	self.rawPosition = rawPosition
	self.deltaPosition = deltaPosition
	self.deltaTime = deltaTime
	self.tapCount = tapCount
	self.phase = phase	
end

function Touch:Destroy()
	self.position 		= nil
	self.rawPosition	= nil
	self.deltaPosition 	= nil	
end

function Touch.GetMask(...)
	local arg = {...}
	local value = 0	

	for i = 1, #arg do		
		local n = TouchBits[arg[i]] or 0
		
		if n ~= 0 then
			value = value + n				
		end
	end	
		
	if value == 0 then value = TouchBits['all'] end
		
	return value
end

UnityEngine.TouchPhase = TouchPhase
UnityEngine.Touch = Touch
setmetatable(Touch, Touch)
return Touch";
        private string LoadLayerMask = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local Layer = Layer
local rawget = rawget
local setmetatable = setmetatable

local LayerMask = {}

LayerMask.__index = function(t,k)
	return rawget(LayerMask, k)	
end

LayerMask.__call = function(t,v)
	return setmetatable({value = value or 0}, LayerMask)
end

function LayerMask.New(value)	
	return setmetatable({value = value or 0}, LayerMask)		
end

function LayerMask:Get()
	return self.value
end

function LayerMask.NameToLayer(name)
	return Layer[name]
end

function LayerMask.GetMask(...)
	local arg = {...}
	local value = 0	

	for i = 1, #arg do		
		local n = LayerMask.NameToLayer(arg[i])
		
		if n ~= nil then
			value = value + 2 ^ n				
		end
	end	
		
	return value
end

UnityEngine.LayerMask = LayerMask
setmetatable(LayerMask, LayerMask)
return LayerMask";
        private string LoadPlane = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local setmetatable = setmetatable
local Mathf = Mathf
local Vector3 = Vector3

local Plane = {}

Plane.__index = function(t,k)
	return rawget(Plane, k)	
end

Plane.__call = function(t,v)
	return Plane.New(v)
end

function Plane.New(normal, d)
	return setmetatable({normal = normal:Normalize(), distance = d}, Plane)	
end

function Plane:Get()
	return self.normal, self.distance
end

function Plane:Raycast(ray)
	local a = Vector3.Dot(ray.direction, self.normal)
    local num2 = -Vector3.Dot(ray.origin, self.normal) - self.distance
	
    if Mathf.Approximately(a, 0) then                   
		return false, 0        
	end
	
    local enter = num2 / a    
	return enter > 0, enter
end

function Plane:SetNormalAndPosition(inNormal, inPoint)    
    self.normal = inNormal:Normalize()
    self.distance = -Vector3.Dot(inNormal, inPoint)
end    

function Plane:Set3Points(a, b, c)    
    self.normal = Vector3.Normalize(Vector3.Cross(b - a, c - a))
    self.distance = -Vector3.Dot(self.normal, a)
end		    

function Plane:GetDistanceToPoint(inPt)    
	return Vector3.Dot(self.normal, inPt) + self.distance
end    

function Plane:GetSide(inPt)    
	return (Vector3.Dot(self.normal, inPt) + self.distance) > 0
end    

function Plane:SameSide(inPt0, inPt1)    
	local distanceToPoint = self:GetDistanceToPoint(inPt0)
	local num2 = self:GetDistanceToPoint(inPt1)
	return (distanceToPoint > 0 and num2 > 0) or (distanceToPoint <= 0 and num2 <= 0)
end    

UnityEngine.Plane = Plane
setmetatable(Plane, Plane)
return Plane";
        private string LoadTime = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local rawget = rawget
local uTime = UnityEngine.Time
local gettime = tolua.gettime

local _Time = 
{	
	deltaTime			= 0,
	fixedDeltaTime 	 	= 0,
	maximumDeltaTime	= 0.3333333,
	fixedTime			= 0,
	frameCount			= 1,	
	realtimeSinceStartup=0,
	time 				= 0,
	timeScale			= 1,
	timeSinceLevelLoad	= 0,
	unscaledDeltaTime	= 0,	
	unscaledTime		= 0,	
}

local _set = {}

function _set.fixedDeltaTime(v)
	_Time.fixedDeltaTime = v
	uTime.fixedDeltaTime = v
end

function _set.maximumDeltaTime(v)
	_Time.maximumDeltaTime = v
	uTime.maximumDeltaTime = v
end

function _set.timeScale(v)
	_Time.timeScale = v
	uTime.timeScale = v
end

function _set.captureFramerate(v)
	_Time.captureFramerate = v
	uTime.captureFramerate = v
end

function _set.timeSinceLevelLoad(v)
	_Time.timeSinceLevelLoad = v
end

_Time.__index = function(t, k)
	local var = rawget(_Time, k)
	
	if var then
		return var
	end

	return uTime.__index(uTime, k)	
end

_Time.__newindex = function(t, k, v)
	local func = rawget(_set, k)

	if func then
		return func(v)
	end

	error(string.format('Property or indexer `UnityEngine.Time.%s' cannot be assigned to (it is read only)', k))	
end

local Time = {}
local counter = 1

function Time:SetDeltaTime(deltaTime, unscaledDeltaTime)	
	local _Time = _Time
	_Time.deltaTime = deltaTime	
	_Time.unscaledDeltaTime = unscaledDeltaTime
	counter = counter - 1

	if counter == 0 and uTime then	
		_Time.time = uTime.time
		_Time.timeSinceLevelLoad = uTime.timeSinceLevelLoad
		_Time.unscaledTime = uTime.unscaledTime
		_Time.realtimeSinceStartup = uTime.realtimeSinceStartup
		_Time.frameCount = uTime.frameCount
		counter = 1000000
	else
		_Time.time = _Time.time + deltaTime
		_Time.realtimeSinceStartup = _Time.realtimeSinceStartup + unscaledDeltaTime
		_Time.timeSinceLevelLoad = _Time.timeSinceLevelLoad + deltaTime	
		_Time.unscaledTime = _Time.unscaledTime + unscaledDeltaTime 
	end		
end

function Time:SetFixedDelta(fixedDeltaTime)	
	_Time.deltaTime = fixedDeltaTime
	_Time.fixedDeltaTime = fixedDeltaTime

	_Time.fixedTime = _Time.fixedTime + fixedDeltaTime
end

function Time:SetFrameCount()
	_Time.frameCount = _Time.frameCount + 1
end

function Time:SetTimeScale(scale)
	local last = _Time.timeScale
	_Time.timeScale = scale
	uTime.timeScale = scale
	return last
end

function Time:GetTimestamp()
	return gettime()
end

UnityEngine.Time = Time
setmetatable(Time, _Time)

if uTime ~= nil then
	_Time.maximumDeltaTime = uTime.maximumDeltaTime	
	_Time.timeScale = uTime.timeScale	
end


return Time";
        private string LoadList = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local setmetatable = setmetatable

local list = {}
list.__index = list

function list:new()
	local t = {length = 0, _prev = 0, _next = 0}
	t._prev = t
	t._next = t
	return setmetatable(t, list)
end

function list:clear()
	self._next = self
	self._prev = self
	self.length = 0
end

function list:push(value)
	--assert(value)
	local node = {value = value, _prev = 0, _next = 0, removed = false}

	self._prev._next = node
	node._next = self
	node._prev = self._prev
	self._prev = node

	self.length = self.length + 1
	return node
end

function list:pushnode(node)
	if not node.removed then return end

	self._prev._next = node
	node._next = self
	node._prev = self._prev
	self._prev = node
	node.removed = false
	self.length = self.length + 1
end

function list:pop()
	local _prev = self._prev
	self:remove(_prev)
	return _prev.value
end

function list:unshift(v)
	local node = {value = v, _prev = 0, _next = 0, removed = false}

	self._next._prev = node
	node._prev = self
	node._next = self._next
	self._next = node

	self.length = self.length + 1
	return node
end

function list:shift()
	local _next = self._next
	self:remove(_next)
	return _next.value
end

function list:remove(iter)
	if iter.removed then return end

	local _prev = iter._prev
	local _next = iter._next
	_next._prev = _prev
	_prev._next = _next
	
	self.length = math.max(0, self.length - 1)
	iter.removed = true
end

function list:find(v, iter)
	iter = iter or self

	repeat
		if v == iter.value then
			return iter
		else
			iter = iter._next
		end		
	until iter == self

	return nil
end

function list:findlast(v, iter)
	iter = iter or self

	repeat
		if v == iter.value then
			return iter
		end

		iter = iter._prev
	until iter == self

	return nil
end

function list:next(iter)
	local _next = iter._next
	if _next ~= self then
		return _next, _next.value
	end

	return nil
end

function list:prev(iter)
	local _prev = iter._prev
	if _prev ~= self then
		return _prev, _prev.value
	end

	return nil
end

function list:erase(v)
	local iter = self:find(v)

	if iter then
		self:remove(iter)		
	end
end

function list:insert(v, iter)	
	if not iter then
		return self:push(v)
	end

	local node = {value = v, _next = 0, _prev = 0, removed = false}

	if iter._next then
		iter._next._prev = node
		node._next = iter._next
	else
		self.last = node
	end

	node._prev = iter
	iter._next = node
	self.length = self.length + 1
	return node
end

function list:head()
	return self._next.value
end

function list:tail()
	return self._prev.value
end

function list:clone()
	local t = list:new()

	for i, v in list.next, self, self do
		t:push(v)
	end

	return t
end

ilist = function(_list) return list.next, _list, _list end
rilist = function(_list) return list.prev, _list, _list end

setmetatable(list, {__call = list.new})
return list";
        private string LoadUTF8 = @"local utf8 = {}

--byte index of the next char after the char at byte index i, followed by a valid flag for the char at byte index i.
--nil if not found. invalid characters are iterated as 1-byte chars.
function utf8.next_raw(s, i)
	if not i then
		if #s == 0 then return nil end
		return 1, true --fake flag (doesn't matter since this flag is not to be taken as full validation)
	end
	if i > #s then return end
	local c = s:byte(i)
	if c >= 0x00 and c <= 0x7F then
		i = i + 1
	elseif c >= 0xC2 and c <= 0xDF then
		i = i + 2
	elseif c >= 0xE0 and c <= 0xEF then
		i = i + 3
	elseif c >= 0xF0 and c <= 0xF4 then
		i = i + 4
	else --invalid
		return i + 1, false
	end
	if i > #s then return end
	return i, true
end

--next() is the generic iterator and can be replaced for different semantics. next_raw() must preserve its semantics.
utf8.next = utf8.next_raw

--iterate chars, returning the byte index where each char starts
function utf8.byte_indices(s, previ)
	return utf8.next, s, previ
end

--number of chars in string
function utf8.len(s)
	assert(s, 'bad argument #1 to 'len' (string expected, got nil)')
	local len = 0
	for _ in utf8.byte_indices(s) do
		len = len + 1
	end
	return len
end

--byte index given char index. nil if the index is outside the string.
function utf8.byte_index(s, target_ci)
	if target_ci < 1 then return end
	local ci = 0
	for i in utf8.byte_indices(s) do
		ci = ci + 1
		if ci == target_ci then
			return i
		end
	end
	assert(target_ci > ci, 'invalid index')
end

--char index given byte index. nil if the index is outside the string.
function utf8.char_index(s, target_i)
	if target_i < 1 or target_i > #s then return end
	local ci = 0
	for i in utf8.byte_indices(s) do
		ci = ci + 1
		if i == target_i then
			return ci
		end
	end
	error('invalid index')
end

--byte index of the prev. char before the char at byte index i, which defaults to #s + 1.
--nil if the index is outside the 2..#s+1 range.
--NOTE: unlike next(), this is a O(N) operation!
function utf8.prev(s, nexti)
	nexti = nexti or #s + 1
	if nexti <= 1 or nexti > #s + 1 then return end
	local lasti, lastvalid = utf8.next(s)
	for i, valid in utf8.byte_indices(s) do
		if i == nexti then
			return lasti, lastvalid
		end
		lasti, lastvalid = i, valid
	end
	if nexti == #s + 1 then
		return lasti, lastvalid
	end
	error('invalid index')
end

--iterate chars in reverse order, returning the byte index where each char starts.
function utf8.byte_indices_reverse(s, nexti)
	if #s < 200 then
		--using prev() is a O(N^2/2) operation, ok for small strings (200 chars need 40,000 iterations)
		return utf8.prev, s, nexti
	else
		--store byte indices in a table and iterate them in reverse.
		--this is 40x slower than byte_indices() but still fast at 2mil chars/second (but eats RAM and makes garbage).
		local t = {}
		for i in utf8.byte_indices(s) do
			if nexti and i >= nexti then break end
			table.insert(t, i)
		end
		local i = #t + 1
		return function()
			i = i - 1
			return t[i]
		end
	end
end

--sub based on char indices, which, unlike with standard string.sub(), can't be negative.
--start_ci can be 1..inf and end_ci can be 0..inf. end_ci can be nil meaning last char.
--if start_ci is out of range or end_ci < start_ci, the empty string is returned.
--if end_ci is out of range, it is considered to be the last position in the string.
function utf8.sub(s, start_ci, end_ci)
	--assert for positive indices because we might implement negative indices in the future.
	assert(start_ci >= 1)
	assert(not end_ci or end_ci >= 0)
	local ci = 0
	local start_i, end_i
	for i in utf8.byte_indices(s) do
		ci = ci + 1
		if ci == start_ci then
			start_i = i
		end
		if ci == end_ci then
			end_i = i
		end
	end
	if not start_i then
		assert(start_ci > ci, 'invalid index')
		return ''
	end
	if end_ci and not end_i then
		if end_ci < start_ci then
			return ''
		end
		assert(end_ci > ci, 'invalid index')
	end
	return s:sub(start_i, end_i and end_i - 1)
end

--check if a string contains a substring at byte index i without making garbage.
--nil if the index is out of range. true if searching for the empty string.
function utf8.contains(s, i, sub)
	if i < 1 or i > #s then return nil end
	for si = 1, #sub do
		if s:byte(i + si - 1) ~= sub:byte(si) then
			return false
		end
	end
	return true
end

--count the number of occurences of a substring in a string. the substring cannot be the empty string.
function utf8.count(s, sub)
	assert(#sub > 0)
	local count = 0
	local i = 1
	while i do
		if utf8.contains(s, i, sub) then
			count = count + 1
			i = i + #sub
			if i > #s then break end
		else
			i = utf8.next(s, i)
		end
	end
	return count
end

--utf8 validation and sanitization

--check if there's a valid utf8 codepoint at byte index i. valid ranges for each utf8 byte are:
-- byte  1          2           3          4
--------------------------------------------
-- 00 - 7F
-- C2 - DF    80 - BF
-- E0         A0 - BF     80 - BF
-- E1 - EC    80 - BF     80 - BF
-- ED         80 - 9F     80 - BF
-- EE - EF    80 - BF     80 - BF
-- F0         90 - BF     80 - BF    80 - BF
-- F1 - F3    80 - BF     80 - BF    80 - BF
-- F4         80 - 8F     80 - BF    80 - BF
function utf8.isvalid(s, i)
	local c = s:byte(i)
	if not c then
		return false
	elseif c >= 0x00 and c <= 0x7F then
		return true
	elseif c >= 0xC2 and c <= 0xDF then
		local c2 = s:byte(i + 1)
		return c2 and c2 >= 0x80 and c2 <= 0xBF
	elseif c >= 0xE0 and c <= 0xEF then
		local c2 = s:byte(i + 1)
		local c3 = s:byte(i + 2)
		if c == 0xE0 then
			return c2 and c3 and
				c2 >= 0xA0 and c2 <= 0xBF and
				c3 >= 0x80 and c3 <= 0xBF
		elseif c >= 0xE1 and c <= 0xEC then
			return c2 and c3 and
				c2 >= 0x80 and c2 <= 0xBF and
				c3 >= 0x80 and c3 <= 0xBF
		elseif c == 0xED then
			return c2 and c3 and
				c2 >= 0x80 and c2 <= 0x9F and
				c3 >= 0x80 and c3 <= 0xBF
		elseif c >= 0xEE and c <= 0xEF then
			if c == 0xEF and c2 == 0xBF and (c3 == 0xBE or c3 == 0xBF) then
				return false --uFFFE and uFFFF non-characters
			end
			return c2 and c3 and
				c2 >= 0x80 and c2 <= 0xBF and
				c3 >= 0x80 and c3 <= 0xBF
		end
	elseif c >= 0xF0 and c <= 0xF4 then
		local c2 = s:byte(i + 1)
		local c3 = s:byte(i + 2)
		local c4 = s:byte(i + 3)
		if c == 0xF0 then
			return c2 and c3 and c4 and
				c2 >= 0x90 and c2 <= 0xBF and
				c3 >= 0x80 and c3 <= 0xBF and
				c4 >= 0x80 and c4 <= 0xBF
		elseif c >= 0xF1 and c <= 0xF3 then
			return c2 and c3 and c4 and
				c2 >= 0x80 and c2 <= 0xBF and
				c3 >= 0x80 and c3 <= 0xBF and
				c4 >= 0x80 and c4 <= 0xBF
		elseif c == 0xF4 then
			return c2 and c3 and c4 and
				c2 >= 0x80 and c2 <= 0x8F and
				c3 >= 0x80 and c3 <= 0xBF and
				c4 >= 0x80 and c4 <= 0xBF
		end
	end
	return false
end

--byte index of the next valid utf8 char after the char at byte index i.
--nil if indices go out of range. invalid characters are skipped.
function utf8.next_valid(s, i)
	local valid
	i, valid = utf8.next_raw(s, i)
	while i and (not valid or not utf8.isvalid(s, i)) do
		i, valid = utf8.next(s, i)
	end
	return i
end

--iterate valid chars, returning the byte index where each char starts
function utf8.valid_byte_indices(s)
	return utf8.next_valid, s
end

--assert that a string only contains valid utf8 characters
function utf8.validate(s)
	for i, valid in utf8.byte_indices(s) do
		if not valid or not utf8.isvalid(s, i) then
			error(string.format('invalid utf8 char at #%d', i))
		end
	end
end

local function table_lookup(s, i, j, t)
	return t[s:sub(i, j)]
end

--replace characters in string based on a function f(s, i, j, ...) -> replacement_string | nil
function utf8.replace(s, f, ...)
	if type(f) == 'table' then
		return utf8.replace(s, table_lookup, f)
	end
	if s == '' then
		return s
	end
	local t = {}
	local lasti = 1
	for i in utf8.byte_indices(s) do
		local nexti = utf8.next(s, i) or #s + 1
		local repl = f(s, i, nexti - 1, ...)
		if repl then
			table.insert(t, s:sub(lasti, i - 1))
			table.insert(t, repl)
			lasti = nexti
		end
	end
	table.insert(t, s:sub(lasti))
	return table.concat(t)
end

local function replace_invalid(s, i, j, repl_char)
	if not utf8.isvalid(s, i) then
		return repl_char
	end
end

--replace invalid utf8 chars with a replacement char
function utf8.sanitize(s, repl_char)
	repl_char = repl_char or '�' --\uFFFD
	return utf8.replace(s, replace_invalid, repl_char)
end

return utf8
";
        private string LoadEvent = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------

local setmetatable = setmetatable
local xpcall = xpcall
local pcall = pcall
local assert = assert
local rawget = rawget
local error = error
local print = print
local maxn = table.maxn
local traceback = tolua.traceback
local ilist = ilist

local _xpcall = {}

_xpcall.__call = function(self, ...)	
	if jit then
		if nil == self.obj then
			return xpcall(self.func, traceback, ...)					
		else		
			return xpcall(self.func, traceback, self.obj, ...)					
		end
	else
		local args = {...}

		if nil == self.obj then
			local func = function() self.func(unpack(args, 1, maxn(args))) end
			return xpcall(func, traceback)					
		else		
			local func = function() self.func(self.obj, unpack(args, 1, maxn(args))) end
			return xpcall(func, traceback)
		end
	end	
end

_xpcall.__eq = function(lhs, rhs)
	return lhs.func == rhs.func and lhs.obj == rhs.obj
end

local function xfunctor(func, obj)	
	return setmetatable({func = func, obj = obj}, _xpcall)			
end

local _pcall = {}

_pcall.__call = function(self, ...)
	if nil == self.obj then
		return pcall(self.func, ...)					
	else		
		return pcall(self.func, self.obj, ...)					
	end	
end

_pcall.__eq = function(lhs, rhs)
	return lhs.func == rhs.func and lhs.obj == rhs.obj
end

local function functor(func, obj)	
	return setmetatable({func = func, obj = obj}, _pcall)			
end

local _event = {}
_event.__index = _event

--废弃
function _event:Add(func, obj)
	assert(func)		

	if self.keepSafe then			
		func = xfunctor(func, obj)
	else
		func = functor(func, obj)
	end	

	if self.lock then
		local node = {value = func, _prev = 0, _next = 0, removed = true}
		table.insert(self.opList, function() self.list:pushnode(node) end)			
		return node
	else
		return self.list:push(func)
	end	
end

--废弃
function _event:Remove(func, obj)	
	for i, v in ilist(self.list) do							
		if v.func == func and v.obj == obj then
			if self.lock then
				table.insert(self.opList, function() self.list:remove(i) end)				
			else
				self.list:remove(i)
			end
			break
		end
	end		
end

function _event:CreateListener(func, obj)
	if self.keepSafe then			
		func = xfunctor(func, obj)
	else
		func = functor(func, obj)
	end	
	
	return {value = func, _prev = 0, _next = 0, removed = true}		
end

function _event:AddListener(handle)	
	assert(handle)

	if self.lock then		
		table.insert(self.opList, function() self.list:pushnode(handle) end)		
	else
		self.list:pushnode(handle)
	end	
end

function _event:RemoveListener(handle)	
	assert(handle)	

	if self.lock then		
		table.insert(self.opList, function() self.list:remove(handle) end)				
	else
		self.list:remove(handle)
	end
end

function _event:Count()
	return self.list.length
end	

function _event:Clear()
	self.list:clear()
	self.opList = {}	
	self.lock = false
	self.keepSafe = false
	self.current = nil
end

function _event:Dump()
	local count = 0
	
	for _, v in ilist(self.list) do
		if v.obj then
			print('update function:', v.func, 'object name:', v.obj.name)
		else
			print('update function: ', v.func)
		end
		
		count = count + 1
	end
	
	print('all function is:', count)
end

_event.__call = function(self, ...)			
	local _list = self.list	
	self.lock = true
	local ilist = ilist				

	for i, f in ilist(_list) do		
		self.current = i						
		local flag, msg = f(...)
		
		if not flag then			
			_list:remove(i)			
			self.lock = false		
			error(msg)				
		end
	end	

	local opList = self.opList	
	self.lock = false		

	for i, op in ipairs(opList) do									
		op()
		opList[i] = nil
	end
end

function event(name, safe)
	safe = safe or false
	return setmetatable({name = name, keepSafe = safe, lock = false, opList = {}, list = list:new()}, _event)				
end

UpdateBeat 		= event('Update', true)
LateUpdateBeat	= event('LateUpdate', true)
FixedUpdateBeat	= event('FixedUpdate', true)
CoUpdateBeat	= event('CoUpdate')				--只在协同使用

local Time = Time
local UpdateBeat = UpdateBeat
local LateUpdateBeat = LateUpdateBeat
local FixedUpdateBeat = FixedUpdateBeat
local CoUpdateBeat = CoUpdateBeat

--逻辑update
function Update(deltaTime, unscaledDeltaTime)
	Time:SetDeltaTime(deltaTime, unscaledDeltaTime)				
	UpdateBeat()	
end

function LateUpdate()	
	LateUpdateBeat()		
	CoUpdateBeat()		
	Time:SetFrameCount()		
end

--物理update
function FixedUpdate(fixedDeltaTime)
	Time:SetFixedDelta(fixedDeltaTime)
	FixedUpdateBeat()
end

function PrintEvents()
	UpdateBeat:Dump()
	FixedUpdateBeat:Dump()
end";
        private string LoadTypeof = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local type = type
local types = {}
local _typeof = tolua.typeof
local _findtype = tolua.findtype

function typeof(obj)
	local t = type(obj)
	local ret = nil
	
	if t == 'table' then
		ret = types[obj]
		
		if ret == nil then
			ret = _typeof(obj)
			types[obj] = ret
		end		
  	elseif t == 'string' then
  		ret = types[obj]

  		if ret == nil then
  			ret = _findtype(obj)
  			types[obj] = ret
  		end	
  	else
  		error(debug.traceback('attemp to call typeof on type '..t))
	end
	
	return ret
end";
        private string LoadSlot = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local setmetatable = setmetatable

local _slot = {}
setmetatable(_slot, _slot)	

_slot.__call = function(self, ...)			
	if nil == self.obj then
		return self.func(...)			
	else		
		return self.func(self.obj, ...)			
	end
end

_slot.__eq = function (lhs, rhs)
	return lhs.func == rhs.func and lhs.obj == rhs.obj
end

--可用于 Timer 定时器回调函数. 例如Timer.New(slot(self.func, self))
function slot(func, obj)	
	return setmetatable({func = func, obj = obj}, _slot)			
end";
        private string LoadTimer = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local setmetatable = setmetatable
local UpdateBeat = UpdateBeat
local CoUpdateBeat = CoUpdateBeat
local Time = Time

Timer = {}

local Timer = Timer
local mt = {__index = Timer}

--unscaled false 采用deltaTime计时，true 采用 unscaledDeltaTime计时
function Timer.New(func, duration, loop, unscaled)
	unscaled = unscaled or false and true	
	loop = loop or 1
	return setmetatable({func = func, duration = duration, time = duration, loop = loop, unscaled = unscaled, running = false}, mt)	
end

function Timer:Start()
	self.running = true

	if not self.handle then
		self.handle = UpdateBeat:CreateListener(self.Update, self)
	end

	UpdateBeat:AddListener(self.handle)	
end

function Timer:Reset(func, duration, loop, unscaled)
	self.duration 	= duration
	self.loop		= loop or 1
	self.unscaled	= unscaled
	self.func		= func
	self.time		= duration		
end

function Timer:Stop()
	self.running = false

	if self.handle then
		UpdateBeat:RemoveListener(self.handle)	
	end
end

function Timer:Update()
	if not self.running then
		return
	end

	local delta = self.unscaled and Time.unscaledDeltaTime or Time.deltaTime	
	self.time = self.time - delta
	
	if self.time <= 0 then
		self.func()
		
		if self.loop > 0 then
			self.loop = self.loop - 1
			self.time = self.time + self.duration
		end
		
		if self.loop == 0 then
			self:Stop()
		elseif self.loop < 0 then
			self.time = self.time + self.duration
		end
	end
end

--给协同使用的帧计数timer
FrameTimer = {}

local FrameTimer = FrameTimer
local mt2 = {__index = FrameTimer}

function FrameTimer.New(func, count, loop)	
	local c = Time.frameCount + count
	loop = loop or 1
	return setmetatable({func = func, loop = loop, duration = count, count = c, running = false}, mt2)		
end

function FrameTimer:Reset(func, count, loop)
	self.func = func
	self.duration = count
	self.loop = loop
	self.count = Time.frameCount + count	
end

function FrameTimer:Start()		
	if not self.handle then
		self.handle = CoUpdateBeat:CreateListener(self.Update, self)
	end
	
	CoUpdateBeat:AddListener(self.handle)	
	self.running = true
end

function FrameTimer:Stop()	
	self.running = false

	if self.handle then
		CoUpdateBeat:RemoveListener(self.handle)	
	end
end

function FrameTimer:Update()	
	if not self.running then
		return
	end

	if Time.frameCount >= self.count then
		self.func()	
		
		if self.loop > 0 then
			self.loop = self.loop - 1
		end
		
		if self.loop == 0 then
			self:Stop()
		else
			self.count = Time.frameCount + self.duration
		end
	end
end

CoTimer = {}

local CoTimer = CoTimer
local mt3 = {__index = CoTimer}

function CoTimer.New(func, duration, loop)	
	loop = loop or 1
	return setmetatable({duration = duration, loop = loop, func = func, time = duration, running = false}, mt3)			
end

function CoTimer:Start()		
	if not self.handle then	
		self.handle = CoUpdateBeat:CreateListener(self.Update, self)
	end
	
	self.running = true
	CoUpdateBeat:AddListener(self.handle)	
end

function CoTimer:Reset(func, duration, loop)
	self.duration 	= duration
	self.loop		= loop or 1	
	self.func		= func
	self.time		= duration		
end

function CoTimer:Stop()
	self.running = false

	if self.handle then
		CoUpdateBeat:RemoveListener(self.handle)	
	end
end

function CoTimer:Update()	
	if not self.running then
		return
	end

	if self.time <= 0 then
		self.func()		
		
		if self.loop > 0 then
			self.loop = self.loop - 1
			self.time = self.time + self.duration
		end
		
		if self.loop == 0 then
			self:Stop()
		elseif self.loop < 0 then
			self.time = self.time + self.duration
		end
	end
	
	self.time = self.time - Time.deltaTime
end";
        private string LoadCoroutine = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local create = coroutine.create
local running = coroutine.running
local resume = coroutine.resume
local yield = coroutine.yield
local error = error
local unpack = unpack
local debug = debug
local FrameTimer = FrameTimer
local CoTimer = CoTimer

local comap = {}
local pool = {}
setmetatable(comap, {__mode = 'kv'})

function coroutine.start(f, ...)	
	local co = create(f)
	
	if running() == nil then
		local flag, msg = resume(co, ...)
	
		if not flag then					
			error(debug.traceback(co, msg))
		end					
	else
		local args = {...}
		local timer = nil		
		
		local action = function()												
			comap[co] = nil
			timer.func = nil
			local flag, msg = resume(co, unpack(args, 1, table.maxn(args)))						
			table.insert(pool, timer)
	
			if not flag then	
				timer:Stop()														
				error(debug.traceback(co, msg))						
			end		
		end
			
		if #pool > 0 then
			timer = table.remove(pool)
			timer:Reset(action, 0, 1)
		else
			timer = FrameTimer.New(action, 0, 1)
		end
		
		comap[co] = timer
		timer:Start()		
	end

	return co
end

function coroutine.wait(t, co, ...)
	local args = {...}
	co = co or running()		
	local timer = nil
		
	local action = function()		
		comap[co] = nil		
		timer.func = nil
		local flag, msg = resume(co, unpack(args, 1, table.maxn(args)))
		
		if not flag then	
			timer:Stop()						
			error(debug.traceback(co, msg))			
			return
		end
	end
	
	timer = CoTimer.New(action, t, 1)
	comap[co] = timer	
	timer:Start()
	return yield()
end

function coroutine.step(t, co, ...)
	local args = {...}
	co = co or running()		
	local timer = nil
	
	local action = function()	
		comap[co] = nil					
		timer.func = nil
		local flag, msg = resume(co, unpack(args, 1, table.maxn(args)))
		table.insert(pool, timer)
	
		if not flag then	
			timer:Stop()																			
			error(debug.traceback(co, msg))
			return	
		end		
	end
				
	if #pool > 0 then
		timer = table.remove(pool)
		timer:Reset(action, t or 1, 1)
	else
		timer = FrameTimer.New(action, t or 1, 1)
	end

	comap[co] = timer
	timer:Start()
	return yield()
end

function coroutine.www(www, co)			
	co = co or running()			
	local timer = nil			
			
	local action = function()				
		if not www.isDone then		
			return		
		end		
				
		comap[co] = nil
		timer:Stop()		
		timer.func = nil
		local flag, msg = resume(co)			
		table.insert(pool, timer)	
			
		if not flag then												
			error(debug.traceback(co, msg))			
			return			
		end				
	end		
				
	if #pool > 0 then
		timer = table.remove(pool)
		timer:Reset(action, 1, -1)
	else	
		timer = FrameTimer.New(action, 1, -1)	
	end
	comap[co] = timer	
 	timer:Start()
 	return yield()
end

function coroutine.stop(co)
 	local timer = comap[co] 	 	

 	if timer ~= nil then
 		comap[co] = nil
 		timer:Stop()  	
 		timer.func = nil	
 	end
end";
        private string LoadValueType = @"--------------------------------------------------------------------------------
--      Copyright (c) 2015 - 2016 , 蒙占志(topameng) topameng@gmail.com
--      All rights reserved.
--      Use, modification and distribution are subject to the 'MIT License'
--------------------------------------------------------------------------------
local ValueType = {}

ValueType[Vector3] 		= 1
ValueType[Quaternion]	= 2
ValueType[Vector2]		= 3
ValueType[Color]		= 4
ValueType[Vector4]		= 5
ValueType[Ray]			= 6
ValueType[Bounds]		= 7
ValueType[Touch]		= 8
ValueType[LayerMask]	= 9
ValueType[RaycastHit]	= 10
ValueType[int64]		= 11
ValueType[uint64]		= 12

local function GetValueType()	
	local getmetatable = getmetatable
	local ValueType = ValueType

	return function(udata)
		local meta = getmetatable(udata)	

		if meta == nil then
			return 0
		end

		return ValueType[meta] or 0
	end
end

function AddValueType(table, type)
	ValueType[table] = type
end

GetLuaValueType = GetValueType() ";
        private string LoadBindingFlags = @"if System.Reflection == nil then    
    System.Reflection = {}
end

local function GetMask(...)
    local arg = {...}
    local value = 0 

    for i = 1, #arg do              
        value = value + arg[i]    
    end 
        
    return value
end

local BindingFlags = 
{
    Default = 0,
    IgnoreCase = 1,
    DeclaredOnly = 2,
    Instance = 4,
    Static = 8,
    Public = 16,
    NonPublic = 32,
    FlattenHierarchy = 64,
    InvokeMethod = 256,
    CreateInstance = 512,
    GetField = 1024,
    SetField = 2048,
    GetProperty = 4096,
    SetProperty = 8192,
    PutDispProperty = 16384,
    PutRefDispProperty = 32768,
    ExactBinding = 65536,
    SuppressChangeType = 131072,
    OptionalParamBinding = 262144,
    IgnoreReturn = 16777216,
}

System.Reflection.BindingFlags = BindingFlags
System.Reflection.BindingFlags.GetMask = GetMask

return BindingFlags";
    }
}