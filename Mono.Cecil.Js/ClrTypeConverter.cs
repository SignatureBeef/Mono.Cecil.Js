using System;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using Jint;
using Jint.Native;

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
}
