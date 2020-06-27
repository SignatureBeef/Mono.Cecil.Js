namespace Mono.Cecil.Js.Javascript
{
    public class JsConsole
    {
        public void log(params object[] args)
        {
            if (args.Length > 0)
                foreach (var arg in args)
                    System.Console.WriteLine(arg);
            else System.Console.WriteLine();
        }
    }
}
