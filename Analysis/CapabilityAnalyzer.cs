using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>Derives PatchCapabilities from a patch method's signature using Harmony's own interpretation rules.</summary>
    internal static class CapabilityAnalyzer
    {
        internal static PatchCapabilities Analyze(MethodInfo method, PatchKind kind, MethodBase argSource)
        {
            PatchCapabilities caps = new();
            ParameterInfo[] parameters = method.GetParameters();

            caps.IsFactory = IsFactory(method, parameters);
            caps.IsVoid    = method.ReturnType == typeof(void);

            if (kind == PatchKind.Transpiler)
            {
                foreach (ParameterInfo p in parameters)
                {
                    if (p.ParameterType == typeof(ILGenerator)) caps.TakesGenerator = true;
                    if (p.ParameterType == typeof(MethodBase))  caps.TakesOriginal  = true;
                }
                return caps;
            }

            caps.ReturnsBool = method.ReturnType == typeof(bool);

            List<InjectedParam> injected = InjectionResolver.Resolve(method);

            // Pass-through postfix: Harmony feeds __result into the first parameter and takes the return value back.
            if (kind == PatchKind.Postfix && !caps.IsVoid && injected.Count > 0 && injected[0].Parameter.ParameterType == method.ReturnType)
            {
                caps.PassThrough     = true;
                caps.ReadsResult     = true;
                caps.CanChangeResult = true;
                injected.RemoveAt(0);
            }

            if (kind == PatchKind.Finalizer && !caps.IsVoid)
                caps.ReturnsException = true;

            string[] argNames = ArgNames(argSource);

            foreach (InjectedParam p in injected)
            {
                switch (p.Type)
                {
                    case InjectionType.Instance:
                        caps.UsesInstance = true;
                        break;
                    case InjectionType.OriginalMethod:
                        break;
                    case InjectionType.ArgsArray:
                        caps.UsesArgsArray = true;
                        break;
                    case InjectionType.Result:
                        caps.ReadsResult = true;
                        if (p.IsByRef) caps.CanChangeResult = true;
                        break;
                    case InjectionType.ResultRef:
                        caps.CanChangeResult = true;
                        break;
                    case InjectionType.State:
                        caps.UsesState = true;
                        if (p.IsByRef) caps.StateIsWritable = true;
                        break;
                    case InjectionType.Exception:
                        caps.ObservesException = true;
                        break;
                    case InjectionType.RunOriginal:
                        caps.UsesRunOriginal = true;
                        break;
                    default:
                        ClassifyPlainParameter(caps, p, argNames);
                        break;
                }
            }

            if (kind == PatchKind.Prefix)
            {
                string? reason = caps.ReturnsBool ? "returns bool" : AffectsReason(injected);
                caps.AffectsOriginal = reason != null;
                caps.AffectsReason   = reason ?? string.Empty;
            }

            return caps;
        }

        // Mirrors MethodCreatorTools.AffectsOriginal (Harmony 2.4.2) minus the bool-return shortcut handled by the caller.
        // Returns the first reason found (Harmony's Any() stops at the first as well), or null.
        private static string? AffectsReason(List<InjectedParam> injected)
        {
            foreach (InjectedParam p in injected)
            {
                if (p.Type is InjectionType.Instance or InjectionType.OriginalMethod or InjectionType.State)
                    continue;

                ParameterInfo info = p.Parameter;
                string name = p.RealName;

                if (info.IsOut || info.IsRetval || info.ParameterType.IsByRef)
                    return $"ref/out parameter '{name}'";

                Type t = info.ParameterType;
                if (!AccessTools.IsValue(t) && !AccessTools.IsStruct(t))
                    return p.Type == InjectionType.ArgsArray ? "__args" : $"reference-type parameter '{name}'";
            }
            return null;
        }

        private static void ClassifyPlainParameter(PatchCapabilities caps, InjectedParam p, string[] argNames)
        {
            if (p.IsField)
            {
                caps.Fields.Add(p.FieldName);
                if (p.IsByRef) caps.WritableFields.Add(p.FieldName);
                return;
            }

            string? argName = null;
            if (p.TryGetArgIndex(out int index))
                argName = index >= 0 && index < argNames.Length ? argNames[index] : null;
            else if (Array.IndexOf(argNames, p.RealName) >= 0)
                argName = p.RealName;

            if (argName == null)
            {
                // Harmony's last resort is a delegate injection ([HarmonyDelegate] type); anything else would have thrown at patch time.
                caps.Notes.Add(typeof(Delegate).IsAssignableFrom(p.Parameter.ParameterType)
                    ? $"delegate injection '{p.RealName}'"
                    : $"parameter '{p.RealName}' matches no original argument");
                return;
            }

            caps.ReadArgs.Add(argName);
            if (p.IsByRef) caps.CanChangeArgs.Add(argName);
        }

        private static string[] ArgNames(MethodBase source)
        {
            ParameterInfo[] ps = source.GetParameters();
            string[] names = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                names[i] = ps[i].Name ?? $"__{i}";
            return names;
        }

        // Patch.GetMethod(): static, returns DynamicMethod/MethodInfo, single MethodBase parameter → invoked to get the real patch.
        private static bool IsFactory(MethodInfo method, ParameterInfo[] parameters) =>
            method.IsStatic
            && (method.ReturnType == typeof(DynamicMethod) || method.ReturnType == typeof(MethodInfo))
            && parameters.Length == 1
            && parameters[0].ParameterType == typeof(MethodBase);
    }
}
