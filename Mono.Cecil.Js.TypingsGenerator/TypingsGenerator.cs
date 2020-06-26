using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Mono.Cecil.Js.TypingsGenerator
{
    public class TypingsGenerator : IDisposable
    {
        private bool disposedValue;

        private List<Type> types = new List<Type>();

        public TypingsGenerator()
        {

        }

        public void AddAssembly(System.Reflection.Assembly assembly)
        {
            Type[] types = null;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                types = rtle.Types;
            }

            var publicTypes = types.Where(t => t.IsPublic);

            foreach (var type in publicTypes)
                AddType(type);
        }

        public void AddType(Type type)
        {
            var typeName = type.Name;
            if (type.IsGenericType && type.Name.LastIndexOf('`') > -1)
            {
                typeName = type.Name.Substring(0, type.Name.LastIndexOf('`'));
            }

            if (type.FullName != null && type.FullName != "System.Object"
                && !this.types.Any(t => t.FullName == type.FullName)
                && !(type.IsGenericType && type.GetGenericArguments().Count(a => a.FullName != null) > 0) // exclude overloaded types
                && !type.IsByRef
                && !type.IsArray
                && !type.IsPointer
                && !type.IsNested
            )
            {
                this.types.Add(type);

                foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method.ReturnType != type)
                    {
                        AddType(method.ReturnType);
                    }

                    foreach (var prm in method.GetParameters())
                    {
                        if (prm.ParameterType != type)
                        {
                            AddType(prm.ParameterType);
                        }
                    }
                }

                if (type.BaseType != null)
                {
                    AddType(type.BaseType);
                }
            }
        }
        public void AddType<TType>() => AddType(typeof(TType));

        private string GetJsType(Type type)
        {
            string resolve()
            {
                if (this.types.Contains(type)
                    && !type.IsGenericType
                )
                {
                    return type.FullName;
                }

                return null;
            };

            return type.FullName switch
            {
                "System.Void" => "void",

                "System.Boolean" => "boolean",
                "System.Boolean[]" => "boolean[]",

                "System.String" => "string",
                "System.String[]" => "string[]",

                "System.Int16" => "number",
                "System.Int16[]" => "number[]",
                "System.Int32" => "number",
                "System.Int32[]" => "number[]",
                "System.Int64" => "number",
                "System.Int64[]" => "number[]",
                "System.Single" => "number",
                "System.Single[]" => "number[]",
                "System.Decimal" => "number",
                "System.Decimal[]" => "number[]",
                "System.Char" => "number",
                "System.Char[]" => "number[]",

                _ => resolve() ?? "any"
            };
        }

        private void WriteMethodParameters(Type type, MethodBase method, StringBuilder sb)
        {
            foreach (var arg in method.GetParameters())
            {
                if (arg.Position != 0)
                    sb.Append(", ");

                if (arg.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
                    sb.Append("...");

                if (arg.Name == "function") sb.Append("_");
                sb.Append(arg.Name);

                if (arg.HasDefaultValue)
                    sb.Append("?");

                sb.Append(": ");

                if (type.IsGenericType)
                {
                    if (type.GetGenericArguments().Any(t => t == arg.ParameterType))
                    {
                        sb.Append(arg.ParameterType.Name);

                        continue;
                    }
                }
                sb.Append(GetJsType(arg.ParameterType));
            }
        }

        private bool WriteType(Type type, StringBuilder sb)
        {
            var typeIsStatic = type.IsAbstract && type.IsSealed;

            if (type.IsGenericType)
            {
                sb.Append($"{type.Name.Substring(0, type.Name.LastIndexOf('`'))}");

                var args = type.GetGenericArguments();

                sb.Append("<");
                foreach (var ga in args)
                {
                    if (ga.IsGenericType)
                    {
                        return false; // dont get support this
                    }
                    sb.Append(ga.Name);
                }
                sb.Append(">");
            }
            else
            {
                sb.Append($"{type.Name}");

                if (!type.IsEnum && type.BaseType != null && type.BaseType.FullName != "System.Object")
                {
                    sb.Append($" extends {type.BaseType.FullName}");
                }
            }

            sb.AppendLine(" {");

            foreach (var field in type.IsEnum ? type.GetFields() : type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
            {
                if (type.IsEnum)
                {
                    if (!field.IsLiteral) continue;

                    sb.Append("\t\t\t");

                    sb.Append(field.Name);

                    sb.Append(" = ");
                    sb.Append(field.GetRawConstantValue());

                    sb.AppendLine(",");
                }
                else
                {
                    sb.Append("\t\t\t");
                    if (typeIsStatic || field.IsStatic) sb.Append("static ");

                    sb.Append(field.Name);

                    sb.Append(": ");
                    sb.Append(GetJsType(field.FieldType));

                    sb.AppendLine(";");
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
            {
                sb.Append("\t\t\t");
                if (typeIsStatic || property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true) sb.Append("static ");

                sb.Append(property.Name);

                sb.Append(": ");
                sb.Append(GetJsType(property.PropertyType));

                sb.AppendLine(";");
            }

            if (!type.IsEnum)
            {
                foreach (var method in type.GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
                {
                    if (type.GetProperties().Any(p => p.GetMethod == method || p.SetMethod == method)) continue;

                    sb.Append("\t\t\t ");
                    if (typeIsStatic || method.IsStatic) sb.Append("static ");

                    sb.Append("constructor");
                    sb.Append("(");

                    foreach (var arg in method.GetParameters())
                    {
                        if (arg.Position != 0)
                            sb.Append(", ");

                        sb.Append(arg.Name);
                        sb.Append(": ");
                        sb.Append(GetJsType(arg.ParameterType));
                    }

                    sb.Append(") ");

                    sb.AppendLine(";");
                }

                foreach (var method in type.GetMethods())
                {
                    if (type.GetProperties().Any(p => p.GetMethod == method || p.SetMethod == method)) continue;
                    if (type.GetProperties().Any(p => $"get_{p.Name}" == method.Name || $"set_{p.Name}" == method.Name)) continue; // TODO: use the correct implementation

                    sb.Append("\t\t\t ");
                    if (typeIsStatic || method.IsStatic) sb.Append("static ");

                    sb.Append(method.Name);
                    sb.Append("(");

                    WriteMethodParameters(type, method, sb);

                    sb.Append(") ");

                    sb.Append(": " + GetJsType(method.ReturnType));

                    sb.AppendLine(";");
                }

                foreach (var evt in type.GetEvents())
                {
                    sb.Append("\t\t\t");
                    if (typeIsStatic || evt.AddMethod.IsStatic) sb.Append("static ");

                    sb.Append(evt.Name);

                    var invoke = evt.EventHandlerType.GetMethod("Invoke");

                    sb.Append(": ");
                    sb.Append("{ connect: (callback: (");
                    WriteMethodParameters(type, invoke, sb);
                    sb.Append(") => ");
                    sb.Append(GetJsType(evt.EventHandlerType.GetMethod("Invoke").ReturnType));
                    sb.Append(") => {disconnect: () => void} }");

                    sb.AppendLine(";");
                }
            }

            return true;
        }

        public void Write(string outputFile = "global.autogenerated.d.ts")
        {
            var sb = new StringBuilder();

            sb.AppendLine("/** Auto generated by the Mono.Cecil.Js.TypingsGenerator tool **/");
            sb.AppendLine();
            sb.AppendLine(@"declare module dotnet {");

            var modules = this.types.Select(x => x.Namespace).Distinct();
            foreach (var module in modules)
            {
                sb.AppendLine($"\tmodule {module} {{");

                var types = this.types.Where(x => x.Namespace == module).Distinct().OrderBy(x => x.Namespace).ThenBy(y => y.FullName).ToArray();
                foreach (var type in types)
                {
                    if (type.IsNested) continue;

                    StringBuilder sub = new StringBuilder();

                    if (type.IsInterface)
                    {
                        sub.Append($"\t\tinterface ");
                    }
                    else if (type.IsEnum)
                    {
                        if (type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0)
                            sub.AppendLine($"\t\t/** Flags */");

                        sub.Append($"\t\tenum ");
                    }
                    else
                    {
                        sub.Append($"\t\t");
                        if (type.IsAbstract)
                            sub.Append("abstract ");
                        sub.Append("class ");
                    }

                    if (!WriteType(type, sub)) continue;

                    sb.Append(sub);

                    sb.AppendLine("\t\t}");
                }

                sb.AppendLine("\t}");
            }

            sb.AppendLine(@"}");

            File.WriteAllText(outputFile, sb.ToString());
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    this.types.Clear();
                    this.types = null;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~TypingsGenerator()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
