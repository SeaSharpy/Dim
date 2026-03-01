using System.Security.Cryptography;
using System.Text;
public static partial class Transpiler
{
    sealed class TranspileState
    {
        public StringBuilder HeaderBuilder { get; } = new();
        public StringBuilder SourceBuilder { get; } = new();
        public StringBuilder MethodsBuilder { get; } = new();
        public int Indent;
        public DictionaryStack<int, Type> Locals { get; } = new();
        public Dictionary<int, Type> Arguments { get; } = new();
        public Dictionary<int, (ClassType target, Expression source)> InlineIsBindings { get; } = new();
        public HashSet<int> VolatileLocals { get; set; } = new();
        public string FullName = "";
        public string FullClassName = "";
        public Type CurrentType = new ValueType("");
        public Type? ReturnType;
        public string Namespace = "";
        public string Name = "";
        public Class Current = null!;
        public List<string> ImportedNamespaces { get; set; } = new();
        public List<ClassType> UsingTypes { get; set; } = new();
        public int Blocks;
        public List<Class> Classes { get; set; } = new();
        public List<InterfaceDef> Interfaces { get; set; } = new();
    }

    [ThreadStatic]
    static TranspileState? currentState;

    static TranspileState State => currentState ?? throw new InvalidOperationException("Transpiler state is not initialized");

    static readonly string[] valueTypes = new[]
    {
        "bool", "int", "uint", "long", "ulong", "float", "double", "byte", "char", "short", "ushort", "cstr", "inst"
    };
    public static string TranslateType(Type type)
    {
        if (type is ValueType valueType)
        {
            switch (valueType.Name)
            {
                case "bool":
                    return "bool";
                case "int":
                    return "int32_t";
                case "uint":
                    return "uint32_t";
                case "long":
                    return "int64_t";
                case "ulong":
                    return "uint64_t";
                case "float":
                    return "float";
                case "double":
                    return "double";
                case "sbyte":
                    return "int8_t";
                case "byte":
                    return "uint8_t";
                case "char":
                    return "char";
                case "short":
                    return "int16_t";
                case "ushort":
                    return "uint16_t";
                case "cstr":
                    return "const char*";
                case "inst":
                    return "Instance*";
                default:
                    throw new Exception($"Invalid value type {valueType.Name} on line {valueType.Line}");
            }
        }
        else if (type is ClassType classType)
        {
            if (TryGetInterface(classType, out _))
                return "Instance*";
            Class @class = GetClass(classType);
            return $"{@class.Namespace}_{@class.Name}*";
        }
        else
            throw new Exception($"Invalid type on line {type.Line}");
    }

    static StringBuilder h => State.HeaderBuilder;
    static StringBuilder c => State.SourceBuilder;
    static StringBuilder m2 => State.MethodsBuilder;
    static int indent { get => State.Indent; set => State.Indent = value; }
    static DictionaryStack<int, Type> locals => State.Locals;
    static Dictionary<int, Type> arguments => State.Arguments;
    static Dictionary<int, (ClassType target, Expression source)> inlineIsBindings => State.InlineIsBindings;
    static HashSet<int> volatileLocals { get => State.VolatileLocals; set => State.VolatileLocals = value; }
    static string FullName { get => State.FullName; set => State.FullName = value; }
    static string FullClassName { get => State.FullClassName; set => State.FullClassName = value; }
    static Type CurrentType { get => State.CurrentType; set => State.CurrentType = value; }
    static Type? ReturnType { get => State.ReturnType; set => State.ReturnType = value; }
    static string Namespace { get => State.Namespace; set => State.Namespace = value; }
    static string Name { get => State.Name; set => State.Name = value; }
    static Class Current { get => State.Current; set => State.Current = value; }
    static List<string> ImportedNamespaces { get => State.ImportedNamespaces; set => State.ImportedNamespaces = value; }
    static List<ClassType> UsingTypes { get => State.UsingTypes; set => State.UsingTypes = value; }
    static int Blocks { get => State.Blocks; set => State.Blocks = value; }
    static List<Class> classes { get => State.Classes; set => State.Classes = value; }
    static List<InterfaceDef> interfaces { get => State.Interfaces; set => State.Interfaces = value; }

    static void H(string text = "") => h.Append(text);
    static void C(string text = "") => c.Append(text);
    static void M2(string text = "") => m2.Append(text);

    static void HL(string line = "") { h.AppendLine(line); }
    static void CL(string line = "") { c.AppendLine(line); c.Append(new string(' ', indent * 4)); }
    static void ML2(string line = "") { m2.AppendLine(line); }

    static void Both(string text = "")
    {
        H(text);
        C(text);
    }

    static void BothL(string line = "")
    {
        HL(line);
        CL(line);
    }

