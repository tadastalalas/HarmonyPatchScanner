using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>
    /// Reads a bool prefix's IL and classifies what it returns by looking at the instruction that produces
    /// the value for every <c>ret</c> (following <c>br</c>/<c>nop</c>/<c>dup</c> and local variable stores).
    /// Anything that is not a constant — calls, comparisons, fields, arguments — counts as Conditional.
    /// </summary>
    internal static class PrefixReturnAnalyzer
    {
        private const int MaxDepth = 8;

        private enum Producer { True, False, Computed }

        internal static PrefixReturn Classify(MethodInfo method)
        {
            if (method.ReturnType != typeof(bool))
                return PrefixReturn.NotBool;

            List<CodeInstruction> code;
            try
            {
                code = HarmonyLib.PatchProcessor.GetOriginalInstructions(method);
            }
            catch
            {
                return PrefixReturn.Unknown;
            }

            if (code == null || code.Count == 0)
                return PrefixReturn.Unknown;

            List<int>[] preds = BuildPredecessors(code);
            bool sawTrue = false, sawFalse = false, sawComputed = false, sawRet = false;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ret) continue;
                sawRet = true;

                HashSet<Producer> producers = [];
                foreach (int j in preds[i])
                    Collect(code, preds, j, producers, [], 0);

                if (producers.Count == 0) producers.Add(Producer.Computed);

                if (producers.Contains(Producer.True))     sawTrue     = true;
                if (producers.Contains(Producer.False))    sawFalse    = true;
                if (producers.Contains(Producer.Computed)) sawComputed = true;
            }

            if (!sawRet)                return PrefixReturn.Unknown;
            if (sawComputed)            return PrefixReturn.Conditional;
            if (sawTrue && sawFalse)    return PrefixReturn.Conditional;
            if (sawTrue)                return PrefixReturn.AlwaysTrue;
            if (sawFalse)               return PrefixReturn.AlwaysFalse;
            return PrefixReturn.Unknown;
        }

        // Classifies the value on top of the stack right after instruction j executed.
        private static void Collect(List<CodeInstruction> code, List<int>[] preds, int j, HashSet<Producer> result, HashSet<int> visitedLocals, int depth)
        {
            if (depth > MaxDepth) { result.Add(Producer.Computed); return; }

            CodeInstruction ins = code[j];
            OpCode op = ins.opcode;

            if (op == OpCodes.Nop || op == OpCodes.Br || op == OpCodes.Br_S || op == OpCodes.Dup)
            {
                // transparent: the value was produced before this instruction
                if (preds[j].Count == 0) result.Add(Producer.Computed);
                foreach (int k in preds[j])
                    Collect(code, preds, k, result, visitedLocals, depth + 1);
                return;
            }

            if (TryConstant(ins, out bool value))
            {
                result.Add(value ? Producer.True : Producer.False);
                return;
            }

            if (TryLocalIndex(ins, out int local, out bool isLoad, out bool isAddress) && isLoad && !isAddress)
            {
                if (!visitedLocals.Add(local)) return;

                bool anyStore = false;
                for (int k = 0; k < code.Count; k++)
                {
                    if (!TryLocalIndex(code[k], out int other, out bool otherIsLoad, out bool otherIsAddress) || other != local) continue;

                    if (otherIsAddress)
                    {
                        // written through its address (out/ref call) — value unknown
                        result.Add(Producer.Computed);
                        anyStore = true;
                        continue;
                    }
                    if (otherIsLoad) continue;

                    anyStore = true;
                    if (preds[k].Count == 0) result.Add(Producer.Computed);
                    foreach (int p in preds[k])
                        Collect(code, preds, p, result, visitedLocals, depth + 1);
                }

                if (!anyStore) result.Add(Producer.Computed);
                return;
            }

            result.Add(Producer.Computed);
        }

        private static bool TryConstant(CodeInstruction ins, out bool value)
        {
            OpCode op = ins.opcode;
            value = false;

            if (op == OpCodes.Ldc_I4_0) { value = false; return true; }
            if (op == OpCodes.Ldc_I4_1) { value = true;  return true; }
            if (op == OpCodes.Ldc_I4_M1 || op == OpCodes.Ldc_I4_2 || op == OpCodes.Ldc_I4_3 || op == OpCodes.Ldc_I4_4 ||
                op == OpCodes.Ldc_I4_5  || op == OpCodes.Ldc_I4_6 || op == OpCodes.Ldc_I4_7 || op == OpCodes.Ldc_I4_8)
            {
                value = true;
                return true;
            }
            if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S)
            {
                value = ins.operand switch
                {
                    int i    => i != 0,
                    sbyte sb => sb != 0,
                    byte b   => b != 0,
                    short s  => s != 0,
                    long l   => l != 0,
                    _        => true
                };
                return true;
            }
            return false;
        }

        private static bool TryLocalIndex(CodeInstruction ins, out int index, out bool isLoad, out bool isAddress)
        {
            OpCode op = ins.opcode;
            index = -1; isLoad = false; isAddress = false;

            if (op == OpCodes.Ldloc_0) { index = 0; isLoad = true; return true; }
            if (op == OpCodes.Ldloc_1) { index = 1; isLoad = true; return true; }
            if (op == OpCodes.Ldloc_2) { index = 2; isLoad = true; return true; }
            if (op == OpCodes.Ldloc_3) { index = 3; isLoad = true; return true; }
            if (op == OpCodes.Stloc_0) { index = 0; return true; }
            if (op == OpCodes.Stloc_1) { index = 1; return true; }
            if (op == OpCodes.Stloc_2) { index = 2; return true; }
            if (op == OpCodes.Stloc_3) { index = 3; return true; }

            bool load    = op == OpCodes.Ldloc || op == OpCodes.Ldloc_S;
            bool address = op == OpCodes.Ldloca || op == OpCodes.Ldloca_S;
            bool store   = op == OpCodes.Stloc || op == OpCodes.Stloc_S;
            if (!load && !address && !store) return false;

            index = ins.operand switch
            {
                LocalBuilder lb       => lb.LocalIndex,
                LocalVariableInfo lvi => lvi.LocalIndex,
                int i                 => i,
                short s               => s,
                byte b                => b,
                sbyte sb              => sb,
                ushort us             => us,
                _                     => -1
            };
            if (index < 0) return false;

            isLoad    = load || address;
            isAddress = address;
            return true;
        }

        // preds[i] = indices of instructions that can execute immediately before instruction i.
        private static List<int>[] BuildPredecessors(List<CodeInstruction> code)
        {
            Dictionary<Label, int> labelAt = [];
            for (int i = 0; i < code.Count; i++)
                foreach (Label l in code[i].labels)
                    labelAt[l] = i;

            List<int>[] preds = new List<int>[code.Count];
            for (int i = 0; i < code.Count; i++) preds[i] = [];

            for (int i = 0; i < code.Count; i++)
            {
                OpCode op = code[i].opcode;
                FlowControl flow = op.FlowControl;

                bool fallsThrough = flow != FlowControl.Branch && flow != FlowControl.Return && flow != FlowControl.Throw;
                if (fallsThrough && i + 1 < code.Count)
                    preds[i + 1].Add(i);

                if (flow == FlowControl.Branch || flow == FlowControl.Cond_Branch)
                {
                    switch (code[i].operand)
                    {
                        case Label l when labelAt.TryGetValue(l, out int t):
                            preds[t].Add(i);
                            break;
                        case Label[] ls:
                            foreach (Label l in ls)
                                if (labelAt.TryGetValue(l, out int t))
                                    preds[t].Add(i);
                            break;
                    }
                }
            }
            return preds;
        }
    }
}
