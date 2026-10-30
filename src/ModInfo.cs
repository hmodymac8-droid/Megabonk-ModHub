using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace ModHub;

internal enum Loader { BepInEx, MelonLoader, Unknown }

/// <summary>What a mod DLL says about itself, read from its metadata without loading it.</summary>
internal sealed class PluginMeta
{
    public string Guid, Name, Version;
    public List<string> Dependencies = new(); // GUIDs of hard dependencies
    public Loader Loader;
    public bool ForIl2Cpp;                   // built for BepInEx 6 IL2CPP games (like Megabonk)
    public string Entry;                     // where it sits in the zip (set by the installer)
}

/// <summary>
/// Reads [BepInPlugin] / [BepInDependency] (or MelonLoader's [MelonInfo]) from a DLL with Mono.Cecil,
/// so disabled, freshly downloaded and not-yet-loaded mods can be named and checked. Nothing is executed.
/// </summary>
internal static class ModInfo
{
    public static PluginMeta Read(Stream dll)
    {
        try
        {
            using var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { ReadSymbols = false, InMemory = true });
            var refs = asm.MainModule.AssemblyReferences.Select(r => r.Name).ToList();
            var meta = new PluginMeta
            {
                Loader = refs.Any(r => r.StartsWith("BepInEx")) ? Loader.BepInEx
                    : refs.Any(r => r == "MelonLoader") ? Loader.MelonLoader
                    : Loader.Unknown,
                ForIl2Cpp = refs.Any(r => r == "BepInEx.Unity.IL2CPP" || r.StartsWith("Il2CppInterop") || r == "Il2Cppmscorlib"),
            };

            foreach (var type in asm.MainModule.Types)
                foreach (var attr in type.CustomAttributes)
                {
                    var name = attr.AttributeType.Name;
                    if (name == "BepInPlugin" && attr.ConstructorArguments.Count >= 3)
                    {
                        meta.Guid ??= attr.ConstructorArguments[0].Value as string;
                        meta.Name ??= attr.ConstructorArguments[1].Value as string;
                        meta.Version ??= attr.ConstructorArguments[2].Value as string;
                    }
                    else if (name == "BepInDependency" && attr.ConstructorArguments.Count >= 1)
                    {
                        // Second argument (if any) is the flags: 1 = hard dependency (the default), 2 = soft.
                        bool soft = attr.ConstructorArguments.Count >= 2 && Convert.ToInt32(attr.ConstructorArguments[1].Value) == 2;
                        if (!soft && attr.ConstructorArguments[0].Value is string dep) meta.Dependencies.Add(dep);
                    }
                }

            // MelonLoader: [assembly: MelonInfo(typeof(Mod), "Name", "Version", "Author")]
            foreach (var attr in asm.CustomAttributes)
                if (attr.AttributeType.Name == "MelonInfoAttribute" && attr.ConstructorArguments.Count >= 3)
                {
                    meta.Name ??= attr.ConstructorArguments[1].Value as string;
                    meta.Version ??= attr.ConstructorArguments[2].Value as string;
                }
            return meta;
        }
        catch
        {
            return null; // native DLL or not .NET
        }
    }

    public static PluginMeta Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(fs);
        }
        catch { return null; }
    }
}