    static string BuildSignature(string returnType, string name, IReadOnlyList<Type> args)
    {
        var sb = new StringBuilder();
        sb.Append(returnType);
        sb.Append(' ');
        sb.Append(name);
        sb.Append('(');

        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(TranslateType(args[i]));
            sb.Append(" p_");
            sb.Append(i);
        }

        sb.Append(')');
        return sb.ToString();
    }

    static string BuildSignatureNoArgs(string returnType, string name)
        => $"{returnType} {name}(void)";
    static string BuildRealSignature(string returnType, string name, params string[] args)
        => $"{returnType} {name}({string.Join(", ", args)})";
    static string BuildFunctionPointerType(Method method)
    {
        string returnType = method.ReturnType != null ? TranslateType(method.ReturnType) : "void";
        List<string> argTypes = method.Arguments.Select(TranslateType).ToList();
        string args = argTypes.Count == 0 ? "void" : string.Join(", ", argTypes);
        return $"{returnType} (*)({args})";
    }
    static void CollectLocalIds(Expression expression, HashSet<int> ids)
    {
        switch (expression)
        {
            case LocalExpression localExpression:
                ids.Add(localExpression.ID);
                return;
            case ArgumentExpression:
            case NumberExpression:
            case StringExpression:
            case ClassExpression:
            case StaticFieldExpression:
            case NewExpression:
            case NilExpression:
                return;
            case CallStaticExpression callStaticExpression:
                foreach (var arg in callStaticExpression.Arguments)
                    CollectLocalIds(arg, ids);
                return;
            case CallExpression callExpression:
                foreach (var arg in callExpression.Arguments)
                    CollectLocalIds(arg, ids);
                return;
            case CallInstanceExpression callInstanceExpression:
                foreach (var arg in callInstanceExpression.Arguments)
                    CollectLocalIds(arg, ids);
                return;
            case InstanceFieldExpression instanceFieldExpression:
                CollectLocalIds(instanceFieldExpression.Instance, ids);
                return;
            case BinaryExpression binaryExpression:
                CollectLocalIds(binaryExpression.Left, ids);
                CollectLocalIds(binaryExpression.Right, ids);
                return;
            case UnaryExpression unaryExpression:
                CollectLocalIds(unaryExpression.Right, ids);
                return;
            case PostfixExpression postfixExpression:
                CollectLocalIds(postfixExpression.Left, ids);
                return;
            case IfExpression ifExpression:
                CollectLocalIds(ifExpression.Condition, ids);
                CollectLocalIds(ifExpression.True, ids);
                CollectLocalIds(ifExpression.False, ids);
                return;
            case IsExpression isExpression:
                CollectLocalIds(isExpression.Source, ids);
                CollectLocalIds(isExpression.True, ids);
                CollectLocalIds(isExpression.False, ids);
                return;
            case AsExpression asExpression:
                CollectLocalIds(asExpression.Source, ids);
                return;
            default:
                return;
        }
    }
    static bool ContainsTryStatement(Statement statement)
    {
        switch (statement)
        {
            case TryStatement:
                return true;
            case BlockStatement blockStatement:
                return blockStatement.Body.Any(ContainsTryStatement);
            case IfStatement ifStatement:
                return ContainsTryStatement(ifStatement.True) || (ifStatement.False != null && ContainsTryStatement(ifStatement.False));
            case WhileStatement whileStatement:
                return ContainsTryStatement(whileStatement.Body);
            case IsStatement isStatement:
                return ContainsTryStatement(isStatement.True) || (isStatement.False != null && ContainsTryStatement(isStatement.False));
            default:
                return false;
        }
    }
    static void CollectStatementLocalReads(Statement statement, HashSet<int> ids)
    {
        switch (statement)
        {
            case CallStatement callStatement:
                CollectLocalIds(callStatement.Expression, ids);
                return;
            case ReturnStatement returnStatement:
                if (returnStatement.Expression != null)
                    CollectLocalIds(returnStatement.Expression, ids);
                return;
            case AssignmentStatement assignmentStatement:
                CollectLocalIds(assignmentStatement.Expression, ids);
                return;
            case ThrowStatement throwStatement:
                CollectLocalIds(throwStatement.Expression, ids);
                return;
            case LocalAssignmentStatement localAssignmentStatement:
                CollectLocalIds(localAssignmentStatement.Expression, ids);
                return;
            case StaticFieldAssignmentStatement staticFieldAssignmentStatement:
                CollectLocalIds(staticFieldAssignmentStatement.StaticField, ids);
                CollectLocalIds(staticFieldAssignmentStatement.Expression, ids);
                return;
            case InstanceFieldAssignmentStatement instanceFieldAssignmentStatement:
                CollectLocalIds(instanceFieldAssignmentStatement.InstanceField, ids);
                CollectLocalIds(instanceFieldAssignmentStatement.Expression, ids);
                return;
            case WhileStatement whileStatement:
                CollectLocalIds(whileStatement.Condition, ids);
                CollectStatementLocalReads(whileStatement.Body, ids);
                return;
            case IfStatement ifStatement:
                CollectLocalIds(ifStatement.Condition, ids);
                CollectStatementLocalReads(ifStatement.True, ids);
                if (ifStatement.False != null)
                    CollectStatementLocalReads(ifStatement.False, ids);
                return;
            case IsStatement isStatement:
                CollectLocalIds(isStatement.Source, ids);
                CollectStatementLocalReads(isStatement.True, ids);
                if (isStatement.False != null)
                    CollectStatementLocalReads(isStatement.False, ids);
                return;
            case TryStatement tryStatement:
                CollectStatementLocalReads(tryStatement.Body, ids);
                foreach (var (_, callStatement) in tryStatement.Catchers)
                    CollectLocalIds(callStatement.Expression, ids);
                return;
            case BlockStatement blockStatement:
                foreach (var sub in blockStatement.Body)
                    CollectStatementLocalReads(sub, ids);
                return;
            default:
                return;
        }
    }
    static void CollectLocalReadsAfterTry(Statement statement, HashSet<int> ids, bool afterTry = false)
    {
        switch (statement)
        {
            case BlockStatement blockStatement:
                bool seenTry = afterTry;
                foreach (var sub in blockStatement.Body)
                {
                    if (seenTry)
                        CollectStatementLocalReads(sub, ids);
                    CollectLocalReadsAfterTry(sub, ids, seenTry);
                    if (ContainsTryStatement(sub))
                        seenTry = true;
                }
                return;
            case IfStatement ifStatement:
                CollectLocalReadsAfterTry(ifStatement.True, ids, afterTry);
                if (ifStatement.False != null)
                    CollectLocalReadsAfterTry(ifStatement.False, ids, afterTry);
                return;
            case WhileStatement whileStatement:
                CollectLocalReadsAfterTry(whileStatement.Body, ids, afterTry);
                return;
            case IsStatement isStatement:
                CollectLocalReadsAfterTry(isStatement.True, ids, afterTry);
                if (isStatement.False != null)
                    CollectLocalReadsAfterTry(isStatement.False, ids, afterTry);
                return;
            case TryStatement tryStatement:
                CollectLocalReadsAfterTry(tryStatement.Body, ids, afterTry);
                foreach (var (_, callStatement) in tryStatement.Catchers)
                    CollectLocalIds(callStatement.Expression, ids);
                return;
            default:
                return;
        }
    }
    static void EmitLocalDeclaration(Type type, int id, bool isVolatile)
    {
        string cType = TranslateType(type);
        if (!isVolatile)
        {
            CL($"{(type is ClassType ? "class" : "value")}_local({cType}, {id});");
            return;
        }
        if (type is ClassType)
        {
            CL($"{cType} volatile l_{id} = NULL;");
            CL($"runtime_reference_local(state, (Instance **)&l_{id}, l_r_{id});");
            CL($"(void)l_{id};");
        }
        else
        {
            CL($"{cType} volatile l_{id} = 0;");
            CL($"(void)l_{id};");
        }
    }

    public static (string Header, string Source) TranspileModule(
        List<Class> transpileClasses,
        List<InterfaceDef> allInterfaces,
        List<Class> allClasses,
        List<string> importedNamespaces,
        List<ClassType> usingTypes,
        string AllHeaderFileName)
    {
        TranspileState? previousState = currentState;
        currentState = new TranspileState();
        try
        {
            ImportedNamespaces = importedNamespaces;
            UsingTypes = usingTypes;
            classes = allClasses;
            interfaces = allInterfaces;
            h.Clear();
            c.Clear();
            m2.Clear();

            CL($"#include \"{AllHeaderFileName}\"");
            foreach (var cls in transpileClasses)
            {
                BothL();
                CL();
                Current = cls;
                Namespace = cls.Namespace;
                Name = cls.Name;
                FullName = $"{Namespace}_{Name}";
                FullClassName = $"{Namespace} {Name}";
                CurrentType = new ClassType(Namespace, Name);

            Both(BuildSignatureNoArgs($"{FullName}*", $"new_{FullName}"));
            CL();
            HL(";");
            CL("{");
            CL($"    {FullName}* instance = ({FullName}*)malloc(sizeof({FullName}));");
            int fieldIndex = 0;
            foreach (var f in GetAllInstanceFields(cls))
            {
                if (f.Type is ValueType)
                    CL($"    instance->f_{fieldIndex++} = 0;");
                else
                    CL($"    instance->f_{fieldIndex++} = NULL;");
            }
            CL("    return instance;");
            CL("}");
            CL();
            Both(BuildRealSignature($"void", $"free_{FullName}", $"{FullName}* instance"));
            HL(";");
            CL();
            CL("{");
            CL("    free(instance);");
            CL("}");

            CL();
            HL($"extern static_{FullName} static_{FullName}_data;");
            CL($"static_{FullName} static_{FullName}_data;");

            int methodCount = cls.Methods.Count;
            if (methodCount > 0)
            {
                ML2();
                HL($"extern Method {FullName}_methods[];");
                ML2($"Method {FullName}_methods[] = {{");
                foreach (var method in cls.Methods)
                {
                    ReturnType = method.ReturnType;
                    volatileLocals = new HashSet<int>();
                    CollectLocalReadsAfterTry(method.Body, volatileLocals);
                    CL();
                    string Return = method.ReturnType != null ? TranslateType(method.ReturnType) : "void";
                    string Name = $"{FullName}_{method.Name}";
                    ML2($"    {{ \"{method.Name}\", (void*){Name} }},");
                    string Signature = BuildSignature(Return, Name, method.Arguments);
                    arguments.Clear();
                    int i = 0;
                    foreach (var arg in method.Arguments)
                        arguments.Add(i++, arg);
                    CL(Signature);
                    indent++;
                    CL("{");
                    CL();
                    i = 0;
                    foreach (var arg in method.Arguments)
                    {
                        if (arg is ClassType classType && !classType.Nullable)
                        {
                            CL($"if (!p_{i})");
                            CL($"{{");
                            Class @class = GetClass(classType);
                            CL($"   printf(\"{@class.Namespace} {@class.Name} argument to {method.Name} is nil\\n\");");
                            CL($"   abort();");
                            CL($"}}");
                        }
                        i++;
                    }
                    if (method.ReturnType != null)
                        CL($"{(method.ReturnType is ClassType ? "class" : "value")}_ret({Return});");
                    CL("method_start;");
                    for (int argIndex = 0; argIndex < method.Arguments.Count; argIndex++)
                    {
                        var arg = method.Arguments[argIndex];
                        if (arg is ClassType)
                            CL($"class_arg({argIndex});");
                        else
                            CL($"use(p_{argIndex});");
                    }
                    bool isBox = method.Name == "Box";
                    bool isUnbox = method.Name == "Unbox";
                    if (isBox)
                    {
                        CL($"l_retval = (STD_Any*)runtime_new(state, \"STD\", \"Any\");");
                        CL($"((STD_Any*)l_retval)->f_0 = (Instance*)p_0;");
                        CL("do_ret_void;");
                    }
                    else if (isUnbox)
                    {
                        CL("if (!p_0)");
                        CL("{");
                        CL("    do_ret_value(NULL);");
                        CL("}");
                        CL("STD_Any *l_any = (STD_Any*)p_0;");
                        CL("if (!l_any->f_0)");
                        CL("    l_retval = NULL;");
                        CL($"else if (((Instance*)l_any->f_0)->definition == get_{FullName}())");
                        CL($"    l_retval = ({FullName}*)l_any->f_0;");
                        CL("else");
                        CL("    l_retval = NULL;");
                        CL("do_ret_void;");
                    }
                    else
                    {
                        TranslateStatement(method.Body);
                    }
                    CL($"method_end;");
                    if (method.ReturnType is ClassType)
                        CL("method_end_class_ret;");
                    indent--;
                    if (method.ReturnType != null)
                        CL("ret_value;");
                    else
                        CL("ret_void;");
                    CL();
                    CL("}");
                }
                ML2("};");
            }
            }

            return (h.ToString(), c.ToString() + m2.ToString());
        }
        finally
        {
            currentState = previousState;
        }
    }
    public static string TranspileTypes(
        List<Class> allClasses,
        List<InterfaceDef> allInterfaces,
        Dictionary<string, List<string>> importedNamespacesByClass)
    {
        TranspileState? previousState = currentState;
        currentState = new TranspileState();
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("#pragma once");
            sb.AppendLine("#include \"runtime.h\"");

            classes = allClasses;
            interfaces = allInterfaces;
            var allNamespaces = allClasses
                .Select(c => c.Namespace)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        foreach (var cls in allClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            sb.AppendLine($"typedef struct {fullName} {fullName};");
            sb.AppendLine($"typedef struct static_{fullName} static_{fullName};");
        }

        foreach (var cls in allClasses)
        {
            Namespace = cls.Namespace;
            Name = cls.Name;
            CurrentType = new ClassType(Namespace, Name);
            ImportedNamespaces = importedNamespacesByClass.TryGetValue($"{cls.Namespace} {cls.Name}", out var imports)
                ? imports
                : allNamespaces;

            string fullName = $"{cls.Namespace}_{cls.Name}";
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"// {cls.Namespace} { cls.Name}");

            sb.AppendLine($"");
            sb.AppendLine($"typedef struct {fullName} {{");
            sb.AppendLine("    Definition *definition;");
            sb.AppendLine("    bool seen;");
            int i = 0;
            foreach (var f in GetAllInstanceFields(cls))
                sb.AppendLine($"    {TranslateType(f.Type)} f_{i++}; // {f.Name}");
            sb.AppendLine($"}} {fullName};");

            sb.AppendLine($"");
            i = 0;
            sb.AppendLine($"typedef struct static_{fullName} {{");
            if (cls.StaticFields.Count == 0)
                sb.AppendLine("    uint8_t _pad;");
            else
                foreach (var f in cls.StaticFields)
                    sb.AppendLine($"    {TranslateType(f.Type)} f_{i++}; // {f.Name}");
            sb.AppendLine($"}} static_{fullName};");
        }

            return sb.ToString();
        }
        finally
        {
            currentState = previousState;
        }
    }

    public static string TranspileDefinitions(List<Class> compiledClasses, List<Class> allClasses, string AllHeaderFileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#define FUNCTION_VAR");
        sb.AppendLine($"#include \"{AllHeaderFileName}\"");
        sb.AppendLine("RuntimeState *state = NULL;");
        sb.AppendLine();
        foreach (var cls in allClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            sb.AppendLine();
            sb.AppendLine($"Definition *def_{fullName} = NULL;");
            sb.AppendLine($"Definition *get_{fullName}(void)");
            sb.AppendLine("{");
            sb.AppendLine($"    return ensure_definition(&def_{fullName}, \"{cls.Namespace}\", \"{cls.Name}\");");
            sb.AppendLine("}");
        }
        foreach (var cls in allClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            bool hasInstanceRefs = GetAllInstanceFields(cls).Any(f => f.Type is ClassType);
            bool hasStaticRefs = cls.StaticFields.Any(f => f.Type is ClassType);
            if (hasInstanceRefs || hasStaticRefs)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine($"// {cls.Namespace} {cls.Name}");
            }
            if (hasInstanceRefs)
            {
                sb.AppendLine($"");
                sb.AppendLine($"static void show_refs_{fullName}(Instance *instance)");
                sb.AppendLine("{");
                sb.AppendLine($"    {fullName} *obj = ({fullName}*)instance;");
                int i = 0;
                foreach (var f in GetAllInstanceFields(cls))
                {
                    if (f.Type is ClassType)
                        sb.AppendLine($"    if (obj->f_{i}) runtime_show_instance(state, (Instance*)obj->f_{i});");
                    i++;
                }
                sb.AppendLine("}");
            }
            if (hasStaticRefs)
            {
                sb.AppendLine($"");
                sb.AppendLine($"static void show_static_refs_{fullName}(void)");
                sb.AppendLine("{");
                sb.AppendLine($"    static_{fullName} *s = &static_{fullName}_data;");
                int i = 0;
                foreach (var f in cls.StaticFields)
                {
                    if (f.Type is ClassType)
                        sb.AppendLine($"    if (s->f_{i}) runtime_show_instance(state, (Instance*)s->f_{i});");
                    i++;
                }
                sb.AppendLine("}");
            }
        }
        sb.AppendLine($"");
        sb.AppendLine($"");
        sb.AppendLine("static Definition definitions[] = {");
        foreach (var cls in compiledClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            sb.AppendLine("    {");
            sb.AppendLine($"        .namespace_ = \"{cls.Namespace}\",");
            sb.AppendLine($"        .name = \"{cls.Name}\",");
            sb.AppendLine($"        .instance_size = sizeof({fullName}),");
            sb.AppendLine($"        .new = (InitFunc)new_{fullName},");
            sb.AppendLine($"        .free = (FreeFunc)free_{fullName},");
            sb.AppendLine(GetAllInstanceFields(cls).Any(f => f.Type is ClassType)
                ? $"        .show_refs = show_refs_{fullName},"
                : "        .show_refs = NULL,");
            sb.AppendLine($"        .static_data = (Instance**)&static_{fullName}_data,");
            sb.AppendLine(cls.StaticFields.Any(f => f.Type is ClassType)
                ? $"        .show_static_refs = show_static_refs_{fullName},"
                : "        .show_static_refs = NULL,");
            if (cls.Methods.Count > 0)
            {
                sb.AppendLine($"        .methods = {fullName}_methods,");
                sb.AppendLine($"        .method_count = {cls.Methods.Count}");
            }
            else
            {
                sb.AppendLine("        .methods = NULL,");
                sb.AppendLine("        .method_count = 0");
            }
            sb.AppendLine("    },");
        }
        sb.AppendLine("};");
        sb.AppendLine($"");
        sb.AppendLine($"");
        sb.AppendLine("EXPORT void getDefinitions(APITable *table) {");
        sb.AppendLine($"    table->count = {compiledClasses.Count};");
        sb.AppendLine("    table->defs = definitions;");
        sb.AppendLine("    state = table->state;");
        sb.AppendLine("    runtime_init = table->runtime_init;");
        sb.AppendLine("    runtime_load_package = table->runtime_load_package;");
        sb.AppendLine("    runtime_new = table->runtime_new;");
        sb.AppendLine("    runtime_free = table->runtime_free;");
        sb.AppendLine("    runtime_new_reference_local = table->runtime_new_reference_local;");
        sb.AppendLine("    runtime_gc = table->runtime_gc;");
        sb.AppendLine("    runtime_gc_force = table->runtime_gc_force;");
        sb.AppendLine("    runtime_add_alloc = table->runtime_add_alloc;");
        sb.AppendLine("    runtime_sub_alloc = table->runtime_sub_alloc;");
        sb.AppendLine("    runtime_show_instance = table->runtime_show_instance;");
        sb.AppendLine("    runtime_null_coalesce = table->runtime_null_coalesce;");
        sb.AppendLine("    runtime_unwrap = table->runtime_unwrap;");
        sb.AppendLine("    runtime_throw = table->runtime_throw;");
        sb.AppendLine("    runtime_exception = table->runtime_exception;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static bool TypeMatches(Type type, Type other, bool ignoreNullable = false)
    {
        if (type is ValueType valueType)
            return other is ValueType otherValueType && (valueType.Name == otherValueType.Name || otherValueType.Name == "unknown_number");
        else if (type is ClassType classType)
        {
            if (classType.Name == "Nullable" && classType.Namespace == "__")
                return false;
            if (other is ClassType otherClassType)
            {
                if (@otherClassType.Name == "Nullable" && @otherClassType.Namespace == "__")
                    return classType.Nullable;
                bool assignable;
                if (TryGetInterface(classType, out var expectedInterface) && expectedInterface != null)
                {
                    if (TryGetInterface(otherClassType, out var actualInterface) && actualInterface != null)
                        assignable = InterfaceKey(actualInterface) == InterfaceKey(expectedInterface);
                    else
                    {
                        Class @otherClass = GetClass(otherClassType);
                        assignable = ClassImplementsInterface(@otherClass, expectedInterface);
                    }
                }
                else
                {
                    if (TryGetInterface(otherClassType, out _))
                        assignable = false;
                    else
                    {
                        Class @class = GetClass(classType);
                        Class @otherClass = GetClass(otherClassType);
                        assignable = ClassMatches(@otherClass, @class);
                    }
                }
                bool nullableMatches = ignoreNullable || classType.Nullable || !otherClassType.Nullable;
                return assignable && nullableMatches;
            }
        }
        return false;
    }

    static bool GetMethod(ref Class? @class, string name, IEnumerable<Type> arguments, out Method? method, bool searchHierarchy = false)
    {
        List<Type> argumentList = arguments.ToList();
        method = null;
        Func<Method, bool> selector = m =>
            {
                if (m.Name != name || m.Arguments.Count != argumentList.Count)
                    return false;
                for (int i = 0; i < m.Arguments.Count; i++)
                    if (!TypeMatches(m.Arguments[i], argumentList[i]))
                        return false;
                return true;
            };
        if (@class == null)
        {
            List<(Method method, Class @class)> candidates = [.. Current.Methods.Where(selector).Select(m => (m, Current))];
            if (candidates.Count > 1) return false;
            else if (candidates.Count == 1)
            {
                method = candidates[0].method;
                @class = candidates[0].@class;
                return true;
            }
            else
            {
                foreach (var usingType in UsingTypes)
                {
                    Class @usingClass = GetClass(usingType);
                    candidates.AddRange(@usingClass.Methods.Where(selector).Select(m => (m, @usingClass)));
                }

                if (candidates.Count == 1)
                {
                    method = candidates[0].method;
                    @class = candidates[0].@class;
                    return true;
                }
                else
                    return false;
            }
        }
        if (!searchHierarchy)
        {
            Method[] methods = @class.Methods.Where(selector).ToArray();
            if (methods.Length == 1)
            {
                method = methods[0];
                return true;
            }
            return false;
        }

        Class topClass = GetTopBaseClass(@class);
        Method[] topMethods = topClass.Methods.Where(selector).ToArray();
        if (topMethods.Length == 1)
        {
            method = topMethods[0];
            @class = topClass;
            return true;
        }
        if (topMethods.Length > 1)
            return false;
        List<(Method method, Class owner)> found = new();
        foreach (var candidateClass in classes)
        {
            if (ClassKey(candidateClass) == ClassKey(topClass))
                continue;
            if (!IsSubclassOf(candidateClass, topClass))
                continue;
            found.AddRange(candidateClass.Methods.Where(selector).Select(m => (m, candidateClass)));
        }
        if (found.Count == 1)
        {
            method = found[0].method;
            @class = found[0].owner;
            return true;
        }
        return false;
    }
    static (Class @class, Field field, int fieldId) ResolveUnqualifiedStaticField(string name, int line)
    {
        Class? @class = null;
        bool found = false;
        if (Current.StaticFields.Any(f => f.Name == name))
        {
            @class = Current;
            found = true;
        }
        foreach (var usingType in UsingTypes)
        {
            Class @usingClass = GetClass(usingType);
            if (@usingClass.StaticFields.Any(f => f.Name == name))
            {
                if (found)
                    throw new Exception($"Field ambiguous on line {line}");
                @class = @usingClass;
                found = true;
            }
        }
        if (!found || @class == null)
            throw new Exception($"Field not found on line {line}");
        int fieldId = @class.StaticFields.FindIndex(f => f.Name == name);
        if (fieldId == -1)
            throw new Exception($"Field not found on line {line}");
        return (@class, @class.StaticFields[fieldId], fieldId);
    }
    static List<Class> GetRuntimeMatchClasses(ClassType targetType)
    {
        if (TryGetInterface(targetType, out var iface) && iface != null)
            return classes.Where(c => ClassImplementsInterface(c, iface)).ToList();
        Class targetClass = GetClass(targetType);
        return classes.Where(c => ClassMatches(c, targetClass)).ToList();
    }
    static string BuildRuntimeTypeCheckExpr(string instanceExpr, ClassType targetType)
    {
        List<Class> matches = GetRuntimeMatchClasses(targetType);
        if (matches.Count == 0)
            return "0";
        var checks = matches
            .Select(c => $"{instanceExpr}->definition == get_{c.Namespace}_{c.Name}()")
            .ToArray();
        return $"({instanceExpr} != NULL && ({string.Join(" || ", checks)}))";
    }
    static int isTempId = 0;
    static void TranslateStatement(Statement statement)
    {
        switch (statement)
        {
            case EmptyStatement emptyStatement:
                break;
            case CallStatement call:
                {
                    C("use(");
                    TranslateExpression(call.Expression, false);
                    CL(");");
                    break;
                }
            case TryStatement tryStatement:
                {
                    C("try ");
                    TranslateStatement(tryStatement.Body);
                    indent++;
                    CL($"catch {{");
                    foreach (var (type, callStatement) in tryStatement.Catchers)
                    {
                        Class @class = GetClass(type);
                        C($"if (strcmp(\"{@class.Namespace}\", exception->definition->namespace_) == 0 && strcmp(\"{type.Name}\", exception->definition->name) == 0) ");
                        TranslateStatement(callStatement);
                        C("else ");
                    }
                    indent--;
                    CL($"runtime_throw(state, exception);");
                    CL("} endcatch");
                    break;
                }
            case ThrowStatement throwStatement:
                {
                    C("runtime_throw(state, ");
                    TranslateExpression(throwStatement.Expression);
                    CL(");");
                    break;
                }
            case GcStatement gcStatement:
                {
                    CL("gc_force;");
                    break;
                }
            case ReturnStatement returnStatement:
                {
                    if (returnStatement.Expression != null)
                    {
                        if (ReturnType == null)
                            throw new Exception($"Return type mismatch on line {returnStatement.Line}");
                        Type returnType = GetType(returnStatement.Expression);
                        if (!TypeMatches(ReturnType, returnType))
                            throw new Exception($"Return type mismatch on line {returnStatement.Line}");
                        C("do_ret_value(");
                        C($"({TranslateType(ReturnType)})");
                        TranslateExpression(returnStatement.Expression);
                        CL(");");
                        break;
                    }
                    else if (ReturnType != null)
                        throw new Exception($"Return type mismatch on line {returnStatement.Line}");
                    CL("do_ret_void;");
                    break;
                }
            case AssignmentStatement assignmentStatement:
                {
                    var (@class, field, fieldId) = ResolveUnqualifiedStaticField(assignmentStatement.Name, assignmentStatement.Line);
                    Type type = GetType(assignmentStatement.Expression);
                    if (!TypeMatches(field.Type, type))
                        throw new Exception($"Static field type assignment mismatch on line {assignmentStatement.Line}");
                    C($"static_data({@class.Namespace}_{@class.Name})->f_{fieldId}");
                    C($" = ");
                    C($"({TranslateType(field.Type)})");
                    TranslateExpression(assignmentStatement.Expression);
                    CL(";");
                    break;
                }
            case LocalAssignmentStatement localAssignmentStatement:
                {
                    Type type = GetType(localAssignmentStatement.Expression);
                    if (!locals.TryGet(localAssignmentStatement.ID, out var local))
                        throw new Exception($"Local not found on line {localAssignmentStatement.Line}");
                    if (!TypeMatches(local!, type))
                        throw new Exception($"Local type assignment mismatch on line {localAssignmentStatement.Line}");
                    C($"set_local({localAssignmentStatement.ID}, ");
                    C($"({TranslateType(local!)})");
                    TranslateExpression(localAssignmentStatement.Expression);
                    CL(");");
                    break;
                }
            case StaticFieldAssignmentStatement staticFieldAssignmentStatement:
                {
                    Type type = GetType(staticFieldAssignmentStatement.Expression);
                    Type field = GetType(staticFieldAssignmentStatement.StaticField);
                    if (!TypeMatches(field, type))
                        throw new Exception($"Static field type assignment mismatch on line {staticFieldAssignmentStatement.Line}");
                    TranslateExpression(staticFieldAssignmentStatement.StaticField);
                    C($" = ");
                    C($"({TranslateType(field)})");
                    TranslateExpression(staticFieldAssignmentStatement.Expression);
                    CL(";");
                    break;
                }
            case InstanceFieldAssignmentStatement instanceFieldAssignmentStatement:
                {
                    Type type = GetType(instanceFieldAssignmentStatement.Expression);
                    Type field = GetType(instanceFieldAssignmentStatement.InstanceField);
                    if (!TypeMatches(field, type))
                        throw new Exception($"Instance field type assignment mismatch on line {instanceFieldAssignmentStatement.Line}");
                    TranslateExpression(instanceFieldAssignmentStatement.InstanceField);
                    C($" = ");
                    C($"({TranslateType(field)})");
                    TranslateExpression(instanceFieldAssignmentStatement.Expression);
                    CL(";");
                    break;
                }
            case WhileStatement whileStatement:
                {
                    C("while (");
                    TranslateExpression(whileStatement.Condition, false);
                    C(") ");
                    TranslateStatement(whileStatement.Body);
                    break;
                }
            case IfStatement ifStatement:
                {
                    C("if (");
                    TranslateExpression(ifStatement.Condition, false);
                    C(") ");
                    TranslateStatement(ifStatement.True);
                    if (ifStatement.False != null)
                    {
                        C("else ");
                        TranslateStatement(ifStatement.False);
                    }
                    break;
                }
            case IsStatement isStatement:
                {
                    Type sourceType = GetType(isStatement.Source);
                    if (sourceType is not ClassType)
                        throw new Exception($"is source must be class/interface type on line {isStatement.Line}");
                    string tmpName = $"l_is_tmp_{isTempId++}";
                    string targetCType = TranslateType(isStatement.TargetType);
                    CL("{");
                    indent++;
                    C($"Instance* {tmpName} = (Instance*)");
                    TranslateExpression(isStatement.Source, false);
                    CL(";");
                    CL($"if ({BuildRuntimeTypeCheckExpr(tmpName, isStatement.TargetType)})");
                    CL("{");
                    indent++;
                    CL($"class_local({targetCType}, {isStatement.BindID});");
                    CL($"set_local({isStatement.BindID}, ({targetCType}){tmpName});");
                    locals.Push(new Dictionary<int, Type> { { isStatement.BindID, isStatement.TargetType } });
                    TranslateStatement(isStatement.True);
                    locals.Pop();
                    indent--;
                    CL("}");
                    if (isStatement.False != null)
                    {
                        CL("else");
                        TranslateStatement(isStatement.False);
                    }
                    indent--;
                    CL("}");
                    break;
                }
            case BlockStatement blockStatement:
                {
                    indent++;
                    CL("{");
                    CL();
                    if (blockStatement.Locals.Count > 0)
                    {
                        CL($"block_enter({Blocks++});");
                        foreach (var local in blockStatement.Locals)
                            EmitLocalDeclaration(local.Value, local.Key, volatileLocals.Contains(local.Key));
                        CL();
                    }
                    locals.Push(blockStatement.Locals);
                    foreach (var subStatement in blockStatement.Body)
                        TranslateStatement(subStatement);
                    locals.Pop();
                    if (blockStatement.Locals.Count > 0)
                    {
                        CL($"block_exit({--Blocks});");
                    }
                    indent--;
                    CL();
                    CL("}");
                    break;
                }
            default:
                throw new Exception($"Invalid statement on line {statement.Line}");
        }
    }
}
