const {
    System,
    Mono,
    MonoMod,
} = dotnet;

export function using<T extends { Dispose() }>(instance: T, callback: (instance: T) => void) {
    callback(instance);
    instance.Dispose();
}

export default function () {
    console.log('Running JavaScript modifications...');

    // const assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly("Mono.Cecil.Js.dll");
    // if(assembly) {
    //     console.log('test');
    // }

    System.Console.Write('Are you awesome? [Y/n]: ')
    if(System.Console.ReadLine().toLowerCase() != 'y') {
        console.log('You arent awesome, how disappointing');
        return;
    }

    const zipPath = "terraria-server-1405.zip";
    if (!System.IO.File.Exists(zipPath)) {
        console.log('Downloading server...');
        using(new System.Net.Http.HttpClient(), client => {
            var data = client.GetByteArrayAsync('https://terraria.org/system/dedicated_servers/archives/000/000/039/original/terraria-server-1405.zip').Result;
            System.IO.File.WriteAllBytes(zipPath, data);
        });
        console.log('Done');
    } else console.log(`${zipPath} already exists`);

    var directory = System.IO.Path.GetFileNameWithoutExtension(zipPath);
    var info = new System.IO.DirectoryInfo(directory);
    console.log(`Extracting to ${directory}`);

    if (info.Exists) info.Delete(true);

    info.Refresh();

    if (!info.Exists || info.GetDirectories().Length == 0)
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, directory);

    const pathIn = System.IO.Path.Combine(directory, "1405", "Windows", "TerrariaServer.exe");

    console.log(`Extracting embedded binaries for assembly resolution...`);
    var extractor = new Mono.Cecil.Js.ResourceExtractor();
    var embeddedResourcesDir = extractor.Extract(pathIn);

    using(new Mono.Cecil.Js.JsModder(), mm => {
        mm.InputPath = pathIn;
        mm.OutputPath = "OTAPI.dll";
        mm.MissingDependencyThrow = false;
        mm.GACPaths = host.newArr(System.String, 0);
        mm.DefaultAssemblyResolver.AddSearchDirectory(embeddedResourcesDir);

        mm.Read();

        mm.MapDependencies();
        mm.AutoPatch();
        mm.Write();
    });
}