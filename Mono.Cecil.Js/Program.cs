using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using System.IO;
using System.Linq;

namespace Mono.Cecil.Js
{
    public class ResourceExtractor
    {
        public string Extract(string inputFile)
        {
            var extractionFolder = Path.Combine(Path.GetDirectoryName(inputFile), "EmbeddedResources");
            using (var asmms = new MemoryStream(File.ReadAllBytes(inputFile)))
            {
                var def = AssemblyDefinition.ReadAssembly(asmms);

                if (Directory.Exists(extractionFolder)) Directory.Delete(extractionFolder, true);
                Directory.CreateDirectory(extractionFolder);

                foreach (var module in def.Modules)
                {
                    if (module.HasResources)
                    {
                        foreach (var resource in module.Resources.ToArray())
                        {
                            if (resource.ResourceType == ResourceType.Embedded)
                            {
                                var er = resource as EmbeddedResource;
                                var data = er.GetResourceData();

                                if (data.Length > 2)
                                {
                                    bool is_pe = data.Take(2).SequenceEqual(new byte[] { 77, 90 }); // MZ
                                    if (is_pe)
                                    {
                                        var ms = new MemoryStream(data);
                                        var asm = AssemblyDefinition.ReadAssembly(ms);

                                        File.WriteAllBytes(Path.Combine(extractionFolder, $"{asm.Name.Name}.dll"), data);
                                        module.Resources.Remove(resource);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return extractionFolder;
        }
    }

    public class Console
    {
        public void log(params object[] args)
        {
            foreach (var arg in args)
                System.Console.WriteLine(arg);
        }
    }

    // this is to get around ClearScript not handling AssemblyResolver correctly, it doesnt detect the overloaded instance so any method call to DefaultAssemblyResolver throws even if it is an instance of DefaultAssemblyResolver
    // e.g. : 'TypeError: mm.AssemblyResolver.AddSearchDirectory is not a function'
    public class JsModder : MonoMod.MonoModder
    {
        public DefaultAssemblyResolver DefaultAssemblyResolver
            => this.AssemblyResolver as DefaultAssemblyResolver;
    }

    class Program
    {
        static void Main(string[] args)
        {
            using (var typeGen = new TypingsGenerator.TypingsGenerator())
            {
                typeGen.AddAssembly(typeof(Mono.Cecil.AssemblyDefinition).Assembly);
                typeGen.AddAssembly(typeof(MonoMod.MonoModder).Assembly);
                typeGen.AddType<System.Net.Http.HttpClient>();
                typeGen.AddType(typeof(System.IO.File));
                typeGen.AddType(typeof(System.IO.Directory));
                typeGen.AddType(typeof(System.Console));
                typeGen.AddType(typeof(System.IO.Path));
                typeGen.AddType(typeof(System.IO.Compression.ZipFile));
                typeGen.AddType(typeof(System.IO.DirectoryInfo));
                typeGen.AddType<ResourceExtractor>();
                typeGen.AddType<JsModder>();
                typeGen.AddType<string>();
                typeGen.AddType<System.IDisposable>();

                typeGen.Write();

                using (var engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDynamicModuleImports | V8ScriptEngineFlags.EnableDebugging))
                {
                    engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                    engine.AddHostObject("console", new Console());
                    engine.AddHostObject("host", new HostFunctions());

                    var typeCollection = new HostTypeCollection("mscorlib", "System", "System.Core", "System.Console", "System.Net.Http",
                        typeof(System.IO.Compression.ZipFile).Assembly.GetName().Name,
                        typeof(Program).Assembly.GetName().Name,
                        "Mono.Cecil", "MonoMod");
                    engine.AddHostObject("dotnet", typeCollection);

                    foreach (var directory in Directory.EnumerateDirectories("scripts"))
                    {
                        var moduleFolder = Path.Combine(Directory.GetCurrentDirectory(), directory, "dist");
                        engine.DocumentSettings.SearchPath = moduleFolder;

                        engine.Execute(new DocumentInfo()
                        {
                            Category = ModuleCategory.Standard
                        }, File.ReadAllText(Path.Combine(moduleFolder, "index.js")));
                    }
                }
            }
        }
    }
}
