using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>Maps the assembly that declares a patch method to the mod (launcher module) it belongs to.</summary>
    internal sealed class ModResolver
    {
        private readonly Dictionary<string, ModInfo>   _byKey      = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Assembly, ModInfo> _byAssembly = [];

        internal IEnumerable<ModInfo> All => _byKey.Values;

        internal ModInfo Resolve(Assembly assembly)
        {
            if (_byAssembly.TryGetValue(assembly, out ModInfo? cached))
                return cached;

            string  assemblyName = assembly.GetName().Name ?? "Unknown";
            string? moduleId     = ModuleLoadOrderHelper.GetModuleId(assemblyName);
            string? folder       = null;

            if (moduleId == null)
            {
                folder = ModuleFolderFromLocation(assembly);
                if (ModuleLoadOrderHelper.IsKnownModule(folder))
                    moduleId = folder;
            }

            string key = moduleId ?? (folder != null ? "folder:" + folder : "asm:" + assemblyName);

            if (!_byKey.TryGetValue(key, out ModInfo? mod))
            {
                string label = moduleId != null ? ModuleLoadOrderHelper.GetModuleName(moduleId) : folder ?? assemblyName;
                mod = new ModInfo(
                    key,
                    label,
                    moduleId,
                    ModuleLoadOrderHelper.GetLauncherPosition(moduleId ?? assemblyName),
                    ModuleLoadOrderHelper.IsOfficialModule(moduleId),
                    FilterHelper.IsCommunityLibrary(moduleId));
                _byKey[key] = mod;
            }

            mod.Assemblies.Add(assemblyName);
            _byAssembly[assembly] = mod;
            return mod;
        }

        /// <summary>Classifies the assembly that declares a patched method.</summary>
        internal (TargetOrigin Origin, ModInfo? Mod) ResolveTarget(Assembly? assembly)
        {
            if (assembly == null)
                return (TargetOrigin.Unknown, null);

            string name = assembly.GetName().Name ?? string.Empty;

            if (IsFrameworkAssembly(name))
                return (TargetOrigin.Framework, null);

            if (IsOfficialAssemblyName(name))
                return (TargetOrigin.Official, null);

            ModInfo mod = Resolve(assembly);
            if (mod.IsOfficial)
                return (TargetOrigin.Official, null);

            return mod.ModuleId != null || !string.IsNullOrEmpty(assembly.Location)
                ? (TargetOrigin.Mod, mod)
                : (TargetOrigin.Unknown, null);
        }

        private static bool IsFrameworkAssembly(string name) =>
            name == "mscorlib" || name == "netstandard" || name == "System" ||
            name.StartsWith("System.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            name.StartsWith("Mono.", StringComparison.Ordinal);

        // Game assemblies live in bin\, not in a module folder, so SubModule.xml lookup cannot find them.
        private static bool IsOfficialAssemblyName(string name) =>
            name.StartsWith("TaleWorlds.", StringComparison.Ordinal) ||
            name.StartsWith("SandBox", StringComparison.Ordinal) ||
            name.StartsWith("StoryMode", StringComparison.Ordinal) ||
            name.StartsWith("CustomBattle", StringComparison.Ordinal) ||
            name.StartsWith("Multiplayer", StringComparison.Ordinal) ||
            name.StartsWith("BirthAndDeath", StringComparison.Ordinal);

        // ...\Modules\<Folder>\bin\Win64_Shipping_Client\X.dll  →  "<Folder>"
        private static string? ModuleFolderFromLocation(Assembly assembly)
        {
            try
            {
                string location = assembly.Location;
                if (string.IsNullOrEmpty(location)) return null;

                string[] parts = location.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                for (int i = 0; i < parts.Length - 1; i++)
                    if (string.Equals(parts[i], "Modules", StringComparison.OrdinalIgnoreCase))
                        return parts[i + 1];
            }
            catch (NotSupportedException)
            {
                // dynamic assemblies have no location
            }
            return null;
        }
    }
}
