using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;
using Newtonsoft.Json;

class Program
{
    sealed class PackageSpec
    {
        public string Name = "";
        public string Root = "";
        public string BinPath = "";
    }

    static List<Class> LoadClassesFromMeta(string metaPath)
    {
        using BinaryReader reader = new BinaryReader(File.OpenRead(metaPath));
        int count = reader.ReadInt32();
        var classes = new List<Class>(count);
        for (int i = 0; i < count; i++)
            classes.Add(Class.BinaryIn(reader));
        return classes;
    }

    static string? FindFileInDir(string root, params string[] names)
    {
        foreach (var name in names)
        {
            string path = Path.Combine(root, name);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    static string GetExecutableDirectory()
    {
        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    static int RunProcess(string fileName, IEnumerable<string> args, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        Console.WriteLine($"{fileName} {string.Join(' ', args)}");
        using var process = Process.Start(info) ?? throw new Exception($"Failed to start {fileName}");
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.Error.WriteLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }

    static void BuildPackageDll(string packageName, string workingDir, IEnumerable<string> includeDirs, IEnumerable<string> sourceFiles, string outputDir)
    {
        var sources = sourceFiles.Where(File.Exists).ToList();
        if (sources.Count == 0)
            throw new Exception($"No C sources found for {packageName} in {workingDir}");

        Directory.CreateDirectory(outputDir);

        var args = new List<string>
        {
            "/nologo",
            "/std:c17",
            "/W4",
            "/Ox",
            "/arch:AVX2",
        };
        foreach (var include in includeDirs.Distinct())
        {
            args.Add("/I");
            args.Add(include);
        }
        args.AddRange(sources);
        args.Add("/LD");
        args.Add($"/Fe:{Path.Combine(outputDir, $"{packageName}.dll")}");
        args.Add("/link");
        args.Add($"/IMPLIB:{Path.Combine(outputDir, $"{packageName}.lib")}");
        args.Add("/PDB:NONE");
        args.Add("/DEBUG:NONE");

        int exitCode = RunProcess("clang-cl", args, workingDir);
        if (exitCode != 0)
            throw new Exception($"clang-cl failed for {packageName} with exit code {exitCode}");
    }

    static PackageSpec ResolvePackageSpec(string name, string root)
    {
        string resolvedRoot = Path.GetFullPath(root);
        if (!Directory.Exists(resolvedRoot))
            throw new Exception($"Package root not found: {resolvedRoot}");

        string? bin = FindFileInDir(resolvedRoot, $"{name}.bin");
        if (bin == null)
            throw new Exception($"Package bin not found for {name} in {resolvedRoot}");

        return new PackageSpec
        {
            Name = name,
            Root = resolvedRoot,
            BinPath = bin
        };
    }

    static void CopyPackageArtifacts(string packageName, string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        string sourceBin = Path.Combine(sourceDir, $"{packageName}.bin");
        string sourceHeader = Path.Combine(sourceDir, $"{packageName}.h");
        string destBin = Path.Combine(destinationDir, $"{packageName}.bin");
        string destHeader = Path.Combine(destinationDir, $"{packageName}.h");

        if (!File.Exists(sourceBin))
            throw new Exception($"Package bin not found: {sourceBin}");
        if (!File.Exists(sourceHeader))
            throw new Exception($"Package header not found: {sourceHeader}");

        File.Copy(sourceBin, destBin, true);
        File.Copy(sourceHeader, destHeader, true);
    }

    static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string destPath = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, true);
        }
    }

    static void PopulateRunDirectory(string binRoot, string packagesRoot, string runRoot)
    {
        if (Directory.Exists(runRoot))
            Directory.Delete(runRoot, true);
        Directory.CreateDirectory(runRoot);

        void CopyDllToRun(string dllPath)
        {
            string dest = Path.Combine(runRoot, Path.GetFileName(dllPath));
            File.Copy(dllPath, dest, true);
        }

        foreach (var dll in Directory.EnumerateFiles(binRoot, "*.dll", SearchOption.TopDirectoryOnly))
            CopyDllToRun(dll);

        if (!Directory.Exists(packagesRoot))
            return;

        foreach (var packageDir in Directory.EnumerateDirectories(packagesRoot))
        {
            foreach (var dll in Directory.EnumerateFiles(packageDir, "*.dll", SearchOption.TopDirectoryOnly))
                CopyDllToRun(dll);
        }
    }


