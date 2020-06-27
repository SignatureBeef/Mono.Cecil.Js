namespace Mono.Cecil.Js.Javascript
{
    // this is to get around ClearScript not handling AssemblyResolver correctly, it doesnt detect the overloaded instance so any method call to DefaultAssemblyResolver throws even if it is an instance of DefaultAssemblyResolver
    // e.g. : 'TypeError: mm.AssemblyResolver.AddSearchDirectory is not a function'
    public class JsModder : MonoMod.MonoModder
    {
        public DefaultAssemblyResolver DefaultAssemblyResolver
            => this.AssemblyResolver as DefaultAssemblyResolver;
    }
}
