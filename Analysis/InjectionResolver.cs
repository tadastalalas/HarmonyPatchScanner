using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>Mirrors HarmonyLib.InjectionType (internal in Harmony).</summary>
    internal enum InjectionType
    {
        Unknown,
        Instance,
        OriginalMethod,
        ArgsArray,
        Result,
        ResultRef,
        State,
        Exception,
        RunOriginal
    }

    internal sealed class InjectedParam
    {
        internal ParameterInfo Parameter { get; }
        /// <summary>Name after [HarmonyArgument] renames are undone — what Harmony matches against.</summary>
        internal string RealName { get; }
        internal InjectionType Type { get; }

        internal InjectedParam(ParameterInfo parameter, string realName, InjectionType type)
        {
            Parameter = parameter;
            RealName  = realName;
            Type      = type;
        }

        internal bool IsByRef  => Parameter.ParameterType.IsByRef || Parameter.IsOut;
        internal bool IsField  => Type == InjectionType.Unknown && RealName.StartsWith("___", StringComparison.Ordinal);
        internal string FieldName => RealName.Substring(3);

        /// <summary>"__0", "__1" — argument by zero-based index.</summary>
        internal bool TryGetArgIndex(out int index)
        {
            index = -1;
            return Type == InjectionType.Unknown
                && !IsField
                && RealName.StartsWith("__", StringComparison.Ordinal)
                && int.TryParse(RealName.Substring(2), out index);
        }
    }

    /// <summary>
    /// Resolves patch parameters the way MethodPatcherTools.OriginalParameters does:
    /// parameter-level [HarmonyArgument] wins, then method/type-level [HarmonyArgument(original, newName)],
    /// then the plain parameter name.
    /// </summary>
    internal static class InjectionResolver
    {
        internal static List<InjectedParam> Resolve(MethodInfo method)
        {
            List<HarmonyArgument> baseArgs = method.GetCustomAttributes(false).OfType<HarmonyArgument>().ToList();
            if (method.DeclaringType != null)
                baseArgs.AddRange(method.DeclaringType.GetCustomAttributes(false).OfType<HarmonyArgument>());

            List<InjectedParam> result = [];
            foreach (ParameterInfo p in method.GetParameters())
            {
                string name = p.Name ?? string.Empty;
                HarmonyArgument? own = p.GetCustomAttributes(false).OfType<HarmonyArgument>().FirstOrDefault();

                string realName = own != null
                    ? own.OriginalName ?? name
                    : RealNameFromBase(baseArgs, name) ?? name;

                result.Add(new InjectedParam(p, realName, Classify(realName)));
            }
            return result;
        }

        private static string? RealNameFromBase(List<HarmonyArgument> baseArgs, string name)
        {
            HarmonyArgument? match = baseArgs.FirstOrDefault(a => a.NewName == name);
            return match != null && !string.IsNullOrEmpty(match.OriginalName) ? match.OriginalName : null;
        }

        private static InjectionType Classify(string realName) => realName switch
        {
            "__instance"       => InjectionType.Instance,
            "__originalMethod" => InjectionType.OriginalMethod,
            "__args"           => InjectionType.ArgsArray,
            "__result"         => InjectionType.Result,
            "__resultRef"      => InjectionType.ResultRef,
            "__state"          => InjectionType.State,
            "__exception"      => InjectionType.Exception,
            "__runOriginal"    => InjectionType.RunOriginal,
            _                  => InjectionType.Unknown
        };
    }
}