    static void BuildSTD(string binRoot, string packageName, string allTypesPath)
    {
        Class STD_String = new Class(
            "STD",
            "String",
            0,
            [
                new Method(
                "New",
                [new ValueType("cstr")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromString",
                [new ClassType("STD", "String")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "Clone",
                [new ClassType("STD", "String")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "Concat",
                [new ClassType("STD", "String"), new ClassType("STD", "String")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),

            new Method(
                "FromBool",
                [new ValueType("bool")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromInt",
                [new ValueType("int")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromUInt",
                [new ValueType("uint")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromLong",
                [new ValueType("long")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromULong",
                [new ValueType("ulong")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromFloat",
                [new ValueType("float")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromDouble",
                [new ValueType("double")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromByte",
                [new ValueType("byte")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromSByte",
                [new ValueType("sbyte")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromChar",
                [new ValueType("char")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromShort",
                [new ValueType("short")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "FromUShort",
                [new ValueType("ushort")],
                new ClassType("STD", "String"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),

            new Method(
                "Length",
                [new ClassType("STD", "String")],
                new ValueType("int"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "IsEmpty",
                [new ClassType("STD", "String")],
                new ValueType("bool"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "Equals",
                [new ClassType("STD", "String"), new ClassType("STD", "String")],
                new ValueType("bool"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "Compare",
                [new ClassType("STD", "String"), new ClassType("STD", "String")],
                new ValueType("int"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "Box",
                [new ClassType("STD", "String") { Nullable = true }],
                new ClassType("STD", "Any"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "Unbox",
                [new ClassType("STD", "Any") { Nullable = true }],
                new ClassType("STD", "String") { Nullable = true },
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            )
            ],
            new List<Field>(),
            new List<Field>()
        );

        Class STD_Any = new Class(
            "STD",
            "Any",
            0,
            [],
            new List<Field>(),
            new List<Field>()
            {
                new Field("value", new ValueType("inst"), 0)
            }
        );

        Class STD_List = new Class(
            "STD",
            "List",
            0,
            [
                new Method("New", [], new ClassType("STD", "List"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Add", [new ClassType("STD", "List"), new ClassType("STD", "Any") with { Nullable = true }], null, new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Count", [new ClassType("STD", "List")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Get", [new ClassType("STD", "List"), new ValueType("int")], new ClassType("STD", "Any") { Nullable = true }, new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Set", [new ClassType("STD", "List"), new ValueType("int"), new ClassType("STD", "Any") { Nullable = true }], null, new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Pop", [new ClassType("STD", "List")], new ClassType("STD", "Any") with { Nullable = true }, new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("RemoveAt", [new ClassType("STD", "List"), new ValueType("int")], null, new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Clear", [new ClassType("STD", "List")], null, new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0)
            ],
            new List<Field>(),
            new List<Field>()
        );

        Class STD_STD = new Class(
            "STD",
            "STD",
            0,
            [
            new Method(
                "Print",
                [new ClassType("STD", "String")],
                null,
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            ),
            new Method(
                "TimeMS",
                [],
                new ValueType("double"),
                new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0),
                0
            )
            ],
            new List<Field>(),
            new List<Field>()
        );

        Class STD_Math = new Class(
            "STD",
            "Math",
0,
            [
                new Method("Sqrt", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Pow", [new ValueType("double"), new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Sin", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Cos", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Tan", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Asin", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Acos", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Atan", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Atan2", [new ValueType("double"), new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Exp", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Log", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Log10", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Floor", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Ceil", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Round", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Fmod", [new ValueType("double"), new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Abs", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Min", [new ValueType("double"), new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Max", [new ValueType("double"), new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0)
            ],
            new List<Field>(),
            new List<Field>()
        );

        Class STD_MathF = new Class(
            "STD",
            "MathF",
0,
            [
                new Method("Sqrt", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Pow", [new ValueType("float"), new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Sin", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Cos", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Tan", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Asin", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Acos", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Atan", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Atan2", [new ValueType("float"), new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Exp", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Log", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Log10", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Floor", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Ceil", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Round", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Fmod", [new ValueType("float"), new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("Abs", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Min", [new ValueType("float"), new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("Max", [new ValueType("float"), new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0)
            ],
            new List<Field>(),
            new List<Field>()
        );

        Class STD_MathI = new Class(
            "STD",
            "MathI",
0,
            [
                new Method("MinInt", [new ValueType("int"), new ValueType("int")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxInt", [new ValueType("int"), new ValueType("int")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampInt", [new ValueType("int"), new ValueType("int"), new ValueType("int")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("AbsInt", [new ValueType("int")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("MinUInt", [new ValueType("uint"), new ValueType("uint")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxUInt", [new ValueType("uint"), new ValueType("uint")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampUInt", [new ValueType("uint"), new ValueType("uint"), new ValueType("uint")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("MinLong", [new ValueType("long"), new ValueType("long")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxLong", [new ValueType("long"), new ValueType("long")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampLong", [new ValueType("long"), new ValueType("long"), new ValueType("long")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("AbsLong", [new ValueType("long")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("MinULong", [new ValueType("ulong"), new ValueType("ulong")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxULong", [new ValueType("ulong"), new ValueType("ulong")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampULong", [new ValueType("ulong"), new ValueType("ulong"), new ValueType("ulong")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("MinShort", [new ValueType("short"), new ValueType("short")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxShort", [new ValueType("short"), new ValueType("short")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampShort", [new ValueType("short"), new ValueType("short"), new ValueType("short")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("AbsShort", [new ValueType("short")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("MinUShort", [new ValueType("ushort"), new ValueType("ushort")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxUShort", [new ValueType("ushort"), new ValueType("ushort")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampUShort", [new ValueType("ushort"), new ValueType("ushort"), new ValueType("ushort")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("MinSByte", [new ValueType("sbyte"), new ValueType("sbyte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxSByte", [new ValueType("sbyte"), new ValueType("sbyte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampSByte", [new ValueType("sbyte"), new ValueType("sbyte"), new ValueType("sbyte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("AbsSByte", [new ValueType("sbyte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                new Method("MinByte", [new ValueType("byte"), new ValueType("byte")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("MaxByte", [new ValueType("byte"), new ValueType("byte")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ClampByte", [new ValueType("byte"), new ValueType("byte"), new ValueType("byte")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0)
            ],
            new List<Field>(),
            new List<Field>()
        );

        Class STD_MathC = new Class(
            "STD",
            "MathC",
0,
            [
                // BoolFrom*
                new Method("BoolFromBool", [new ValueType("bool")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromInt", [new ValueType("int")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromUInt", [new ValueType("uint")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromLong", [new ValueType("long")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromULong", [new ValueType("ulong")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromFloat", [new ValueType("float")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromDouble", [new ValueType("double")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromByte", [new ValueType("byte")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromSByte", [new ValueType("sbyte")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromChar", [new ValueType("char")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromShort", [new ValueType("short")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("BoolFromUShort", [new ValueType("ushort")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // IntFrom*
                new Method("IntFromBool", [new ValueType("bool")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromInt", [new ValueType("int")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromUInt", [new ValueType("uint")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromLong", [new ValueType("long")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromULong", [new ValueType("ulong")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromFloat", [new ValueType("float")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromDouble", [new ValueType("double")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromByte", [new ValueType("byte")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromSByte", [new ValueType("sbyte")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromChar", [new ValueType("char")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromShort", [new ValueType("short")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("IntFromUShort", [new ValueType("ushort")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // UIntFrom*
                new Method("UIntFromBool", [new ValueType("bool")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromInt", [new ValueType("int")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromUInt", [new ValueType("uint")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromLong", [new ValueType("long")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromULong", [new ValueType("ulong")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromFloat", [new ValueType("float")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromDouble", [new ValueType("double")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromByte", [new ValueType("byte")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromSByte", [new ValueType("sbyte")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromChar", [new ValueType("char")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromShort", [new ValueType("short")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UIntFromUShort", [new ValueType("ushort")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // LongFrom*
                new Method("LongFromBool", [new ValueType("bool")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromInt", [new ValueType("int")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromUInt", [new ValueType("uint")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromLong", [new ValueType("long")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromULong", [new ValueType("ulong")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromFloat", [new ValueType("float")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromDouble", [new ValueType("double")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromByte", [new ValueType("byte")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromSByte", [new ValueType("sbyte")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromChar", [new ValueType("char")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromShort", [new ValueType("short")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("LongFromUShort", [new ValueType("ushort")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ULongFrom*
                new Method("ULongFromBool", [new ValueType("bool")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromInt", [new ValueType("int")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromUInt", [new ValueType("uint")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromLong", [new ValueType("long")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromULong", [new ValueType("ulong")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromFloat", [new ValueType("float")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromDouble", [new ValueType("double")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromByte", [new ValueType("byte")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromSByte", [new ValueType("sbyte")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromChar", [new ValueType("char")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromShort", [new ValueType("short")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ULongFromUShort", [new ValueType("ushort")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // FloatFrom*
                new Method("FloatFromBool", [new ValueType("bool")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromInt", [new ValueType("int")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromUInt", [new ValueType("uint")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromLong", [new ValueType("long")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromULong", [new ValueType("ulong")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromFloat", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromDouble", [new ValueType("double")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromByte", [new ValueType("byte")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromSByte", [new ValueType("sbyte")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromChar", [new ValueType("char")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromShort", [new ValueType("short")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("FloatFromUShort", [new ValueType("ushort")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // DoubleFrom*
                new Method("DoubleFromBool", [new ValueType("bool")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromInt", [new ValueType("int")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromUInt", [new ValueType("uint")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromLong", [new ValueType("long")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromULong", [new ValueType("ulong")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromFloat", [new ValueType("float")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromDouble", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromByte", [new ValueType("byte")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromSByte", [new ValueType("sbyte")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromChar", [new ValueType("char")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromShort", [new ValueType("short")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("DoubleFromUShort", [new ValueType("ushort")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ByteFrom*
                new Method("ByteFromBool", [new ValueType("bool")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromInt", [new ValueType("int")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromUInt", [new ValueType("uint")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromLong", [new ValueType("long")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromULong", [new ValueType("ulong")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromFloat", [new ValueType("float")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromDouble", [new ValueType("double")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromByte", [new ValueType("byte")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromSByte", [new ValueType("sbyte")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromChar", [new ValueType("char")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromShort", [new ValueType("short")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ByteFromUShort", [new ValueType("ushort")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // SByteFrom*
                new Method("SByteFromBool", [new ValueType("bool")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromInt", [new ValueType("int")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromUInt", [new ValueType("uint")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromLong", [new ValueType("long")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromULong", [new ValueType("ulong")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromFloat", [new ValueType("float")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromDouble", [new ValueType("double")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromByte", [new ValueType("byte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromSByte", [new ValueType("sbyte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromChar", [new ValueType("char")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromShort", [new ValueType("short")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("SByteFromUShort", [new ValueType("ushort")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // CharFrom*
                new Method("CharFromBool", [new ValueType("bool")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromInt", [new ValueType("int")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromUInt", [new ValueType("uint")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromLong", [new ValueType("long")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromULong", [new ValueType("ulong")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromFloat", [new ValueType("float")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromDouble", [new ValueType("double")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromByte", [new ValueType("byte")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromSByte", [new ValueType("sbyte")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromChar", [new ValueType("char")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromShort", [new ValueType("short")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("CharFromUShort", [new ValueType("ushort")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ShortFrom*
                new Method("ShortFromBool", [new ValueType("bool")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromInt", [new ValueType("int")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromUInt", [new ValueType("uint")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromLong", [new ValueType("long")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromULong", [new ValueType("ulong")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromFloat", [new ValueType("float")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromDouble", [new ValueType("double")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromByte", [new ValueType("byte")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromSByte", [new ValueType("sbyte")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromChar", [new ValueType("char")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromShort", [new ValueType("short")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ShortFromUShort", [new ValueType("ushort")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // UShortFrom*
                new Method("UShortFromBool", [new ValueType("bool")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromInt", [new ValueType("int")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromUInt", [new ValueType("uint")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromLong", [new ValueType("long")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromULong", [new ValueType("ulong")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromFloat", [new ValueType("float")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromDouble", [new ValueType("double")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromByte", [new ValueType("byte")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromSByte", [new ValueType("sbyte")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromChar", [new ValueType("char")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromShort", [new ValueType("short")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("UShortFromUShort", [new ValueType("ushort")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToBool
                new Method("ToBool", [new ValueType("bool")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("int")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("uint")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("long")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("ulong")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("float")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("double")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("byte")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("sbyte")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("char")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("short")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToBool", [new ValueType("ushort")], new ValueType("bool"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToInt
                new Method("ToInt", [new ValueType("bool")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("int")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("uint")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("long")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("ulong")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("float")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("double")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("byte")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("sbyte")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("char")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("short")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToInt", [new ValueType("ushort")], new ValueType("int"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToUInt
                new Method("ToUInt", [new ValueType("bool")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("int")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("uint")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("long")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("ulong")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("float")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("double")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("byte")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("sbyte")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("char")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("short")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUInt", [new ValueType("ushort")], new ValueType("uint"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToLong
                new Method("ToLong", [new ValueType("bool")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("int")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("uint")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("long")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("ulong")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("float")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("double")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("byte")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("sbyte")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("char")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("short")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToLong", [new ValueType("ushort")], new ValueType("long"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToULong
                new Method("ToULong", [new ValueType("bool")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("int")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("uint")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("long")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("ulong")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("float")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("double")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("byte")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("sbyte")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("char")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("short")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToULong", [new ValueType("ushort")], new ValueType("ulong"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToFloat
                new Method("ToFloat", [new ValueType("bool")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("int")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("uint")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("long")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("ulong")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("float")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("double")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("byte")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("sbyte")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("char")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("short")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToFloat", [new ValueType("ushort")], new ValueType("float"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToDouble
                new Method("ToDouble", [new ValueType("bool")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("int")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("uint")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("long")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("ulong")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("float")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("double")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("byte")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("sbyte")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("char")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("short")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToDouble", [new ValueType("ushort")], new ValueType("double"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToByte
                new Method("ToByte", [new ValueType("bool")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("int")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("uint")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("long")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("ulong")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("float")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("double")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("byte")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("sbyte")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("char")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("short")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToByte", [new ValueType("ushort")], new ValueType("byte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToSByte
                new Method("ToSByte", [new ValueType("bool")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("int")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("uint")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("long")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("ulong")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("float")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("double")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("byte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("sbyte")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("char")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("short")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToSByte", [new ValueType("ushort")], new ValueType("sbyte"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToChar
                new Method("ToChar", [new ValueType("bool")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("int")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("uint")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("long")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("ulong")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("float")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("double")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("byte")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("sbyte")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("char")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("short")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToChar", [new ValueType("ushort")], new ValueType("char"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToShort
                new Method("ToShort", [new ValueType("bool")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("int")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("uint")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("long")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("ulong")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("float")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("double")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("byte")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("sbyte")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("char")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("short")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToShort", [new ValueType("ushort")], new ValueType("short"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToUShort
                new Method("ToUShort", [new ValueType("bool")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("int")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("uint")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("long")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("ulong")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("float")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("double")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("byte")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("sbyte")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("char")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("short")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToUShort", [new ValueType("ushort")], new ValueType("ushort"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),

                // ToString
                new Method("ToString", [new ValueType("bool")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("int")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("uint")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("long")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("ulong")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("float")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("double")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("byte")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("sbyte")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("char")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("short")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0),
                new Method("ToString", [new ValueType("ushort")], new ClassType("STD", "String"), new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0)
            ],
            new List<Field>(),
            new List<Field>()
        );

        List<Class> classes =
        [
            STD_String,
            STD_Any,
            STD_List,
            STD_STD,
            STD_Math,
            STD_MathF,
            STD_MathI,
            STD_MathC
        ];

        Directory.CreateDirectory(binRoot);

        using BinaryWriter writer = new BinaryWriter(File.OpenWrite(Path.Combine(binRoot, $"{packageName}.bin")));
        writer.Write(classes.Count);
        foreach (var cls in classes)
            cls.BinaryOut(writer, classes);

        if (!File.Exists(allTypesPath))
            throw new Exception($"STD all_types.h not found: {allTypesPath}");
    }
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
            allClassesByName[$"{cls.Namespace}::{cls.Name}"] = cls;
        foreach (var cls in importedClasses)
            if (!allClassesByName.ContainsKey($"{cls.Namespace}::{cls.Name}"))
                allClassesByName[$"{cls.Namespace}::{cls.Name}"] = cls;
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
        allHeader.AppendLine("        printf(\"Missing definition %s::%s\\n\", namespace_, name);");
        allHeader.AppendLine("        abort();");
        allHeader.AppendLine("    }");
        allHeader.AppendLine("    *cache = def;");
        allHeader.AppendLine("    return def;");
        allHeader.AppendLine("}");
        var importedNamespacesByClass = new Dictionary<string, List<string>>();
        foreach (var result in results)
            foreach (var cls in result.Classes)
                importedNamespacesByClass[$"{cls.Namespace}::{cls.Name}"] = result.ImportedNamespaces;
        timer.Start();
        string typesHeader = Transpiler.TranspileTypes(allClasses, importedNamespacesByClass);
        timer.Stop();
        File.WriteAllText(typesHeaderPath, typesHeader);

        var moduleOutputs = new List<(string Header, string Source, string SourcePath)>();
        foreach (var module in moduleInfos)
        {
            timer.Start();
            (string header, string source) =
                Transpiler.TranspileModule(module.Result.Classes, allClasses, module.Result.ImportedNamespaces, module.Result.UsingTypes, module.IncludeAllPath);
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
