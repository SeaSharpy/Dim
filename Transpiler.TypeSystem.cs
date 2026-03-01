using System.Security.Cryptography;
using System.Text;

public static partial class Transpiler
{
    static string ClassKey(Class @class) => $"{@class.Namespace} {@class.Name}";
    static string InterfaceKey(InterfaceDef iface) => $"{iface.Namespace} {iface.Name}";
    static bool TryGetInterface(ClassType classType, out InterfaceDef? iface)
    {
        iface = interfaces
            .Where(i => i.Name == classType.Name)
            .Where(i => classType.Namespace != null ? i.Namespace.StartsWith(classType.Namespace) : true)
            .Where(i => ImportedNamespaces.Contains(i.Namespace) || i.Namespace == Namespace)
            .FirstOrDefault();
        if (iface == null)
        {
            iface = interfaces
                .Where(i => i.Name == classType.Name)
                .Where(i => classType.Namespace != null ? i.Namespace.StartsWith(classType.Namespace) : true)
                .FirstOrDefault();
        }
        return iface != null;
    }
    static InterfaceDef GetInterface(ClassType classType)
    {
        if (!TryGetInterface(classType, out var iface) || iface == null)
            throw new Exception($"No interface found for {classType.Namespace} {classType.Name} on line {classType.Line}");
        classType.Namespace = iface.Namespace;
        return iface;
    }
    static IEnumerable<ClassType> GetDeclaredInterfaces(Class @class)
    {
        if (@class.Base != null && TryGetInterface(@class.Base, out var _))
            yield return @class.Base;
        foreach (var iface in @class.Interfaces ?? [])
            yield return iface;
    }
    static bool ClassMatches(Class actual, Class expected)
    {
        if (ClassKey(actual) == ClassKey(expected))
            return true;
        return IsSubclassOf(actual, expected);
    }
    static bool IsSubclassOf(Class @class, Class expectedBase)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Class? current = GetBaseClass(@class);
        while (current != null)
        {
            string key = ClassKey(current);
            if (!seen.Add(key))
                throw new Exception($"Inheritance cycle detected at {current.Namespace} {current.Name}");
            if (ClassKey(current) == ClassKey(expectedBase))
                return true;
            current = GetBaseClass(current);
        }
        return false;
    }
    static Class? GetBaseClass(Class @class)
    {
        if (@class.Base == null)
            return null;
        if (TryGetInterface(@class.Base, out _))
            return null;
        return GetClass(@class.Base);
    }
    static bool ClassImplementsInterface(Class @class, InterfaceDef iface)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Class? current = @class;
        while (current != null)
        {
            string key = ClassKey(current);
            if (!seen.Add(key))
                throw new Exception($"Inheritance cycle detected at {current.Namespace} {current.Name}");
            foreach (var ifaceType in GetDeclaredInterfaces(current))
            {
                InterfaceDef resolved = GetInterface(ifaceType);
                if (InterfaceKey(resolved) == InterfaceKey(iface))
                    return true;
            }
            current = GetBaseClass(current);
        }
        return false;
    }
    static Class GetTopBaseClass(Class @class)
    {
        Class current = @class;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            string key = ClassKey(current);
            if (!seen.Add(key))
                throw new Exception($"Inheritance cycle detected at {current.Namespace} {current.Name}");
            Class? baseClass = GetBaseClass(current);
            if (baseClass == null)
                return current;
            current = baseClass;
        }
    }
    static List<Field> GetAllInstanceFields(Class @class)
    {
        List<Field> fields = new();
        HashSet<string> names = new(StringComparer.Ordinal);
        void AppendFrom(Class c)
        {
            Class? baseClass = GetBaseClass(c);
            if (baseClass != null)
                AppendFrom(baseClass);
            foreach (var field in c.InstanceFields)
            {
                if (!names.Add(field.Name))
                    throw new Exception($"Duplicate inherited instance field {field.Name} on class {c.Namespace} {c.Name}");
                fields.Add(field);
            }
        }
        AppendFrom(@class);
        return fields;
    }
    static Class GetClass(ClassType classType)
    {
        if (classType.CachedClass != null)
            return classType.CachedClass;
        List<Class> candidates = classes
            .Where(c => c.Name == classType.Name)
            .Where(c => classType.Namespace != null ? c.Namespace.StartsWith(classType.Namespace) : true)
            .Where(c => ImportedNamespaces.Contains(c.Namespace) || c.Namespace == Namespace)
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = classes
                .Where(c => c.Name == classType.Name)
                .Where(c => classType.Namespace != null ? c.Namespace.StartsWith(classType.Namespace) : true)
                .ToList();
        }
        if (candidates.Count > 1)
            throw new Exception($"Multiple class candidates found for {classType.Namespace} {classType.Name} on line {classType.Line}");
        else if (candidates.Count == 0)
            throw new Exception($"No class found for {classType.Namespace} {classType.Name} on line {classType.Line}");
        classType.CachedClass = candidates[0];
        classType.Namespace = candidates[0].Namespace;
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
            case IsExpression isExpression:
                {
                    Type sourceType = GetType(isExpression.Source);
                    if (sourceType is not ClassType)
                        throw new Exception($"is source must be class/interface type on line {isExpression.Line}");
                    locals.Push(new Dictionary<int, Type> { { isExpression.BindID, isExpression.TargetType } });
                    Type left = GetType(isExpression.True);
                    locals.Pop();
                    Type right = GetType(isExpression.False);
                    if (!TypeMatches(left, right, true))
                        throw new Exception($"is expression branch type mismatch on line {isExpression.Line}");
                    if (left is ClassType leftClass && right is ClassType rightClass)
                        return leftClass with { Nullable = rightClass.Nullable || leftClass.Nullable };
                    return left;
                }
            case AsExpression asExpression:
                {
                    Type sourceType = GetType(asExpression.Source);
                    if (sourceType is not ClassType)
                        throw new Exception($"as source must be class/interface type on line {asExpression.Line}");
                    return asExpression.TargetType with { Nullable = true };
                }
            case CallStaticExpression callStaticExpression:
                {
                    Method? method = callStaticExpression.cachedMethod;
                    if (method == null)
                    {
                        Class? @class = GetClass(callStaticExpression.Callee);
                        if (!GetMethod(ref @class, callStaticExpression.Name, callStaticExpression.Arguments.Select(GetType), out method))
                            throw new Exception($"Ambiguous/nonexistent method call {callStaticExpression.Name} on line {callStaticExpression.Line}");
                        callStaticExpression.cachedMethod = method;
                    }
                    return method!.ReturnType ?? throw new Exception($"Void returning method used in expression on line {callStaticExpression.Line}");
                }
            case CallInstanceExpression callInstanceExpression:
                {
                    Method? method = callInstanceExpression.cachedMethod;
                    if (method == null)
                    {
                        type = GetType(callInstanceExpression.Arguments[0]);
                        if (type is not ClassType classType)
                            throw new Exception("Cannot call on a non-class type you moron");
                        if (TryGetInterface(classType, out _))
                            throw new Exception($"Cannot call instance methods on interface-typed value on line {callInstanceExpression.Line}");
                        Class? @class = GetClass(classType);
                        if (!GetMethod(ref @class, callInstanceExpression.Name, callInstanceExpression.Arguments.Select(GetType), out method, true))
                            throw new Exception($"Ambiguous/nonexistent method call {callInstanceExpression.Name} on line {callInstanceExpression.Line}");
                        callInstanceExpression.cachedMethod = method;
                    }
                    return method!.ReturnType ?? throw new Exception($"Void returning method used in expression on line {callInstanceExpression.Line}");
                }
            case CallExpression callExpression:
                {
                    Method? method = callExpression.cachedMethod;
                    if (method == null)
                    {
                        Class? @class = null;
                        if (!GetMethod(ref @class, callExpression.Name, callExpression.Arguments.Select(GetType), out method))
                            throw new Exception($"Ambiguous/nonexistent method call {callExpression.Name} on line {callExpression.Line}");
                        callExpression.cachedMethod = method;
                    }
                    return method!.ReturnType ?? throw new Exception($"Void returning method used in expression on line {callExpression.Line}");
                }
            case ClassExpression classExpression:
                {
                    var (_, field, _) = ResolveUnqualifiedStaticField(classExpression.Class.Name, classExpression.Line);
                    return field.Type;
                }
            case StaticFieldExpression staticFieldExpression:
                {
                    Class @class = GetClass(staticFieldExpression.Class);
                    Field field = @class.StaticFields.FirstOrDefault(f => f.Name == staticFieldExpression.Field) ?? throw new Exception($"Static field {staticFieldExpression.Field} not found on line {staticFieldExpression.Line}");
                    return field.Type;
                }
            case InstanceFieldExpression instanceFieldExpression:
                {
                    type = GetType(instanceFieldExpression.Instance);
                    if (type is not ClassType classType)
                        throw new Exception("Cannot access instance field on a non-class type you moron");
                    if (TryGetInterface(classType, out _))
                        throw new Exception($"Cannot access fields on interface-typed value on line {instanceFieldExpression.Line}");
                    Class @class = GetClass(classType);
                    Field field = GetAllInstanceFields(@class).FirstOrDefault(f => f.Name == instanceFieldExpression.Field) ?? throw new Exception($"Instance field {instanceFieldExpression.Field} not found on line {instanceFieldExpression.Line}");

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
}
