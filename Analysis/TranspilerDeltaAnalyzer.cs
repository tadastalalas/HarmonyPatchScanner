using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>
    /// Re-applies the transpiler chain of a target step by step (PatchProcessor.GetCurrentInstructions with
    /// maxTranspilers = 0, 1, 2, …) and attributes the difference between consecutive results to each transpiler.
    /// Also checks that every infix still has its inner call present in the final body.
    /// Opt-in: this executes mod transpiler code again.
    /// </summary>
    internal static class TranspilerDeltaAnalyzer
    {
        internal static void Analyze(TargetReport target, List<string> errors)
        {
            bool hasTranspilers = target.Transpilers.Count > 0;
            bool hasInfixes     = target.InnerPrefixes.Count > 0 || target.InnerPostfixes.Count > 0;
            if (!hasTranspilers && !hasInfixes) return;

            List<CodeInstruction> current;
            try
            {
                current = HarmonyLib.PatchProcessor.GetCurrentInstructions(target.Method, 0);
            }
            catch (Exception ex)
            {
                errors.Add($"{target.ShortName}: could not read original IL — {Root(ex).Message}");
                return;
            }

            Dictionary<string, int> previous = Count(current);
            bool chainBroken = false;

            for (int k = 0; k < target.Transpilers.Count; k++)
            {
                ScannedPatch transpiler = target.Transpilers[k];
                TranspilerDelta delta   = new();
                transpiler.Delta        = delta;

                if (chainBroken)
                {
                    delta.Skipped = true;
                    continue;
                }

                try
                {
                    current = HarmonyLib.PatchProcessor.GetCurrentInstructions(target.Method, k + 1);
                }
                catch (Exception ex)
                {
                    delta.Threw = true;
                    delta.Error = Root(ex).Message;
                    chainBroken = true;
                    continue;
                }

                Dictionary<string, int> now = Count(current);
                Diff(previous, now, delta);
                previous = now;
            }

            if (hasInfixes && !chainBroken)
                CheckInfixAnchors(target, current);
        }

        private static void Diff(Dictionary<string, int> before, Dictionary<string, int> after, TranspilerDelta delta)
        {
            foreach (KeyValuePair<string, int> kv in after)
            {
                before.TryGetValue(kv.Key, out int old);
                if (kv.Value > old)
                {
                    delta.Added += kv.Value - old;
                    AddMember(delta, kv.Key);
                }
            }
            foreach (KeyValuePair<string, int> kv in before)
            {
                after.TryGetValue(kv.Key, out int now);
                if (kv.Value > now)
                {
                    delta.Removed += kv.Value - now;
                    AddMember(delta, kv.Key);
                }
            }
        }

        private static void AddMember(TranspilerDelta delta, string token)
        {
            int at = token.IndexOf("\u0001", StringComparison.Ordinal);
            if (at >= 0)
                delta.TouchedMembers.Add(token.Substring(at + 1));
        }

        private static Dictionary<string, int> Count(List<CodeInstruction> code)
        {
            Dictionary<string, int> counts = new(StringComparer.Ordinal);
            for (int i = 0; i < code.Count; i++)
            {
                string token = Token(code[i]);
                counts.TryGetValue(token, out int n);
                counts[token] = n + 1;
            }
            return counts;
        }

        // Member operands are tagged with \u0001 so AddMember can extract a readable name from the token.
        private static string Token(CodeInstruction ins)
        {
            string op = ins.opcode.Name ?? "?";
            return ins.operand switch
            {
                null                  => op,
                Label                 => op + " label",
                Label[]               => op + " labels",
                LocalBuilder lb       => op + " loc" + lb.LocalIndex.ToString(CultureInfo.InvariantCulture),
                LocalVariableInfo lvi => op + " loc" + lvi.LocalIndex.ToString(CultureInfo.InvariantCulture),
                ParameterInfo pi      => op + " arg" + pi.Position.ToString(CultureInfo.InvariantCulture),
                MethodBase m          => op + " \u0001" + Reporting.Names.Method(m),
                FieldInfo f           => op + " \u0001" + Reporting.Names.Type(f.DeclaringType) + "." + f.Name,
                Type t                => op + " " + (t.FullName ?? t.Name),
                string s              => op + " \"" + s + "\"",
                IFormattable n        => op + " " + n.ToString(null, CultureInfo.InvariantCulture),
                object o              => op + " " + o
            };
        }

        private static void CheckInfixAnchors(TargetReport target, List<CodeInstruction> finalBody)
        {
            foreach (ScannedPatch infix in target.InnerPrefixes)
                CheckInfixAnchor(infix, finalBody);
            foreach (ScannedPatch infix in target.InnerPostfixes)
                CheckInfixAnchor(infix, finalBody);
        }

        private static void CheckInfixAnchor(ScannedPatch infix, List<CodeInstruction> body)
        {
            if (infix.InnerMethod == null) return;

            int calls = 0;
            for (int i = 0; i < body.Count; i++)
            {
                CodeInstruction ins = body[i];
                if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodBase m && SameMethod(m, infix.InnerMethod))
                    calls++;
            }

            string inner = Reporting.Names.Method(infix.InnerMethod);
            if (calls == 0)
            {
                infix.Lints.Add(new Finding(Severity.High,
                    $"no call to {inner} exists in the method body after transpilers — this infix cannot attach and does nothing.", infix.Mod));
                return;
            }

            foreach (int pos in infix.InnerPositions)
            {
                bool exists = pos > 0 ? pos <= calls : pos < 0 && -pos <= calls;
                if (!exists)
                    infix.Lints.Add(new Finding(Severity.Medium,
                        $"call position {pos} of {inner} does not exist (the body contains {calls} call(s)); that position is ignored.", infix.Mod));
            }
        }

        private static bool SameMethod(MethodBase a, MethodBase b) =>
            ReferenceEquals(a, b) || (a.MetadataToken == b.MetadataToken && a.Module == b.Module && a.DeclaringType == b.DeclaringType);

        private static Exception Root(Exception ex)
        {
            while (ex is TargetInvocationException { InnerException: not null } tie)
                ex = tie.InnerException!;
            return ex;
        }
    }
}
