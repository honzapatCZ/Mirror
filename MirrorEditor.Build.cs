using Flax.Build;
using Flax.Build.NativeCpp;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class MirrorEditor : GameEditorModule
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();

        // C#-only scripting
        BuildNativeCode = false;

    }

    /// <inheritdoc />
    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        options.SourcePaths.Clear();
        options.SourceFiles.AddRange(Directory.GetFiles(Path.Combine(FolderPath, "Assets", "Mirror", "Editor"), "*.*", SearchOption.AllDirectories));
        
        options.ScriptingAPI.FileReferences.Add(Path.Combine(FolderPath, "Assets", "Mirror", "Editor", "Plugins", "Mono.Cecil", "Mono.CecilX.dll"));
        options.ScriptingAPI.FileReferences.Add(Path.Combine(FolderPath, "Assets", "Mirror", "Editor", "Plugins", "Mono.Cecil", "Mono.CecilX.Mdb.dll"));
        options.ScriptingAPI.FileReferences.Add(Path.Combine(FolderPath, "Assets", "Mirror", "Editor", "Plugins", "Mono.Cecil", "Mono.CecilX.Pdb.dll"));
        options.ScriptingAPI.FileReferences.Add(Path.Combine(FolderPath, "Assets", "Mirror", "Editor", "Plugins", "Mono.Cecil", "Mono.CecilX.Rocks.dll"));

        options.PublicDependencies.Add("Mirror");
        /*
        string output = "";
        foreach(string name in options.SourceFiles)
        {
            output += name.Replace("D:\\Enginy\\Flax\\FlaxTest\\Source\\Mirror", "") + " ";
        }

        Flax.Build.Log.Info(output);
        */
        // Here you can modify the build options for your game module
        // To reference another module use: options.PublicDependencies.Add("Audio");
        // To add C++ define use: options.PublicDefinitions.Add("COMPILE_WITH_FLAX");
        // To learn more see scripting documentation.
    }
}
