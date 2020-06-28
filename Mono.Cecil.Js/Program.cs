﻿using Jint;
using Jint.CommonJS;
using Jint.Native;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;

namespace Mono.Cecil.Js
{
    public class ClrTypeConverter : Jint.Runtime.Interop.DefaultTypeConverter
    {
        public Engine _engine { get; }

        public ClrTypeConverter(Engine engine) : base(engine)
        {
            _engine = engine;
        }

        private static readonly Type iCallableType = typeof(Func<JsValue, JsValue[], JsValue>);
        private static readonly Type objectType = typeof(object);
        private static readonly Type engineType = typeof(Engine);
        private static readonly Type jsValueType = typeof(JsValue);

        public override bool TryConvert(object value, Type type, IFormatProvider formatProvider, out object converted)
        {
            if (!base.TryConvert(value, type, formatProvider, out converted))
            {
                var valueType = value.GetType();
                if (valueType == iCallableType)
                {
                    var function = (Func<JsValue, JsValue[], JsValue>)value;
                    if (typeof(MulticastDelegate).IsAssignableFrom(type))
                    {
                        var method = type.GetMethod("Invoke");
                        var arguments = method.GetParameters();

                        var @params = new ParameterExpression[arguments.Length];
                        for (var i = 0; i < @params.Length; i++)
                        {
                            @params[i] = Expression.Parameter(objectType, arguments[i].Name);
                        }

                        var initializers = new MethodCallExpression[@params.Length];
                        for (int i = 0; i < @params.Length; i++)
                        {
                            initializers[i] = Expression.Call(null, jsValueType.GetMethod("FromObject"), Expression.Constant(_engine, engineType), @params[i]);
                        }

                        var @vars = Expression.NewArrayInit(jsValueType, initializers);

                        var callExpression = Expression.Block(
                                                Expression.Call(
                                                    Expression.Call(
                                                        Expression.Constant(function.Target),
                                                        function.Method,
                                                        Expression.Constant(JsValue.Undefined, jsValueType),
                                                        @vars
                                                    ),
                                                    jsValueType.GetMethod("ToObject")),
                                                Expression.Default(method.ReturnType)
                                           );

                        var dynamicExpression = Expression.Invoke(Expression.Lambda(callExpression, new ReadOnlyCollection<ParameterExpression>(@params)), new ReadOnlyCollection<ParameterExpression>(@params));
                        var expr = Expression.Lambda(type, dynamicExpression, new ReadOnlyCollection<ParameterExpression>(@params));
                        converted = expr.Compile();
                        return true;
                    }
                }

                return false;
            }
            return true;
        }
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
                typeGen.AddType<Cecil.ResourceExtractor>();
                typeGen.AddType<Program>();
                typeGen.AddType<string>();
                typeGen.AddType<System.IDisposable>();

                typeGen.Write("../../../modules/common/global.autogenerated.d.ts");

                var engine = new Engine(cfg => cfg.AllowClr(
                //typeof(System.IO.File).Assembly,
                //typeof(System.Console).Assembly,
                //typeof(System.IO.Compression.ZipFile).Assembly,
                //typeof(Cecil.ResourceExtractor).Assembly,
                //typeof(Javascript.JsModder).Assembly,
                //typeof(System.IDisposable).Assembly,
                //typeof(System.Net.Http.HttpClient).Assembly
                typeGen.Types.Select(x => x.Assembly).Distinct().ToArray()
                ));
                {
                    engine.ClrTypeConverter = new ClrTypeConverter(engine);
                    engine.SetValue("console", new Javascript.JsConsole());
                    engine.SetValue("MonoMod", new Jint.Runtime.Interop.NamespaceReference(engine, "MonoMod"));
                    engine.SetValue("Mono", new Jint.Runtime.Interop.NamespaceReference(engine, "Mono"));

                    foreach (var directory in Directory.EnumerateDirectories("modules"))
                    {
                        var moduleName = directory.Split(Path.DirectorySeparatorChar).Last();
                        var moduleFolder = Path.Combine(Directory.GetCurrentDirectory(), directory, "dist");
                        var moduleFile = Path.Combine(moduleFolder, "index.js");

                        engine.CommonJS().RunMain(moduleFile);
                    }
                }
            }
        }
    }
}
