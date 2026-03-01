using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;
using Newtonsoft.Json;


partial class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: Compile <inputPath>");
            return 1;
        }

        var inputRoot = Path.GetFullPath(args[0]);
        if (!Directory.Exists(inputRoot))
        {
            Console.Error.WriteLine("directory not found");
            return 1;
        }

        string sourceRoot = Path.GetFullPath(Directory.Exists(Path.Combine(inputRoot, "src"))
            ? Path.Combine(inputRoot, "src")
            : inputRoot);
        var objRoot = Path.GetFullPath(Path.Combine(inputRoot, "obj"));
        var binRoot = Path.GetFullPath(Path.Combine(inputRoot, "bin"));
        var packagesRoot = Path.GetFullPath(Path.Combine(inputRoot, "packages"));

        var runRoot = Path.Combine(inputRoot, "run");

        string packageName = Path.GetFileName(inputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        bool isStdPackage = packageName.Equals("STD", StringComparison.OrdinalIgnoreCase);

        if (isStdPackage)
        {
            try
            {
                Directory.CreateDirectory(binRoot);
                var srcRoot = Path.GetFullPath(Path.Combine(inputRoot, "src"));
                string stdAllTypesPath = Path.Combine(srcRoot, "all_types.h");
                BuildSTD(binRoot, packageName, stdAllTypesPath);
                string runtimeInclude = Path.GetFullPath(Path.Combine(inputRoot, "..", "Runtime", "include"));
                var stdSources = Directory.Exists(srcRoot)
                    ? Directory.EnumerateFiles(srcRoot, "*.c", SearchOption.AllDirectories)
                    : Array.Empty<string>();
                BuildPackageDll(packageName, inputRoot, [runtimeInclude, srcRoot], stdSources, binRoot);
                string exeDir = GetExecutableDirectory();
                string exeBinDir = Path.Combine(exeDir, "bin");
                CopyDirectoryRecursive(binRoot, exeBinDir);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }

        if (!Directory.Exists(sourceRoot))
        {
            Console.Error.WriteLine("Source directory not found");
            return 1;
        }

        Directory.CreateDirectory(packagesRoot);

        string executableDir = GetExecutableDirectory();
        string exeStdBinDir = Path.Combine(executableDir, "bin");
        string exeStdBin = Path.Combine(exeStdBinDir, "STD.bin");
        string exeStdDll = Path.Combine(exeStdBinDir, "STD.dll");
        if (!File.Exists(exeStdBin) || !File.Exists(exeStdDll))
        {
            Console.Error.WriteLine($"STD package not found next to executable: {exeStdBinDir}");
            return 1;
        }
        string stdPackageDir = Path.Combine(packagesRoot, "STD");
        Directory.CreateDirectory(stdPackageDir);
        File.Copy(exeStdBin, Path.Combine(stdPackageDir, "STD.bin"), true);
        File.Copy(exeStdDll, Path.Combine(stdPackageDir, "STD.dll"), true);

        Directory.CreateDirectory(binRoot);

        if (Directory.Exists(objRoot))
            Directory.Delete(objRoot, true);
        Directory.CreateDirectory(objRoot);
        Directory.CreateDirectory(binRoot);

        var results = new List<FileParseResult>();
        Stopwatch timer = new();
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.dim", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            timer.Start();
            var tokens = Tokenizer.Tokenize(text);
            var result = Parser.Parse(tokens, path);
            timer.Stop();

            var relativePath = Path.GetRelativePath(sourceRoot, path);
            var jsonOutPath = Path.Combine(objRoot, relativePath) + ".json";
            Directory.CreateDirectory(Path.GetDirectoryName(jsonOutPath)!);

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented
            };

            string json = JsonConvert.SerializeObject(result, settings);
            File.WriteAllText(jsonOutPath, json);

            results.AddRange(result);
        }

        var compiledClasses = results.SelectMany(r => r.Classes).ToList();
        var compiledInterfaces = results.SelectMany(r => r.Interfaces).ToList();
        var compiledNamespaces = new HashSet<string>(compiledClasses.Select(c => c.Namespace), StringComparer.OrdinalIgnoreCase);
        var importedClasses = new List<Class>();
        var importedNamespaces = results
            .SelectMany(r => r.ImportedNamespaces)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var packagesByNamespace = new Dictionary<string, PackageSpec>(StringComparer.OrdinalIgnoreCase);
        packagesByNamespace["STD"] = ResolvePackageSpec("STD", stdPackageDir);

        foreach (var packageDir in Directory.EnumerateDirectories(packagesRoot))
        {
            string name = Path.GetFileName(packageDir);
            if (name.Equals("STD", StringComparison.OrdinalIgnoreCase))
                continue;
            packagesByNamespace[name] = ResolvePackageSpec(name, packageDir);
        }

        var requiredImports = importedNamespaces
            .Where(ns => !compiledNamespaces.Contains(ns))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var ns in requiredImports)
        {
            if (!packagesByNamespace.TryGetValue(ns, out var pkg))
            {
                Console.Error.WriteLine($"Missing package for imported namespace: {ns}");
                return 1;
            }
            importedClasses.AddRange(LoadClassesFromMeta(pkg.BinPath));
        }
        var allClassesByName = new Dictionary<string, Class>(StringComparer.OrdinalIgnoreCase);
        foreach (var cls in compiledClasses)
            allClassesByName[$"{cls.Namespace} {cls.Name}"] = cls;
        foreach (var cls in importedClasses)
            if (!allClassesByName.ContainsKey($"{cls.Namespace} {cls.Name}"))
                allClassesByName[$"{cls.Namespace} {cls.Name}"] = cls;
        var allClasses = allClassesByName.Values.ToList();

        var allHeaderPath = Path.Combine(objRoot, "all.h");
        var typesHeaderPath = Path.Combine(objRoot, "all_types.h");
        string typesHeaderIncludeFromObj = Path.GetRelativePath(objRoot, typesHeaderPath).Replace('\\', '/');
        var moduleInfos = new List<(FileParseResult Result, string SourcePath, string IncludeAllPath)>();

        foreach (var result in results)
        {
            var resultFullPath = Path.GetFullPath(result.Path);

            var relativeBasePath = Path.GetRelativePath(sourceRoot, resultFullPath);
            var outBasePath = Path.Combine(objRoot, relativeBasePath);

            string sourcePath = $"{outBasePath}.c";

            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);

            string sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;

            string includeAllPath = Path.GetRelativePath(sourceDir, allHeaderPath).Replace('\\', '/');
            moduleInfos.Add((result, sourcePath, includeAllPath));
        }

        var allHeader = new StringBuilder();
        allHeader.AppendLine("#pragma once");
        allHeader.AppendLine("#define FUNCTION_VAR_EXT");
        allHeader.AppendLine("#include \"runtime.h\"");
        allHeader.AppendLine($"#include \"{typesHeaderIncludeFromObj}\"");
        allHeader.AppendLine("extern RuntimeState *state;");
        foreach (var cls in allClasses)
            allHeader.AppendLine($"extern Definition *def_{cls.Namespace}_{cls.Name};");
        foreach (var cls in allClasses)
            allHeader.AppendLine($"Definition *get_{cls.Namespace}_{cls.Name}(void);");
        allHeader.AppendLine();
        allHeader.AppendLine("static inline Definition *find_definition(const char *namespace_, const char *name)");
        allHeader.AppendLine("{");
        allHeader.AppendLine("    if (!state)");
        allHeader.AppendLine("        return NULL;");
        allHeader.AppendLine("    for (int i = 0; i < arrlen(state->definitions); i++)");
        allHeader.AppendLine("    {");
        allHeader.AppendLine("        Definition *def = state->definitions[i];");
        allHeader.AppendLine("        if (strcmp(def->namespace_, namespace_) == 0 && strcmp(def->name, name) == 0)");
        allHeader.AppendLine("            return def;");
        allHeader.AppendLine("    }");
        allHeader.AppendLine("    return NULL;");
        allHeader.AppendLine("}");
        allHeader.AppendLine();
        allHeader.AppendLine("static inline Definition *ensure_definition(Definition **cache, const char *namespace_, const char *name)");
        allHeader.AppendLine("{");
        allHeader.AppendLine("    if (*cache)");
        allHeader.AppendLine("        return *cache;");
        allHeader.AppendLine("    Definition *def = find_definition(namespace_, name);");
        allHeader.AppendLine("    if (!def)");
        allHeader.AppendLine("    {");
        allHeader.AppendLine("        printf(\"Missing definition %s %s\\n\", namespace_, name);");
        allHeader.AppendLine("        abort();");
        allHeader.AppendLine("    }");
        allHeader.AppendLine("    *cache = def;");
        allHeader.AppendLine("    return def;");
        allHeader.AppendLine("}");
        var importedNamespacesByClass = new Dictionary<string, List<string>>();
        foreach (var result in results)
            foreach (var cls in result.Classes)
                importedNamespacesByClass[$"{cls.Namespace} {cls.Name}"] = result.ImportedNamespaces;
        timer.Start();
        string typesHeader = Transpiler.TranspileTypes(allClasses, compiledInterfaces, importedNamespacesByClass);
        timer.Stop();
        File.WriteAllText(typesHeaderPath, typesHeader);

        var moduleOutputs = new List<(string Header, string Source, string SourcePath)>();
        foreach (var module in moduleInfos)
        {
            timer.Start();
            (string header, string source) =
                Transpiler.TranspileModule(module.Result.Classes, compiledInterfaces, allClasses, module.Result.ImportedNamespaces, module.Result.UsingTypes, module.IncludeAllPath);
            timer.Stop();
            moduleOutputs.Add((header, source, module.SourcePath));
        }

        foreach (var module in moduleOutputs)
        {
            if (!string.IsNullOrWhiteSpace(module.Header))
            {
                allHeader.AppendLine();
                allHeader.AppendLine(module.Header);
            }
        }
        File.WriteAllText(allHeaderPath, allHeader.ToString());

        foreach (var module in moduleOutputs)
            File.WriteAllText(module.SourcePath, module.Source);

        string definitionsSource = Transpiler.TranspileDefinitions(compiledClasses, allClasses, "all.h");
        File.WriteAllText(Path.Combine(objRoot, "definitions.c"), definitionsSource);

        void ResolveClassTypeNamespace(ClassType classType)
        {
            if (!string.IsNullOrWhiteSpace(classType.Namespace))
                return;
            var interfaceCandidates = compiledInterfaces.Where(i => i.Name == classType.Name).ToList();
            if (interfaceCandidates.Count == 1)
            {
                classType.Namespace = interfaceCandidates[0].Namespace;
                return;
            }
            var classCandidates = allClasses.Where(c => c.Name == classType.Name).ToList();
            if (classCandidates.Count == 1)
            {
                classType.Namespace = classCandidates[0].Namespace;
                return;
            }
            if (classCandidates.Count > 1)
            {
                var preferred = classCandidates.FirstOrDefault(c => c.Namespace.Equals(packageName, StringComparison.OrdinalIgnoreCase));
                if (preferred != null)
                {
                    classType.Namespace = preferred.Namespace;
                    return;
                }
            }
        }
        void ResolveTypeNamespace(Type type)
        {
            if (type is ClassType classType)
                ResolveClassTypeNamespace(classType);
        }
        foreach (var cls in compiledClasses)
        {
            if (cls.Base != null)
                ResolveClassTypeNamespace(cls.Base);
            foreach (var iface in cls.Interfaces ?? [])
                ResolveClassTypeNamespace(iface);
            foreach (var method in cls.Methods)
            {
                foreach (var arg in method.Arguments)
                    ResolveTypeNamespace(arg);
                if (method.ReturnType != null)
                    ResolveTypeNamespace(method.ReturnType);
            }
            foreach (var field in cls.StaticFields)
                ResolveTypeNamespace(field.Type);
            foreach (var field in cls.InstanceFields)
                ResolveTypeNamespace(field.Type);
        }
        using BinaryWriter writer = new BinaryWriter(File.OpenWrite(Path.Combine(binRoot, $"{packageName}.bin")));
        writer.Write(compiledClasses.Count);
        foreach (var cls in compiledClasses)
            cls.BinaryOut(writer, allClasses);

        string runtimeIncludeDir = Path.GetFullPath(Path.Combine(inputRoot, "..", "Runtime", "include"));
        string objIncludeDir = objRoot;
        string packagesIncludeDir = packagesRoot;
        var generatedSources = Directory.EnumerateFiles(objRoot, "*.c", SearchOption.AllDirectories);
        Stopwatch buildTimer = new();
        buildTimer.Start();
        BuildPackageDll(packageName, inputRoot, [runtimeIncludeDir, objIncludeDir, packagesIncludeDir], generatedSources, binRoot);
        buildTimer.Stop();
        PopulateRunDirectory(binRoot, packagesRoot, runRoot);
        Console.WriteLine($"Compile time: {(double)timer.ElapsedTicks / (double)Stopwatch.Frequency * 1000}ms");
        Console.WriteLine($"Build time: {(double)buildTimer.ElapsedTicks / (double)Stopwatch.Frequency * 1000}ms");
        return 0;
    }
}
