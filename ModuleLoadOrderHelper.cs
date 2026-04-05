using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.ModuleManager;

namespace HarmonyPatchScanner
{
    /// <summary>
    /// Builds a launcher-position lookup from TaleWorlds' own module list.
    ///
    /// Utilities.GetModulesNames() returns module IDs in the exact order the
    /// user sorted them in the Bannerlord launcher. That array is what the game
    /// passes to ModuleHelper.InitializeModules() — it is the authoritative load order.
    /// ModuleHelper.GetModuleInfos() preserves that order (iterates the input array
    /// sequentially), unlike GetModules()/GetAllModules() which use a Dictionary.
    /// </summary>
    public static class ModuleLoadOrderHelper
    {
        // Key   = DLL name without extension (e.g. "MyMod"), matched against
        //         Assembly.GetName().Name used in PatchProcessor.BuildPatchInfo
        // Value = 1-based launcher position
        private static Dictionary<string, int>?    _orderByAssembly;

        // Key   = DLL name without extension (e.g. "0Harmony")
        // Value = module Id (e.g. "Bannerlord.Harmony")
        private static Dictionary<string, string>? _moduleIdByAssembly;

        // Stored in launcher order for log output
        private static List<(int Position, string ModuleId, string ModuleName)> _orderedModules = new();

        public static void Build()
        {
            _orderByAssembly    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _moduleIdByAssembly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _orderedModules     = new List<(int, string, string)>();

            try
            {
                // GetModulesNames() returns module IDs in launcher sort order.
                // GetModuleInfos() maps those IDs back to ModuleInfo objects
                // while preserving the order of the input array.
                var moduleIds    = Utilities.GetModulesNames();
                var orderedInfos = ModuleHelper.GetModuleInfos(moduleIds);

                for (int i = 0; i < orderedInfos.Count; i++)
                {
                    var moduleInfo = orderedInfos[i];
                    int position   = i + 1; // 1-based

                    _orderedModules.Add((position, moduleInfo.Id, moduleInfo.Name));

                    // SubModuleInfo.DLLName is e.g. "MyMod.dll".
                    // Stripping ".dll" gives the assembly name that
                    // Assembly.GetName().Name returns at runtime.
                    foreach (var subModule in moduleInfo.SubModules)
                    {
                        var assemblyName = StripDllExtension(subModule.DLLName);

                        if (!string.IsNullOrEmpty(assemblyName))
                        {
                            if (!_orderByAssembly.ContainsKey(assemblyName))
                                _orderByAssembly[assemblyName] = position;

                            if (!_moduleIdByAssembly.ContainsKey(assemblyName))
                                _moduleIdByAssembly[assemblyName] = moduleInfo.Id;
                        }
                    }

                    // Also index by module Id as a fallback (e.g. "Bannerlord.MyMod")
                    if (!string.IsNullOrEmpty(moduleInfo.Id) && !_orderByAssembly.ContainsKey(moduleInfo.Id))
                        _orderByAssembly[moduleInfo.Id] = position;
                }
            }
            catch
            {
                // Degrade gracefully — launcher position will show as "unknown"
            }
        }

        /// <summary>
        /// Returns the 1-based launcher position for a given assembly name,
        /// or null if it could not be determined.
        /// </summary>
        public static int? GetLauncherPosition(string? assemblyName)
        {
            if (_orderByAssembly == null || string.IsNullOrEmpty(assemblyName))
                return null;

            return _orderByAssembly.TryGetValue(assemblyName, out var pos) ? pos : null;
        }

        /// <summary>
        /// Returns the module Id (e.g. "Bannerlord.Harmony") for a given assembly name
        /// (e.g. "0Harmony"), or null if it could not be determined.
        /// </summary>
        public static string? GetModuleId(string? assemblyName)
        {
            if (_moduleIdByAssembly == null || string.IsNullOrEmpty(assemblyName))
                return null;

            return _moduleIdByAssembly.TryGetValue(assemblyName, out var id) ? id : null;
        }

        /// <summary>
        /// Returns true if the given module Id belongs to an official TaleWorlds module.
        /// </summary>
        public static bool IsOfficialModule(string? moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return false;
            var officials = ModuleHelper.GetOfficialModuleIds();
            foreach (var id in officials)
                if (string.Equals(id, moduleId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Appends the full launcher load order list to a StringBuilder — useful
        /// as a header in every log file so the developer knows exactly what was loaded.
        /// </summary>
        public static void AppendLoadOrderHeader(StringBuilder sb)
        {
            sb.AppendLine("════════════════════════════════════════════════════");
            sb.AppendLine("  Launcher Load Order (authoritative DLL load sequence)");
            sb.AppendLine("════════════════════════════════════════════════════");

            if (_orderedModules.Count == 0)
            {
                sb.AppendLine("  (could not determine load order)");
            }
            else
            {
                foreach (var (pos, id, name) in _orderedModules)
                {
                    var officialTag   = IsOfficialModule(id)                   ? "  [official]"   : string.Empty;
                    var communityTag  = FilterHelper.IsCommunityLibrary(id)    ? "  [community lib]" : string.Empty;
                    sb.AppendLine($"  #{pos,-3} {name,-40} ({id}){officialTag}{communityTag}");
                }
            }

            sb.AppendLine();
        }

        // "MyMod.dll" → "MyMod",  "MyMod" → "MyMod"
        private static string? StripDllExtension(string? dllName)
        {
            if (string.IsNullOrEmpty(dllName))
                return null;

            return dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? dllName.Substring(0, dllName.Length - 4)
                : dllName;
        }
    }
}