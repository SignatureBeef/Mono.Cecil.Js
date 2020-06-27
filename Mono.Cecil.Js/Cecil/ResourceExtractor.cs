using System;
using System.IO;
using System.Linq;

namespace Mono.Cecil.Js.Cecil
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
}
