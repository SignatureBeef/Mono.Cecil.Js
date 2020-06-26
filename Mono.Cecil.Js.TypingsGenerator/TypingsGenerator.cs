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

            foreach (var type in types.Where(t => t.IsPublic))
                AddType(type);
        }

        public void AddType(Type type)
        {
            while (type?.FullName != null && !this.types.Any(t => t.FullName == type.FullName))
            {
                this.types.Add(type);
                if (type.BaseType != null && type.BaseType.FullName != "System.Object")
                {
                    type = type.BaseType;
                }
                else break;
            }
        }
        public void AddType<TType>() => AddType(typeof(TType));

        private string GetJsType(Type type)
        {
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
                _ => "any"
            };
        }

        private void WriteType(Type type, StringBuilder sb)
        {
            var typeIsStatic = type.IsAbstract && type.IsSealed;

            if (type.IsGenericType)
            {
                sb.Append($"{type.Name.Substring(0, type.Name.LastIndexOf('`'))}");

                var args = type.GetGenericArguments();

                sb.Append("<");
                foreach (var ga in args)
                {
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

            //foreach (var property in type.GetProperties())
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

                    foreach (var arg in method.GetParameters())
                    {
                        if (arg.Position != 0)
                            sb.Append(", ");

                        if (arg.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
                            sb.Append("...");

                        sb.Append(arg.Name);

                        if (arg.HasDefaultValue)
                            sb.Append("?");

                        sb.Append(": ");

                        if (type.IsGenericType)
                        {
                            if (type.GetGenericArguments().Any(t => t == arg.ParameterType))
                            {
                                sb.Append(arg.ParameterType);

                                continue;
                            }
                        }
                        sb.Append(GetJsType(arg.ParameterType));
                    }

                    sb.Append(") ");

                    sb.Append(": " + GetJsType(method.ReturnType));

                    sb.AppendLine(";");
                }
            }
        }

        public void Write(string outputFile = "../../../scripts/otapi/src/typings/global_autogenerated.d.ts")
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

                    if (type.IsInterface)
                    {
                        sb.Append($"\t\tinterface ");
                        WriteType(type, sb);
                    }
                    else if (type.IsEnum)
                    {
                        if (type.CustomAttributes.Any(x => x.AttributeType.FullName == "System.FlagsAttribute"))
                        {
                            sb.AppendLine($"\t\t/** Flags */");
                        }

                        sb.Append($"\t\tenum ");
                        WriteType(type, sb);
                    }
                    else
                    {
                        sb.Append($"\t\t");
                        if (type.IsAbstract)
                            sb.Append("abstract ");
                        sb.Append("class ");
                        WriteType(type, sb);
                    }

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
