﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Dbus.CodeGenerator
{
    public static partial class Generator
    {
        private static Dictionary<Type, string> signatures = new Dictionary<Type, string>()
        {
            [typeof(ObjectPath)] = "o",
            [typeof(string)] = "s",
            [typeof(Signature)] = "g",
            [typeof(byte)] = "y",
            [typeof(bool)] = "b",
            [typeof(int)] = "i",
            [typeof(uint)] = "u",
            [typeof(object)] = "v",
        };
        const string indent = "            ";

        public static string Run()
        {
            var entry = Assembly.GetEntryAssembly();
            var candidateTypes = entry
                .GetTypes()
                .Concat(entry.GetReferencedAssemblies()
                    .Select(x => Assembly.Load(x))
                    .SelectMany(x => x.GetTypes())
                )
            ;

            var result = new StringBuilder();

            foreach (var type in candidateTypes.OrderBy(x => x.FullName))
            {
                var consume = type.GetTypeInfo().GetCustomAttribute<DbusConsumeAttribute>();
                if (consume != null)
                    result.Append(generateConsumeImplementation(type, consume));
            }

            return result.ToString();
        }

        private static string generateConsumeImplementation(Type type, DbusConsumeAttribute consume)
        {
            var className = type.Name.Substring(1);
            var eventSubscriptions = new StringBuilder();
            var methodImplementations = new StringBuilder();
            var eventImplementations = new StringBuilder();

            var members = type.GetTypeInfo().GetMembers();
            foreach (var member in members.OrderBy(x => x.Name))
            {
                MethodInfo methodInfo;
                EventInfo eventInfo;

                if ((eventInfo = member as EventInfo) != null)
                {
                    var result = generateEventImplementation(eventInfo, consume.InterfaceName);
                    eventSubscriptions.Append(result.Item1);
                    eventImplementations.Append(result.Item2);
                }
                else if ((methodInfo = member as MethodInfo) != null)
                {
                    if (!methodInfo.IsSpecialName)
                        methodImplementations.Append(generateMethodImplementation(methodInfo, consume.InterfaceName));
                }
            }

            return @"
    public sealed class " + className + @" : " + type.FullName + @"
    {
        private readonly Connection connection;
        private readonly ObjectPath path;
        private readonly string destination;
        private readonly System.Collections.Generic.List<System.IDisposable> eventSubscriptions = new System.Collections.Generic.List<System.IDisposable>();

        public " + className + @"(Connection connection, ObjectPath path = null, string destination = null)
        {
            this.connection = connection;
            this.path = path ?? """ + consume.Path + @""";
            this.destination = destination ?? """ + consume.Destination + @""";
" + eventSubscriptions + @"
        }
" + methodImplementations + @"
" + eventImplementations + @"
        private static void assertSignature(Signature actual, Signature expected)
        {
            if (actual != expected)
                throw new System.InvalidOperationException($""Unexpected signature. Got ${ actual}, but expected ${ expected}"");
        }

        public void Dispose()
        {
            eventSubscriptions.ForEach(x => x.Dispose());
        }
    }
";
        }

        private static string buildTypeString(Type type)
        {
            if (!type.IsConstructedGenericType)
                return type.FullName;

            var genericName = type.GetGenericTypeDefinition().FullName;
            var withoutSuffix = genericName.Substring(0, genericName.Length - 2);
            var result = withoutSuffix + "<" +
                string.Join(",", type.GenericTypeArguments.Select(buildTypeString)) +
                ">"
            ;
            return result;
        }
    }
}
