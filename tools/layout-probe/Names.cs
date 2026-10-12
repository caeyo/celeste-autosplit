using System;
using System.Linq;
using System.Reflection;

namespace LayoutProbe
{
    /// <summary>Resolves the manifest's .NET names in the game's assembly, failing clearly when one doesn't.</summary>
    internal static class Names
    {
        public const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags DeclaredInstance = AnyInstance | BindingFlags.DeclaredOnly;

        public static Type Type(Assembly assembly, string name) =>
            assembly.GetType(name) ?? throw new ProbeException("no type " + name + " in " + assembly.GetName().Name);

        /// <summary>A static field, as Namespace.Type::Field.</summary>
        public static FieldInfo StaticField(Assembly assembly, string reference)
        {
            Split(reference, out string typeName, out string fieldName);
            Type type = Type(assembly, typeName);
            return type.GetField(fieldName, AnyStatic | BindingFlags.DeclaredOnly) ?? throw new ProbeException("no static field " + fieldName + " in " + type.FullName);
        }

        /// <summary>A method or constructor, as Namespace.Type::Name(ParameterType, ...).</summary>
        public static MethodBase Method(Assembly assembly, string reference)
        {
            Split(reference, out string typeName, out string signature);
            int open = signature.IndexOf('(');
            if (open < 0 || !signature.EndsWith(")"))
            {
                throw new ProbeException(reference + ": expected Name(ParameterType, ...)");
            }
            string name = signature.Substring(0, open);
            string parameters = signature.Substring(open + 1, signature.Length - open - 2);
            string[] parameterTypes = parameters.Trim().Length == 0 ? new string[0] : parameters.Split(',').Select(p => p.Trim()).ToArray();

            Type type = Type(assembly, typeName);
            MethodBase[] candidates = name == ".ctor" ? type.GetConstructors(AnyInstance)
                : name == ".cctor" ? new MethodBase[] { type.TypeInitializer }.Where(c => c != null).ToArray()
                : type.GetMethods(AnyInstance | AnyStatic | BindingFlags.DeclaredOnly).Where(m => m.Name == name).ToArray<MethodBase>();
            return candidates.SingleOrDefault(m => m.GetParameters().Select(p => TypeName(p.ParameterType)).SequenceEqual(parameterTypes))
                ?? throw new ProbeException("no method " + reference + "; " + type.FullName + " has " + (candidates.Length == 0 ? "no " + name : string.Join(", ", candidates.Select(Describe).ToArray())));
        }

        /// <summary>An instance field of the type or, like C# finds it, of its nearest base class with one.</summary>
        public static FieldInfo InstanceField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, DeclaredInstance);
                if (field != null)
                {
                    return field;
                }
            }
            throw new ProbeException("no instance field " + name + " in " + type.FullName + " or its base classes");
        }

        /// <summary>A type's full name, with generic arguments written as in C#.</summary>
        public static string TypeName(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.FullName ?? type.Name;
            }
            // Each generic level of the name ends in its arity, as in List`1+Enumerator.
            string name = string.Join("+", type.GetGenericTypeDefinition().FullName.Split('+').Select(part => part.Split('`')[0]).ToArray());
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName).ToArray()) + ">";
        }

        private static void Split(string reference, out string type, out string member)
        {
            int separator = reference.IndexOf("::", StringComparison.Ordinal);
            if (separator < 0)
            {
                throw new ProbeException(reference + ": expected Namespace.Type::Member");
            }
            type = reference.Substring(0, separator);
            member = reference.Substring(separator + 2);
        }

        private static string Describe(MethodBase method) =>
            method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => TypeName(p.ParameterType)).ToArray()) + ")";
    }

    /// <summary>A failure the probe explains itself, so its stack trace isn't printed.</summary>
    internal sealed class ProbeException : Exception
    {
        public ProbeException(string message) : base(message)
        {
        }
    }
}
