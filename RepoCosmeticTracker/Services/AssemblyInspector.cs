using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RepoCosmeticTracker.Services
{
    public class ScannedTypeInfo
    {
        public string FullName { get; init; } = "";
        public string Kind { get; init; } = ""; // class / enum / interface
        public List<string> Members { get; init; } = new();
    }

    /// <summary>
    /// Loads Assembly-CSharp.dll in a metadata-only context — no game code
    /// is executed and the UnityEngine runtime doesn't need to be present —
    /// and looks for types whose name suggests they relate to cosmetics.
    /// This is how we find REPO's real data structure instead of guessing
    /// field names that would silently be wrong.
    /// </summary>
    public static class AssemblyInspector
    {
        private static readonly string[] DefaultKeywords =
        {
            "Cosmetic", "Hat", "Skin", "Outfit", "Hair", "Accessory", "Wearable", "Costume"
        };

        public static List<ScannedTypeInfo> ScanForCosmeticTypes(string assemblyCSharpPath, IEnumerable<string>? keywords = null)
        {
            string? managedDir = Path.GetDirectoryName(assemblyCSharpPath);
            if (managedDir == null)
                throw new DirectoryNotFoundException("Could not resolve the Managed folder from the given path.");

            string[] keys = (keywords ?? DefaultKeywords).Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
            if (keys.Length == 0)
                keys = DefaultKeywords;

            // Resolve purely against the DLLs shipped in REPO's own Managed
            // folder. Unity's Mono backend bundles a complete, self-contained
            // BCL there (including its own mscorlib.dll) — mixing in the
            // local .NET runtime's assemblies causes a duplicate-identity
            // crash since both define an assembly named "mscorlib".
            var dllPaths = Directory.GetFiles(managedDir, "*.dll").ToList();

            if (!dllPaths.Any(p => string.Equals(Path.GetFileName(p), "mscorlib.dll", StringComparison.OrdinalIgnoreCase)))
                throw new FileNotFoundException(
                    "mscorlib.dll wasn't found in the Managed folder — this doesn't look like a Mono-backend Unity build.",
                    Path.Combine(managedDir, "mscorlib.dll"));

            var resolver = new PathAssemblyResolver(dllPaths);
            using var mlc = new MetadataLoadContext(resolver, coreAssemblyName: "mscorlib");

            Assembly asm = mlc.LoadFromAssemblyPath(assemblyCSharpPath);

            var results = new List<ScannedTypeInfo>();

            foreach (Type type in SafeGetTypes(asm))
            {
                bool typeNameMatches = keys.Any(k => type.Name.Contains(k, StringComparison.OrdinalIgnoreCase));

                var allFields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                var allProps = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

                var matchingFields = allFields.Where(f => keys.Any(k => f.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
                var matchingProps = allProps.Where(p => keys.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();

                bool memberMatches = matchingFields.Count > 0 || matchingProps.Count > 0;
                if (!typeNameMatches && !memberMatches)
                    continue;

                var info = new ScannedTypeInfo
                {
                    FullName = type.FullName ?? type.Name,
                    Kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : "class"
                };

                if (type.IsEnum)
                {
                    info.Members.AddRange(
                        type.GetFields(BindingFlags.Public | BindingFlags.Static)
                            .Select(f => f.Name));
                }
                else if (typeNameMatches)
                {
                    // The class itself looks relevant — show everything, as before.
                    info.Members.AddRange(allFields.Select(f => $"field: {f.FieldType.Name} {f.Name}"));
                    info.Members.AddRange(allProps.Select(p => $"property: {p.PropertyType.Name} {p.Name}"));
                }
                else
                {
                    // The class name didn't match anything, but it owns a field/property
                    // that did — e.g. a generic "RunManager" with an "unlockedCosmetics"
                    // field. Show just the matches so a big unrelated class doesn't
                    // flood the output with hundreds of irrelevant members.
                    info.Members.AddRange(matchingFields.Select(f => $"field: {f.FieldType.Name} {f.Name}  <-- keyword match"));
                    info.Members.AddRange(matchingProps.Select(p => $"property: {p.PropertyType.Name} {p.Name}  <-- keyword match"));

                    int otherCount = allFields.Length + allProps.Length - matchingFields.Count - matchingProps.Count;
                    if (otherCount > 0)
                        info.Members.Add($"...({otherCount} other non-matching members omitted)");
                }

                results.Add(info);
            }

            return results;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Some types may fail to load if a dependency wasn't found;
                // return whatever did load successfully rather than failing outright.
                return ex.Types.Where(t => t is not null).Cast<Type>();
            }
        }
    }
}
