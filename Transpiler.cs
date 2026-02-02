using System.Security.Cryptography;
using System.Text;

public static class Transpiler
{
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
            Class @class = GetClass(classType);
            return $"{@class.Namespace}_{@class.Name}*";
        }
        else
            throw new Exception($"Invalid type on line {type.Line}");
    }

    static readonly StringBuilder h = new();
    static readonly StringBuilder c = new();
    static readonly StringBuilder m2 = new();
    static int indent = 0;
    static void H(string text = "") => h.Append(text);
    static void C(string text = "") => c.Append(text);
    static void M2(string text = "") => m2.Append(text);

    static void HL(string line = "") { h.AppendLine(line); c.Append(new string(' ', indent * 4)); }
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
    static string BuildFunctionPointerCast(string returnType, IReadOnlyList<string> argTypes)
    {
        string args = argTypes.Count == 0 ? "void" : string.Join(", ", argTypes);
        return $"({returnType} (*)({args}))";
    }

    static DictionaryStack<int, Type> locals = new();
    static Dictionary<int, Type> arguments = new();
    static string FullName = "";
    static string FullClassName = "";
    static Type CurrentType = new ValueType("");
    static Type? ReturnType = null;
    static string Namespace = "";
    static string Name = "";
    static List<string> ImportedNamespaces = new();
    public static (string Header, string Source) TranspileModule(
        List<Class> transpileClasses,
        List<Class> allClasses,
        List<string> importedNamespaces,
        string AllHeaderFileName,
        string TypesHeaderFileName)
    {
        ImportedNamespaces = importedNamespaces;
        classes = allClasses;
        h.Clear();
        c.Clear();
        m2.Clear();

        HL("#pragma once");
        HL($"#include \"{TypesHeaderFileName}\"");

        CL($"#include \"{AllHeaderFileName}\"");
        foreach (var cls in transpileClasses)
        {
            Namespace = cls.Namespace;
            Name = cls.Name;
            FullName = $"{Namespace}_{Name}";
            FullClassName = $"{Namespace}::{Name}";
            CurrentType = new ClassType(Namespace, Name);

            if (cls.InstanceFields.Count > 0)
            {
                BothL();
                BothL("// Demo::Vector2");
                Both(BuildSignatureNoArgs($"{FullName}*", $"new_{FullName}"));
                HL(";");
                CL("{");
                CL();
                CL($"    {FullName}* instance = ({FullName}*)malloc(sizeof({FullName}));");
                int i = 0;
                foreach (var f in cls.InstanceFields)
                {
                    if (f.Type is ValueType)
                        CL($"    instance->f_{i++} = 0;");
                    else
                        CL($"    instance->f_{i++} = NULL;");
                }
                CL("    return instance;");
                CL();
                CL("}");
                BothL();

                BothL("// Demo::Vector2");
                Both(BuildRealSignature($"void", $"free_{FullName}", $"{FullName}* instance"));
                HL(";");
                CL("{");
                CL();
                CL("    free(instance);");
                CL();
                CL("}");
            }

            if (cls.StaticFields.Count > 0)
            {
                BothL();
                BothL("// Demo::Vector2");
                HL($"extern static_{FullName} static_{FullName}_data;");

                CL($"static_{FullName} static_{FullName}_data;");
            }

            int methodCount = cls.Methods.Count;
            if (methodCount > 0)
            {
                HL();
                ML2();
                ML2($"// {FullClassName}");
                HL($"extern Method {FullName}_methods[];");
                ML2($"Method {FullName}_methods[] = {{");
                foreach (var method in cls.Methods)
                {
                    ReturnType = method.ReturnType;
                    BothL();
                    CL($"// {FullClassName}.{method.Name}");
                    ML2($"    // {FullClassName}.{method.Name}");
                    string Return = method.ReturnType != null ? TranslateType(method.ReturnType) : "void";
                    string Name = $"{FullName}_{method.Name}";
                    ML2($"    {{ \"{method.Name}\", (void*){Name} }},");
                    string Signature = BuildSignature(Return, Name, method.Arguments);
                    arguments.Clear();
                    int i = 0;
                    foreach (var arg in method.Arguments)
                        arguments.Add(i++, arg);
                    Both(Signature);
                    HL(";");
                    CL();
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
                            CL($"   printf(\"{@class.Namespace}::{@class.Name} argument to {method.Name} is nil\\n\");");
                            CL($"   abort();");
                            CL($"}}");
                        }
                        i++;
                    }
                    if (method.ReturnType != null)
                        CL($"{Return} l_retval;");
                    if (method.ReturnType is ClassType)
                    {
                        CL($"ReferenceLocal *l_init_preret = state->locals;");
                        CL($"l_retval = NULL;");
                        CL($"runtime_reference_local(state, (Instance**)&l_retval, l_r_retval);");
                    }
                    else if (method.ReturnType is ValueType)
                        CL($"l_retval = 0;");
                    CL("ReferenceLocal *l_init = state->locals;");
                    for (int argIndex = 0; argIndex < method.Arguments.Count; argIndex++)
                    {
                        var arg = method.Arguments[argIndex];
                        if (arg is ClassType)
                            CL($"runtime_reference_local(state, (Instance**)&p_{argIndex}, p_r_{argIndex});");
                    }
                    bool isBox = method.Name == "Box";
                    bool isUnbox = method.Name == "Unbox";
                    if (isBox)
                    {
                        CL($"l_retval = (STD_Any*)runtime_new(state, \"STD\", \"Any\");");
                        CL($"((STD_Any*)l_retval)->f_0 = (Instance*)p_0;");
                        CL("goto _ret;");
                    }
                    else if (isUnbox)
                    {
                        CL("if (!p_0)");
                        CL("{");
                        CL("    l_retval = NULL;");
                        CL("    goto _ret;");
                        CL("}");
                        CL("STD_Any *l_any = (STD_Any*)p_0;");
                        CL("if (!l_any->f_0)");
                        CL("    l_retval = NULL;");
                        CL($"else if (((Instance*)l_any->f_0)->definition == get_{FullName}())");
                        CL($"    l_retval = ({FullName}*)l_any->f_0;");
                        CL("else");
                        CL("    l_retval = NULL;");
                        CL("goto _ret;");
                    }
                    else
                    {
                        TranslateStatement(method.Body);
                    }
                    CL("goto _ret;");
                    CL("_ret:");
                    CL("state->locals = l_init;");
                    CL("runtime_gc(state);");
                    if (method.ReturnType is ClassType)
                        CL($"state->locals = l_init_preret;");
                    indent--;
                    if (method.ReturnType != null)
                        CL("return l_retval;");
                    else
                        CL("return;");
                    CL();
                    CL("}");
                }
                ML2("};");
            }
        }

        return (h.ToString(), c.ToString() + m2.ToString());
    }
    public static string TranspileTypes(
        List<Class> compiledClasses,
        List<Class> allClasses,
        Dictionary<string, List<string>> importedNamespacesByClass,
        Dictionary<string, string> packageHeadersByNamespace,
        string packageName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma once");
        sb.AppendLine("#include \"runtime.h\"");
        if (string.IsNullOrWhiteSpace(packageName))
            throw new Exception("Package name is required for types header generation");

        var compiledNamespaces = new HashSet<string>(compiledClasses.Select(c => c.Namespace), StringComparer.OrdinalIgnoreCase);
        var importedNamespaces = importedNamespacesByClass
            .SelectMany(kvp => kvp.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var ns in importedNamespaces)
        {
            if (compiledNamespaces.Contains(ns))
                continue;
            if (!packageHeadersByNamespace.TryGetValue(ns, out var header))
                throw new Exception($"Missing package header for namespace {ns}");
            sb.AppendLine($"#include \"{header}\"");
        }

        classes = allClasses;

        foreach (var cls in compiledClasses)
        {
            Namespace = cls.Namespace;
            Name = cls.Name;
            CurrentType = new ClassType(Namespace, Name);
            ImportedNamespaces = importedNamespacesByClass.TryGetValue($"{cls.Namespace}::{cls.Name}", out var imports)
                ? imports
                : new List<string>();

            string fullName = $"{cls.Namespace}_{cls.Name}";
            string fullClassName = $"{cls.Namespace}::{cls.Name}";
            if (cls.InstanceFields.Count > 0)
            {
                sb.AppendLine($"// {fullClassName}");
                sb.AppendLine($"typedef struct {fullName} {{");
                sb.AppendLine("    Definition *definition;");
                sb.AppendLine("    bool seen;");
                int i = 0;
                foreach (var f in cls.InstanceFields)
                    sb.AppendLine($"    {TranslateType(f.Type)} f_{i++}; // {fullClassName}.{f.Name}");
                sb.AppendLine($"}} {fullName};");
            }

            if (cls.StaticFields.Count > 0)
            {
                sb.AppendLine($"// {fullClassName}");
                int i = 0;
                sb.AppendLine($"typedef struct static_{fullName} {{");
                foreach (var f in cls.StaticFields)
                    sb.AppendLine($"    {TranslateType(f.Type)} f_{i++}; // {fullClassName}.{f.Name}");
                sb.AppendLine($"}} static_{fullName};");
            }
        }

        return sb.ToString();
    }

    public static string TranspileDefinitions(List<Class> compiledClasses, List<Class> allClasses, string AllHeaderFileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#define FUNCTION_VAR");
        sb.AppendLine($"#include \"{AllHeaderFileName}\"");
        sb.AppendLine("RuntimeState *state = NULL;");
        foreach (var cls in allClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            sb.AppendLine($"Definition *def_{fullName} = NULL;");
        }
        foreach (var cls in allClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            sb.AppendLine($"// {cls.Namespace}::{cls.Name}");
            sb.AppendLine($"Definition *get_{fullName}(void)");
            sb.AppendLine("{");
            sb.AppendLine($"    return ensure_definition(&def_{fullName}, \"{cls.Namespace}\", \"{cls.Name}\");");
            sb.AppendLine("}");
        }
        foreach (var cls in allClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            bool hasInstanceRefs = cls.InstanceFields.Any(f => f.Type is ClassType);
            bool hasStaticRefs = cls.StaticFields.Any(f => f.Type is ClassType);
            if (hasInstanceRefs)
            {
                sb.AppendLine($"// {cls.Namespace}::{cls.Name} instance refs");
                sb.AppendLine($"static void show_refs_{fullName}(Instance *instance)");
                sb.AppendLine("{");
                sb.AppendLine($"    {fullName} *obj = ({fullName}*)instance;");
                int i = 0;
                foreach (var f in cls.InstanceFields)
                {
                    if (f.Type is ClassType)
                        sb.AppendLine($"    if (obj->f_{i}) runtime_show_instance((Instance*)obj->f_{i});");
                    i++;
                }
                sb.AppendLine("}");
            }
            if (hasStaticRefs)
            {
                sb.AppendLine($"// {cls.Namespace}::{cls.Name} static refs");
                sb.AppendLine($"static void show_static_refs_{fullName}(void)");
                sb.AppendLine("{");
                sb.AppendLine($"    static_{fullName} *s = &static_{fullName}_data;");
                int i = 0;
                foreach (var f in cls.StaticFields)
                {
                    if (f.Type is ClassType)
                        sb.AppendLine($"    if (s->f_{i}) runtime_show_instance((Instance*)s->f_{i});");
                    i++;
                }
                sb.AppendLine("}");
            }
        }
        sb.AppendLine("static Definition definitions[] = {");
        foreach (var cls in compiledClasses)
        {
            string fullName = $"{cls.Namespace}_{cls.Name}";
            sb.AppendLine($"    // {cls.Namespace}::{cls.Name}");
            sb.AppendLine("    {");
            sb.AppendLine($"        .namespace_ = \"{cls.Namespace}\",");
            sb.AppendLine($"        .name = \"{cls.Name}\",");
            if (cls.InstanceFields.Count > 0)
            {
                sb.AppendLine($"        .instance_size = sizeof({fullName}),");
                sb.AppendLine($"        .new = (InitFunc)new_{fullName},");
                sb.AppendLine($"        .free = (FreeFunc)free_{fullName},");
                sb.AppendLine(cls.InstanceFields.Any(f => f.Type is ClassType)
                    ? $"        .show_refs = show_refs_{fullName},"
                    : "        .show_refs = NULL,");
            }
            else
            {
                sb.AppendLine("        .instance_size = 0,");
                sb.AppendLine("        .new = NULL,");
                sb.AppendLine("        .free = NULL,");
                sb.AppendLine("        .show_refs = NULL,");
            }
            if (cls.StaticFields.Count > 0)
            {
                sb.AppendLine($"        .static_data = (Instance**)&static_{fullName}_data,");
                sb.AppendLine(cls.StaticFields.Any(f => f.Type is ClassType)
                    ? $"        .show_static_refs = show_static_refs_{fullName},"
                    : "        .show_static_refs = NULL,");
            }
            else
            {
                sb.AppendLine("        .static_data = NULL,");
                sb.AppendLine("        .show_static_refs = NULL,");
            }
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
            Class @class = GetClass(classType);
            if (other is ClassType otherClassType)
            {
                if (@otherClassType.Name == "Nullable" && @otherClassType.Namespace == "__")
                    return classType.Nullable;
                Class @otherClass = GetClass(otherClassType);
                return @class.Name == @otherClass.Name && @class.Namespace == @otherClass.Namespace && (ignoreNullable ? true : (classType.Nullable || !otherClassType.Nullable));
            }
        }
        return false;
    }


    static void TranslateStatement(Statement statement)
    {
        switch (statement)
        {
            case CallStatement call:
                {
                    C("(void)");
                    TranslateExpression(call.Expression);
                    CL(";");
                    break;
                }
            case GcStatement gcStatement:
                {
                    CL("runtime_gc_force(state);");
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
                        C("l_retval = ");
                        TranslateExpression(returnStatement.Expression);
                        CL(";");
                    }
                    else if (ReturnType != null)
                        throw new Exception($"Return type mismatch on line {returnStatement.Line}");
                    CL("goto _ret;");
                    break;
                }
            case LocalAssignmentStatement localAssignmentStatement:
                {
                    Type type = GetType(localAssignmentStatement.Expression);
                    if (!locals.TryGet(localAssignmentStatement.ID, out var local))
                        throw new Exception($"Local not found on line {localAssignmentStatement.Line}");
                    if (!TypeMatches(local!, type))
                        throw new Exception($"Local type assignment mismatch on line {localAssignmentStatement.Line}");
                    C($"l_{localAssignmentStatement.ID} = ");
                    TranslateExpression(localAssignmentStatement.Expression);
                    CL(";");
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
                    TranslateExpression(instanceFieldAssignmentStatement.Expression);
                    CL(";");
                    break;
                }
            case WhileStatement whileStatement:
                {
                    C("while (");
                    TranslateExpression(whileStatement.Condition);
                    C(")");
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
            case BlockStatement blockStatement:
                {
                    indent++;
                    CL("{");
                    CL();
                    if (blockStatement.Locals.Count > 0)
                    {
                        CL("runtime_gc(state);");
                        CL("ReferenceLocal *l_prev = state->locals;");
                        foreach (var local in blockStatement.Locals)
                        {
                            CL($"{TranslateType(local.Value)} l_{local.Key};");
                            if (local.Value is ClassType)
                            {
                                CL($"l_{local.Key} = NULL;");
                                CL($"runtime_reference_local(state, (Instance**)&l_{local.Key}, l_r_{local.Key});");
                            }
                            else if (local.Value is ValueType)
                                CL($"l_{local.Key} = 0;");
                            CL($"(void)l_{local.Key};");
                        }
                        CL();
                    }
                    locals.Push(blockStatement.Locals);
                    foreach (var subStatement in blockStatement.Body)
                        TranslateStatement(subStatement);
                    locals.Pop();
                    if (blockStatement.Locals.Count > 0)
                    {
                        CL("state->locals = l_prev;");
                        CL("runtime_gc(state);");
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
    static List<Class> classes = new();
    static Class GetClass(ClassType classType)
    {
        if (classType.CachedClass != null)
            return classType.CachedClass;
        List<Class> candidates = classes.Where(c => c.Name == classType.Name).Where(c => classType.Namespace != null ? c.Namespace.StartsWith(classType.Namespace) : true).Where(c => ImportedNamespaces.Contains(c.Namespace) || c.Namespace == Namespace).ToList();
        if (candidates.Count > 1)
            throw new Exception($"Multiple class candidates found for {classType.Namespace}::{classType.Name} on line {classType.Line}");
        else if (candidates.Count == 0)
            throw new Exception($"No class found for {classType.Namespace}::{classType.Name} on line {classType.Line}");
        classType.CachedClass = candidates[0];
        return candidates[0];
    }
    static Type GetType(Expression expression)
    {
        if (expression.CachedType != null)
            return expression.CachedType;
        Type? type;
        switch (expression)
        {
            case LocalExpression localExpression:
                return locals.TryGet(localExpression.ID, out type) ? type! : throw new Exception($"Local not found on line {localExpression.Line}");
            case ArgumentExpression argumentExpression:
                return arguments.TryGetValue(argumentExpression.ID, out type) ? type! : throw new Exception($"Argument not found on line {argumentExpression.Line}");
            case IfExpression ifExpression:
                {
                    Type left = GetType(ifExpression.True);
                    Type right = GetType(ifExpression.False);
                    if (left is ClassType leftClass && right is ClassType rightClass)
                        return leftClass with { Nullable = rightClass.Nullable || leftClass.Nullable };
                    else
                        return left;
                }
            case CallExpression callExpression:
                {
                    Class @class = GetClass(callExpression.Callee);
                    Method method = @class.Methods.FirstOrDefault(m => m.Name == callExpression.Name) ?? throw new Exception($"Method not found on line {callExpression.Line}");
                    return method.ReturnType ?? throw new Exception($"Void returning method used in expression on line {callExpression.Line}");
                }
            case CallInstanceExpression callInstanceExpression:
                {
                    type = GetType(callInstanceExpression.Callee);
                    if (type is not ClassType classType)
                        throw new Exception("Cannot call on a non-class type you moron");
                    Class @class = GetClass(classType);
                    Method method = @class.Methods.FirstOrDefault(m => m.Name == callInstanceExpression.Name) ?? throw new Exception($"Method not found on line {callInstanceExpression.Line}");
                    return method.ReturnType ?? throw new Exception($"Void returning method used in expression on line {callInstanceExpression.Line}");
                }
            case ClassExpression classExpression:
                {
                    throw new Exception($"Internal class expression exposed on line {expression.Line}, please report this");
                }
            case StaticFieldExpression staticFieldExpression:
                {
                    Class @class = GetClass(staticFieldExpression.Class);
                    Field field = @class.StaticFields.FirstOrDefault(f => f.Name == staticFieldExpression.Field) ?? throw new Exception($"Static field {staticFieldExpression.Field} not found on line {staticFieldExpression.Line}");
                    return field.Type;
                }
            case MethodIndexExpression methodIndexExpression:
                {
                    throw new Exception("Method index expression not called");
                }
            case InstanceFieldExpression instanceFieldExpression:
                {
                    type = GetType(instanceFieldExpression.Instance);
                    if (type is not ClassType classType)
                        throw new Exception("Cannot access instance field on a non-class type you moron");
                    Class @class = GetClass(classType);
                    Field field = @class.InstanceFields.FirstOrDefault(f => f.Name == instanceFieldExpression.Field) ?? throw new Exception($"Instance field {instanceFieldExpression.Field} not found on line {instanceFieldExpression.Line}");

                    return field.Type;
                }
            case NumberExpression numberExpression:
                {
                    return new ValueType("unknown_number", numberExpression.Line);
                }
            case StringExpression stringExpression:
                {
                    return new ValueType("cstr", stringExpression.Line);
                }
            case BinaryExpression binaryExpression:
                {
                    switch (binaryExpression.Op)
                    {
                        case "==":
                        case "!=":
                        case "&&":
                        case "||":
                        case "<":
                        case ">":
                        case "<=":
                        case ">=":
                            return new ValueType("bool", binaryExpression.Line);
                        case "??":
                            {
                                Type left = GetType(binaryExpression.Left);
                                if (left is not ClassType classType)
                                    throw new Exception($"Cannot use ?? on a non-class type you moron on line {binaryExpression.Line}");
                                return classType with { Nullable = false };
                            }
                        default:
                            {
                                Type left = GetType(binaryExpression.Left);
                                Type right = GetType(binaryExpression.Right);
                                if (left is not ValueType leftValue || right is not ValueType rightValue)
                                    throw new Exception($"Cannot use {binaryExpression.Op} on a non-value type on line {binaryExpression.Line}");
                                if (!TypeMatches(left, right))
                                    throw new Exception($"Type mismatch of binary expression with operator {binaryExpression.Op} on line {binaryExpression.Line}, left is {leftValue.Name} and right is {rightValue.Name}");
                                return left;
                            }
                    }
                }
            case UnaryExpression unaryExpression:
                {
                    return GetType(unaryExpression.Right);
                }
            case PostfixExpression postfixExpression:
                {
                    Type left = GetType(postfixExpression.Left);
                    if (postfixExpression.Op == "@")
                    {
                        if (left is not ClassType classType)
                            throw new Exception($"Cannot use @ on a non-class type on line {postfixExpression.Line}");
                        if (!classType.Nullable)
                            throw new Exception($"Cannot use @ on a non-nullable type on line {postfixExpression.Line}");
                        return classType with { Nullable = false };
                    }
                    throw new Exception($"Invalid postfix operator {postfixExpression.Op} on line {postfixExpression.Line}");
                }
            case NewExpression newExpression:
                {
                    return CurrentType;
                }
            case NilExpression nilExpression:
                {
                    return new ClassType("__", "Nullable", nilExpression.Line) { Nullable = true };
                }
            default:
                throw new Exception($"Invalid expression on line {expression.Line}");
        }
    }
    static void TranslateExpression(Expression expression, bool paren = true)
    {
        switch (expression)
        {
            case LocalExpression localExpression:
                C($"l_{localExpression.ID}");
                break;
            case ArgumentExpression argumentExpression:
                C($"p_{argumentExpression.ID}");
                break;
            case CallExpression callExpression:
                {
                    Class @class = GetClass(callExpression.Callee);
                    Method method = @class.Methods.FirstOrDefault(m => m.Name == callExpression.Name) ?? throw new Exception($"Method not found: {callExpression.Name} on line {callExpression.Line}");
                    List<Type> argTypes = method.Arguments;
                    int i = 0;
                    foreach (var arg in callExpression.Arguments)
                    {
                        Type type = GetType(arg);
                        Type intended = argTypes.ElementAtOrDefault(i++) ?? throw new Exception($"Argument type mismatch on line {arg.Line}");
                        if (!TypeMatches(intended, type))
                            throw new Exception($"Argument type mismatch on line {callExpression.Line}");
                    }
                    int methodIndex = @class.Methods.FindIndex(m => m.Name == callExpression.Name);
                    if (methodIndex == -1)
                        throw new Exception($"Method not found on line {callExpression.Line}");
                    string returnType = method.ReturnType != null ? TranslateType(method.ReturnType) : "void";
                    List<string> translatedArgTypes = method.Arguments.Select(TranslateType).ToList();
                    string defName = $"def_{@class.Namespace}_{@class.Name}";
                    C($"({BuildFunctionPointerCast(returnType, translatedArgTypes)}");
                    C($"ensure_definition(&{defName}, \"{@class.Namespace}\", \"{@class.Name}\")");
                    C($"->methods[{methodIndex}].entry)(");
                    i = 0;
                    foreach (var arg in callExpression.Arguments)
                    {
                        if (i++ > 0)
                            C(", ");
                        TranslateExpression(arg);
                    }
                    C(")");
                    break;
                }
            case IfExpression ifExpression:
                {
                    Type left = GetType(ifExpression.True);
                    Type right = GetType(ifExpression.False);
                    if (!TypeMatches(left, right, true))
                        throw new Exception($"If expression left and right type mismatch on line {ifExpression.Line}");
                    if (paren)
                        C("(");
                    TranslateExpression(ifExpression.Condition);
                    C(" ? ");
                    TranslateExpression(ifExpression.True);
                    C(" : ");
                    TranslateExpression(ifExpression.False);
                    if (paren)
                        C(")");
                    break;
                }
            case CallInstanceExpression callInstanceExpression:
                {
                    Type type = GetType(callInstanceExpression.Callee);
                    if (type is not ClassType classType)
                        throw new Exception($"Cannot call on a non-class type you moron on line {callInstanceExpression.Line}");
                    if (classType.Nullable)
                        throw new Exception($"Cannot call on a nullable type you moron on line {callInstanceExpression.Line}");
                    Class @class = GetClass(classType);
                    Method method = @class.Methods.FirstOrDefault(m => m.Name == callInstanceExpression.Name) ?? throw new Exception($"Method not found on line {callInstanceExpression.Line}");
                    List<Type> argTypes = method.Arguments;
                    int i = 1;
                    foreach (var arg in callInstanceExpression.Arguments)
                    {
                        Type argType = GetType(arg);
                        Type intended = argTypes.ElementAtOrDefault(i++) ?? throw new Exception($"Argument type mismatch on line {arg.Line}");
                        if (!TypeMatches(intended, argType))
                            throw new Exception($"Argument type mismatch on line {callInstanceExpression.Line}");
                    }
                    int methodIndex = @class.Methods.FindIndex(m => m.Name == callInstanceExpression.Name);
                    if (methodIndex == -1)
                        throw new Exception($"Method not found on line {callInstanceExpression.Line}");
                    string returnType = method.ReturnType != null ? TranslateType(method.ReturnType) : "void";
                    List<string> translatedArgTypes = method.Arguments.Select(TranslateType).ToList();
                    string defName = $"def_{@class.Namespace}_{@class.Name}";
                    C($"({BuildFunctionPointerCast(returnType, translatedArgTypes)}");
                    C($"ensure_definition(&{defName}, \"{@class.Namespace}\", \"{@class.Name}\")");
                    C($"->methods[{methodIndex}].entry)(");
                    TranslateExpression(callInstanceExpression.Callee);
                    foreach (var arg in callInstanceExpression.Arguments)
                    {
                        C(", ");
                        TranslateExpression(arg);
                    }
                    C(")");
                    break;
                }
            case ClassExpression classExpression:
                {
                    throw new Exception($"Weird class usage of {classExpression.Class.Namespace}::{classExpression.Class.Name} on line {classExpression.Line}");
                }
            case StaticFieldExpression staticFieldExpression:
                {
                    Class @class = GetClass(staticFieldExpression.Class);
                    int field = @class.StaticFields.FindIndex(f => f.Name == staticFieldExpression.Field);
                    if (field == -1)
                        throw new Exception($"Static field not found on line {staticFieldExpression.Line}");
                    string defName = $"def_{@class.Namespace}_{@class.Name}";
                    C($"((static_{@class.Namespace}_{@class.Name}*)ensure_definition(&{defName}, \"{@class.Namespace}\", \"{@class.Name}\")->static_data)->f_{field}");
                    break;
                }
            case MethodIndexExpression methodIndexExpression:
                {
                    throw new Exception($"Methods on instances must be called on line {methodIndexExpression.Line}");
                }
            case InstanceFieldExpression instanceFieldExpression:
                {
                    Type type = GetType(instanceFieldExpression.Instance);
                    if (type is not ClassType classType)
                        throw new Exception($"Cannot access instance field on a non-class type you moron on line {instanceFieldExpression.Line}");
                    Class @class = GetClass(classType);
                    int field = @class.InstanceFields.FindIndex(f => f.Name == instanceFieldExpression.Field);
                    if (field == -1)
                        throw new Exception($"Instance field not found on line {instanceFieldExpression.Line}");
                    if (paren)
                        C($"(");
                    C($"(({@class.Namespace}_{@class.Name}*)(");
                    TranslateExpression(instanceFieldExpression.Instance);
                    C($"))->f_{field}");
                    if (paren)
                        C($")");
                    break;
                }
            case NumberExpression numberExpression:
                {
                    C(numberExpression.Value);
                    break;
                }
            case StringExpression stringExpression:
                {
                    C("\"");
                    foreach (byte c in stringExpression.Value)
                    {
                        C("\\x");
                        C(c.ToString("X2"));
                    }
                    C("\"");
                    break;
                }
            case BinaryExpression binaryExpression:
                {
                    if (paren)
                        C("(");
                    Type left = GetType(binaryExpression.Left);
                    Type right = GetType(binaryExpression.Right);
                    if (binaryExpression.Op == "??")
                    {
                        if (left is not ClassType leftClassType || right is not ClassType rightClassType)
                            throw new Exception($"Cannot use ?? on a non-class type on line {binaryExpression.Line}");
                        Class @leftClass = GetClass(leftClassType);
                        Class @rightClass = GetClass(rightClassType);
                        if (@leftClass.Namespace != @rightClass.Namespace || @leftClass.Name != @rightClass.Name)
                            throw new Exception($"Types mismatch on line {binaryExpression.Line}");
                        C($"({TranslateType(left)})runtime_null_coalesce((void*)");
                        TranslateExpression(binaryExpression.Left);
                        C($", (void*)");
                        TranslateExpression(binaryExpression.Right);
                        C($")");
                    }
                    else
                    {
                        if (binaryExpression.Op != "==" && binaryExpression.Op != "!=")
                        {
                            if (left is not ValueType || right is not ValueType)
                                throw new Exception($"Cannot use {binaryExpression.Op} on a non-value type on line {binaryExpression.Line}");
                            if (!TypeMatches(left, right))
                                throw new Exception($"Type mismatch on line {binaryExpression.Line}");
                        }
                        TranslateExpression(binaryExpression.Left);
                        C(" ");
                        C(binaryExpression.Op);
                        C(" ");
                        TranslateExpression(binaryExpression.Right);
                    }
                    if (paren)
                        C(")");
                    break;
                }
            case UnaryExpression unaryExpression:
                {
                    if (paren)
                        C("(");
                    C(unaryExpression.Op);
                    TranslateExpression(unaryExpression.Right);
                    if (paren)
                        C(")");
                    break;
                }
            case PostfixExpression postfixExpression:
                {
                    if (postfixExpression.Op != "@")
                        throw new Exception($"Invalid postfix operator {postfixExpression.Op} on line {postfixExpression.Line}");
                    if (paren)
                        C("(");
                    C($"({TranslateType(GetType(postfixExpression.Left))})runtime_unwrap((void*)");
                    TranslateExpression(postfixExpression.Left);
                    C($", {postfixExpression.Line})");
                    if (paren)
                        C(")");
                    break;
                }
            case NewExpression newExpression:
                {
                    C($"({TranslateType(CurrentType)})runtime_new(state, \"{Namespace}\", \"{Name}\")");
                    break;
                }
            case NilExpression nilExpression:
                {
                    C("NULL");
                    break;
                }
        }
    }

}
