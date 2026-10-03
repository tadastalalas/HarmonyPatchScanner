using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Reporting
{
    /// <summary>Short, human-readable names for types, methods, priorities and patch kinds.</summary>
    internal static class Names
    {
        private static readonly Dictionary<Type, string> Keywords = new()
        {
            [typeof(void)]   = "void",   [typeof(bool)]   = "bool",   [typeof(int)]    = "int",
            [typeof(uint)]   = "uint",   [typeof(long)]   = "long",   [typeof(ulong)]  = "ulong",
            [typeof(short)]  = "short",  [typeof(ushort)] = "ushort", [typeof(byte)]   = "byte",
            [typeof(sbyte)]  = "sbyte",  [typeof(float)]  = "float",  [typeof(double)] = "double",
            [typeof(string)] = "string", [typeof(object)] = "object", [typeof(char)]   = "char",
            [typeof(decimal)] = "decimal"
        };

        internal static string Type(Type? t)
        {
            if (t == null) return "?";
            if (Keywords.TryGetValue(t, out string? kw)) return kw;
            if (t.IsByRef)  return "ref " + Type(t.GetElementType());
            if (t.IsArray)  return Type(t.GetElementType()) + "[]";
            if (t.IsPointer) return Type(t.GetElementType()) + "*";

            string name = t.IsGenericType ? StripArity(t.Name) : t.Name;
            if (t.IsGenericParameter) return name;

            if (t.IsNested && t.DeclaringType != null)
                name = Type(t.DeclaringType) + "." + name;

            if (t.IsGenericType)
            {
                Type[] args = t.GetGenericArguments();
                StringBuilder sb = new(name);
                sb.Append('<');
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Type(args[i]));
                }
                sb.Append('>');
                name = sb.ToString();
            }
            return name;
        }

        /// <summary>"Hero.get_Age()" / "Campaign.OnTick(float, bool)" / "Hero.ctor(string)".</summary>
        internal static string Method(MethodBase m)
        {
            string name = m is ConstructorInfo ? (m.IsStatic ? "cctor" : "ctor") : m.Name;
            if (m is MethodInfo mi && mi.IsGenericMethod)
            {
                Type[] args = mi.GetGenericArguments();
                string[] parts = new string[args.Length];
                for (int i = 0; i < args.Length; i++) parts[i] = Type(args[i]);
                name += "<" + string.Join(", ", parts) + ">";
            }

            ParameterInfo[] ps = m.GetParameters();
            string[] pars = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++) pars[i] = Type(ps[i].ParameterType);

            return $"{Type(m.DeclaringType)}.{name}({string.Join(", ", pars)})";
        }

        /// <summary>"AgePatch.Prefix" — declaring type and name only.</summary>
        internal static string PatchMethod(MethodInfo m) => $"{Type(m.DeclaringType)}.{m.Name}";

        internal static string Namespace(MethodBase m) => m.DeclaringType?.Namespace ?? string.Empty;

        /// <summary>Unique per overload; used as dictionary key.</summary>
        internal static string Key(MethodBase m) => m.FullDescription();

        internal static string Priority(int priority)
        {
            string name = priority switch
            {
                HarmonyLib.Priority.First            => "First",
                HarmonyLib.Priority.VeryHigh         => "VeryHigh",
                HarmonyLib.Priority.High             => "High",
                HarmonyLib.Priority.HigherThanNormal => "HigherThanNormal",
                HarmonyLib.Priority.Normal           => "Normal",
                HarmonyLib.Priority.LowerThanNormal  => "LowerThanNormal",
                HarmonyLib.Priority.Low              => "Low",
                HarmonyLib.Priority.VeryLow          => "VeryLow",
                HarmonyLib.Priority.Last             => "Last",
                _                                    => "custom"
            };
            return $"{priority} {name}";
        }

        internal static string Kind(PatchKind kind) => kind switch
        {
            PatchKind.Prefix       => "PREFIX",
            PatchKind.Postfix      => "POSTFIX",
            PatchKind.Transpiler   => "TRANSPILER",
            PatchKind.Finalizer    => "FINALIZER",
            PatchKind.InnerPrefix  => "INNER PREFIX",
            _                      => "INNER POSTFIX"
        };

        internal static string KindPlural(PatchKind kind) => kind switch
        {
            PatchKind.Prefix       => "prefixes",
            PatchKind.Postfix      => "postfixes",
            PatchKind.Transpiler   => "transpilers",
            PatchKind.Finalizer    => "finalizers",
            PatchKind.InnerPrefix  => "inner prefixes",
            _                      => "inner postfixes"
        };

        internal static string KindSingular(PatchKind kind) => kind switch
        {
            PatchKind.Prefix       => "prefix",
            PatchKind.Postfix      => "postfix",
            PatchKind.Transpiler   => "transpiler",
            PatchKind.Finalizer    => "finalizer",
            PatchKind.InnerPrefix  => "inner prefix",
            _                      => "inner postfix"
        };

        internal static string Severity(Severity s) => s switch
        {
            Model.Severity.High   => "HIGH",
            Model.Severity.Medium => "MEDIUM",
            Model.Severity.Low    => "LOW",
            _                     => "INFO"
        };

        internal static string Launcher(int? position) => position.HasValue ? "#" + position.Value : "?";

        internal static string Origin(TargetReport t) => t.Origin switch
        {
            TargetOrigin.Official  => "official game code",
            TargetOrigin.Framework => "framework code",
            TargetOrigin.Mod       => "mod code: " + (t.OriginMod?.Label ?? "?"),
            _                      => "unknown origin"
        };

        private static string StripArity(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }
    }
}
