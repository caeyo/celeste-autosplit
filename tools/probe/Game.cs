using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Probe
{
    internal static class Game
    {
        public const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags DeclaredInstance = AnyInstance | BindingFlags.DeclaredOnly;
        private const BindingFlags DeclaredStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static readonly string[,] Anchors =
        {
            { "Celeste.Celeste::.ctor()", "Celeste.Celeste::Instance" },
            { "Celeste.AutoSplitterInfo::Update()", "Celeste.SaveData::Instance" },
        };

        public static Assembly Load() =>
            Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "real", "Celeste.exe"));

        public static Type Type(Assembly game, string name) =>
            game.GetType(name) ?? throw new ProbeException("no type " + name + " in " + game.GetName().Name);

        public static FieldInfo StaticField(Assembly game, string name)
        {
            Type type = Type(game, Split(name, out string field));
            return type.GetField(field, DeclaredStatic) ?? throw new ProbeException("no static field " + name);
        }

        public static MethodBase Method(Assembly game, string name)
        {
            Type type = Type(game, Split(name, out string method));
            MethodBase found = method == ".ctor()" ? (MethodBase)type.GetConstructor(AnyInstance, null, System.Type.EmptyTypes, null)
                : method.EndsWith("()") ? type.GetMethod(method.Substring(0, method.Length - 2), DeclaredInstance, null, System.Type.EmptyTypes, null)
                : null;
            return found ?? throw new ProbeException("no method " + name);
        }

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

        public static void Prepare(FieldInfo slot, MethodBase anchor)
        {
            RuntimeHelpers.RunClassConstructor(slot.DeclaringType.TypeHandle);
            RuntimeHelpers.RunClassConstructor(anchor.DeclaringType.TypeHandle);
            RuntimeHelpers.PrepareMethod(anchor.MethodHandle);
        }

        private static string Split(string name, out string member)
        {
            int separator = name.IndexOf("::", StringComparison.Ordinal);
            if (separator < 0)
            {
                throw new ProbeException(name + ": expected Namespace.Type::Member");
            }
            member = name.Substring(separator + 2);
            return name.Substring(0, separator);
        }
    }
}
