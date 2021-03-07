using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mirror.Weaver
{
    public static class CompilationFinishedHook
    {
        const string MirrorRuntimeAssemblyName = "Mirror";
        const string MirrorWeaverAssemblyName = "Mirror.Weaver";

        // delegate for subscription to Weaver debug messages
        public static Action<string> OnWeaverMessage;
        // delegate for subscription to Weaver warning messages
        public static Action<string> OnWeaverWarning;
        // delete for subscription to Weaver error messages
        public static Action<string> OnWeaverError;

        // controls whether we weave any assemblies when CompilationPipeline delegates are invoked
        public static bool WeaverEnabled { get; set; }
        // controls weather Weaver errors are reported direct to the Unity console (tests enable this)
        public static bool UnityLogEnabled = true;

        // warning message handler that also calls OnWarningMethod delegate
        static void HandleWarning(string msg)
        {
            if (UnityLogEnabled) Console.WriteLine(msg);
            if (OnWeaverWarning != null) OnWeaverWarning.Invoke(msg);
        }

        // error message handler that also calls OnErrorMethod delegate
        static void HandleError(string msg)
        {
            if (UnityLogEnabled) Console.WriteLine(msg);
            if (OnWeaverError != null) OnWeaverError.Invoke(msg);
        }

        //[InitializeOnLoadMethod]
        public static void OnInitializeOnLoad()
        {
            /*
            ScriptsBuilder.CompilationSuccess += ()=> { OnCompilationFinished(); };
            
            //CompilationPipeline.assemblyCompilationFinished += OnCompilationFinished;

            // We only need to run this once per session
            // after that, all assemblies will be weaved by the event
            
            if (!SessionState.GetBool("MIRROR_WEAVED", false))
            {
                // reset session flag
                SessionState.SetBool("MIRROR_WEAVED", true);
                SessionState.SetBool("MIRROR_WEAVE_SUCCESS", true);

                WeaveExistingAssemblies();
            }
            */
        }

        public static void WeaveExistingAssemblies()
        {
            foreach (UnityAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (File.Exists(assembly.outputPath))
                {
                    OnCompilationFinished(assembly.outputPath);
                }
            }
            /*
#if UNITY_2019_3_OR_NEWER
            EditorUtility.RequestScriptReload();
#else
            UnityEditorInternal.InternalEditorUtility.RequestScriptReload();
#endif*/
        }

        static string FindMirrorRuntime()
        {
            foreach (UnityAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == MirrorRuntimeAssemblyName)
                {
                    return assembly.outputPath;
                }
            }
            return "";
        }
        /*
        static bool CompilerMessagesContainError(CompilerMessage[] messages)
        {
            return messages.Any(msg => msg.type == CompilerMessageType.Error);
        }
        */
        static void OnCompilationFinished(string assemblyPath)
        {
            // Do nothing if there were compile errors on the target
            
            /*
            if (CompilerMessagesContainError(messages))
            {
                Debug.Log("Weaver: stop because compile errors on target");
                return;
            }
            */

            // Should not run on the editor only assemblies
            if (assemblyPath.Contains("-Editor") || assemblyPath.Contains(".Editor"))
            {
                return;
            }

            // don't weave mirror files
            string assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            if (assemblyName == MirrorRuntimeAssemblyName || assemblyName == MirrorWeaverAssemblyName)
            {
                return;
            }

            // find Mirror.dll
            string mirrorRuntimeDll = FindMirrorRuntime();
            if (string.IsNullOrEmpty(mirrorRuntimeDll))
            {
                Console.WriteLine("Failed to find Mirror runtime assembly");
                return;
            }
            if (!File.Exists(mirrorRuntimeDll))
            {
                // this is normal, it happens with any assembly that is built before mirror
                // such as unity packages or your own assemblies
                // those don't need to be weaved
                // if any assembly depends on mirror, then it will be built after
                return;
            }

            // find UnityEngine.CoreModule.dll
            string unityEngineCoreModuleDLL = UnityEditorInternal.InternalEditorUtility.GetEngineCoreModuleAssemblyPath();
            if (string.IsNullOrEmpty(unityEngineCoreModuleDLL))
            {
                Console.WriteLine("Failed to find UnityEngine assembly");
                return;
            }

            HashSet<string> dependencyPaths = GetDependecyPaths(assemblyPath);
            dependencyPaths.Add(Path.GetDirectoryName(mirrorRuntimeDll));
            dependencyPaths.Add(Path.GetDirectoryName(unityEngineCoreModuleDLL));
            Log.WarningMethod = HandleWarning;
            Log.ErrorMethod = HandleError;

            if (!Weaver.WeaveAssembly(assemblyPath, dependencyPaths.ToArray()))
            {
                // Set false...will be checked in \Editor\EnterPlayModeSettingsCheck.CheckSuccessfulWeave()
                //SessionState.SetBool("MIRROR_WEAVE_SUCCESS", false);
                if (UnityLogEnabled) 
                    Console.WriteLine("Weaving failed for: " + assemblyPath);
            }
        }

        static HashSet<string> GetDependecyPaths(string assemblyPath)
        {
            // build directory list for later asm/symbol resolving using CompilationPipeline refs
            HashSet<string> dependencyPaths = new HashSet<string>
            {
                Path.GetDirectoryName(assemblyPath)
            };
            foreach (UnityAssembly unityAsm in CompilationPipeline.GetAssemblies())
            {
                if (unityAsm.outputPath != assemblyPath)
                    continue;

                foreach (string unityAsmRef in unityAsm.compiledAssemblyReferences)
                {
                    dependencyPaths.Add(Path.GetDirectoryName(unityAsmRef));
                }
            }

            return dependencyPaths;
        }
    }
}