using System.Collections;
using System.Net;
using Newtonsoft.Json;

public abstract record Expression(int Line)
{
    [JsonIgnore]
    public Type? CachedType = null;
};
public record LocalExpression(int ID, int Line) : Expression(Line);
public record ArgumentExpression(int ID, int Line) : Expression(Line);
public record CallStaticExpression(ClassType Callee, string Name, List<Expression> Arguments, int Line) : Expression(Line)
{
    public Method? cachedMethod = null;
};
public record CallExpression(string Name, List<Expression> Arguments, int Line) : Expression(Line)
{
    public Class? cachedClass = null;
    public Method? cachedMethod = null;
};
public record CallInstanceExpression(string Name, List<Expression> Arguments, int Line) : Expression(Line)
{
    public Method? cachedMethod = null;
};
public record ClassExpression(ClassType Class, int Line) : Expression(Line);
public record StaticFieldExpression(ClassType Class, string Field, int Line) : Expression(Line);
public record InstanceFieldExpression(Expression Instance, string Field, int Line) : Expression(Line);
public record NumberExpression(string Value, int Line) : Expression(Line);
public record StringExpression(string Value, int Line) : Expression(Line);
public record BinaryExpression(Expression Left, string Op, Expression Right) : Expression(Left.Line);
public record UnaryExpression(string Op, Expression Right, int Line) : Expression(Line);
public record PostfixExpression(Expression Left, string Op, int Line) : Expression(Line);
public record IfExpression(Expression Condition, Expression True, Expression False, int Line) : Expression(Line);
public record NewExpression(int Line) : Expression(Line);
public record NilExpression(int Line) : Expression(Line);
public abstract record Statement(int Line);
public record CallStatement(Expression Expression, int Line) : Statement(Line);
public record ReturnStatement(Expression? Expression, int Line) : Statement(Line);
public record GcStatement(int Line) : Statement(Line);
public record AssignmentStatement(string Name, Expression Expression, int Line) : Statement(Line);
public record LocalAssignmentStatement(int ID, Expression Expression, int Line) : Statement(Line);
public record StaticFieldAssignmentStatement(StaticFieldExpression StaticField, Expression Expression, int Line) : Statement(Line);
public record InstanceFieldAssignmentStatement(InstanceFieldExpression InstanceField, Expression Expression, int Line) : Statement(Line);
public record WhileStatement(Expression Condition, Statement Body, int Line) : Statement(Line);
public record IfStatement(Expression Condition, Statement True, Statement? False, int Line) : Statement(Line);
public record BlockStatement(List<Statement> Body, Dictionary<int, Type> Locals, int Line) : Statement(Line);
public abstract record Type(string Name, int Line = 0)
{
    public virtual void BinaryOut(BinaryWriter writer, List<Class> classes)
    {
        writer.Write(false);
    }
    public static Type BinaryIn(BinaryReader reader)
    {
        bool exists = reader.ReadBoolean();
        if (!exists)
            throw new Exception("Type not found");
        bool isClass = reader.ReadBoolean();
        if (isClass)
        {
            string ns = reader.ReadString();
            string name = reader.ReadString();
            bool nullable = reader.ReadBoolean();
            return new ClassType(ns, name) { Nullable = nullable };
        }
        else
        {
            return new ValueType(reader.ReadString());
        }
    }
};
public record ValueType(string Name, int Line = 0) : Type(Name, Line)
{
    public override void BinaryOut(BinaryWriter writer, List<Class> classes)
    {
        writer.Write(true);
        writer.Write(false);
        writer.Write(Name);
    }
};
public record ClassType : Type
{
    public string? Namespace { get; set; }
    public Class? CachedClass;
    public bool Nullable { get; init; } = false;

    public ClassType(string? ns, string name, int line = 0) : base(name, line)
    {
        Namespace = ns;
    }

    public override void BinaryOut(BinaryWriter writer, List<Class> classes)
    {
        writer.Write(true);
        writer.Write(true);
        if (CachedClass != null)
        {
            writer.Write(CachedClass.Namespace);
            writer.Write(CachedClass.Name);
            return;
        }
        if (string.IsNullOrWhiteSpace(Namespace))
            throw new Exception($"Class type missing namespace for {Name}");
        writer.Write(Namespace);
        writer.Write(Name);
        writer.Write(Nullable);
    }
};
public record Method(string Name, List<Type> Arguments, Type? ReturnType, BlockStatement Body, int Line)
{
    public int i;
};
public record Field(string Name, Type Type, int Line);
public record Class(string Namespace, string Name, int Line, List<Method> Methods, List<Field> StaticFields, List<Field> InstanceFields)
{
    public void BinaryOut(BinaryWriter writer, List<Class> classes)
    {
        writer.Write(Namespace);
        writer.Write(Name);
        writer.Write(Methods.Count);
        writer.Write(StaticFields.Count);
        writer.Write(InstanceFields.Count);
        foreach (var method in Methods)
        {
            writer.Write(method.Name);
            writer.Write(method.Arguments.Count);
            foreach (var arg in method.Arguments)
                arg.BinaryOut(writer, classes);
            writer.Write(method.ReturnType != null);
            if (method.ReturnType != null)
                method.ReturnType.BinaryOut(writer, classes);
        }
        foreach (var field in StaticFields)
        {
            writer.Write(field.Name);
            field.Type.BinaryOut(writer, classes);
        }
        foreach (var field in InstanceFields)
        {
            writer.Write(field.Name);
            field.Type.BinaryOut(writer, classes);
        }
    }
    public static Class BinaryIn(BinaryReader reader)
    {
        string ns = reader.ReadString();
        string name = reader.ReadString();
        int methodCount = reader.ReadInt32();
        int staticFieldCount = reader.ReadInt32();
        int instanceFieldCount = reader.ReadInt32();
        var methods = new List<Method>(methodCount);
        for (int i = 0; i < methodCount; i++)
        {
            string methodName = reader.ReadString();
            int argCount = reader.ReadInt32();
            var args = new List<Type>(argCount);
            for (int j = 0; j < argCount; j++)
                args.Add(Type.BinaryIn(reader));
            bool hasReturnType = reader.ReadBoolean();
            Type? returnType = null;
            if (hasReturnType)
                returnType = Type.BinaryIn(reader);
            methods.Add(new Method(methodName, args, returnType, new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0), 0) { i = i });
        }
        var staticFields = new List<Field>(staticFieldCount);
        for (int i = 0; i < staticFieldCount; i++)
        {
            string fieldName = reader.ReadString();
            Type fieldType = Type.BinaryIn(reader);
            staticFields.Add(new Field(fieldName, fieldType, 0));
        }
        var instanceFields = new List<Field>(instanceFieldCount);
        for (int i = 0; i < instanceFieldCount; i++)
        {
            string fieldName = reader.ReadString();
            Type fieldType = Type.BinaryIn(reader);
            instanceFields.Add(new Field(fieldName, fieldType, 0));
        }
        return new Class(ns, name, 0, methods, staticFields, instanceFields);
    }
};
public record FileParseResult(List<Class> Classes, List<string> ImportedNamespaces, List<ClassType> UsingTypes, string Path = "");
public static class Parser
{
    static Dictionary<string, (Type type, int id)> arguments = new();
    static DictionaryStack<string, (Type type, int id)> locals = new();
    static Stack<int> localIDs = new();
    static readonly BlockStatement EmptyBlock = new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), 0);
    public static FileParseResult Parse(TokenSet tokens, string path)
    {
        try
        {
            return Parse_(tokens) with { Path = path };
        }
        catch (Exception e)
        {
            throw new Exception($"Error parsing at line {tokens.Get(false).line}: {e.Message}");
        }
    }
    static string ns = "";
    static string className = "";
    static List<Field> StaticFields = new();
    static Dictionary<string, Type> qualified = new();
    public static FileParseResult Parse_(TokenSet tokens)
    {
        List<Class> classes = new();
        List<string> importedNamespaces = ["STD"];
        List<ClassType> usingTypes = [new ClassType("STD", "STD")];
        qualified.Clear();
        ns = "";
        while (tokens.Safe())
            if (tokens.IsIdentifier("class"))
            {
                if (string.IsNullOrWhiteSpace(ns))
                    throw new Exception("Class must be defined after a namespace");
                className = tokens.Identifier(out int classLine);
                tokens.Symbol("{");
                List<Method> methods = new();
                List<Field> staticFields = new();
                StaticFields = staticFields;
                List<Field> instanceFields = new();
                while (!tokens.IsSymbol("}"))
                {
                    bool static_ = tokens.IsIdentifier("static");
                    string typeId = tokens.Identifier(out int typeLine);
                    Type? type = null;
                    if (typeId != "void")
                        type = ParseType(tokens, typeId, typeLine);
                    string name = tokens.Identifier(out int nameLine);
                    if (name == "Unbox" || name == "Box")
                        throw new Exception("Cannot use Unbox or Box as a member name");
                    if (tokens.IsSymbol(";"))
                        (static_ ? staticFields : instanceFields).Add(new Field(name, type ?? throw new Exception("Fields cannot use void type"), nameLine));
                    else
                    {
                        var args = new List<Type>();
                        int id = 0;
                        arguments.Clear();
                        if (!static_)
                        {
                            arguments.Add("self", (new ClassType(ns, className, nameLine), id++));
                            args.Add(new ClassType(ns, className, nameLine));
                        }
                        if (tokens.IsSymbol("("))
                            while (!tokens.IsSymbol(")"))
                            {
                                Type argType = ParseType(tokens);
                                string argName = tokens.Identifier(out _);
                                if (arguments.ContainsKey(argName))
                                    throw new Exception($"Duplicate argument name '{argName}'");
                                arguments.Add(argName, (argType, id++));
                                args.Add(argType);
                                if (!tokens.IsSymbol(","))
                                {
                                    tokens.Symbol(")");
                                    break;
                                }
                            }
                        else if (!tokens.IsSymbol("!"))
                            throw new Exception("Expected ! or ( or ; for member of class");
                        locals.Push();
                        localIDs.Push(0);
                        if (!tokens.IsSymbol("{", out int openLine))
                            throw new Exception("Expected { for method body");
                        var block = ParseBlock(tokens, openLine);
                        localIDs.Pop();
                        locals.Pop();
                        methods.Add(new Method(name, args, type, block, nameLine) { i = methods.Count });
                    }
                }
                if (instanceFields.Count > 0)
                {
                    methods.Add(new Method("Box", [new ClassType(ns, className) { Nullable = true }], new ClassType("STD", "Any"), EmptyBlock, classLine) { i = methods.Count });
                    methods.Add(new Method("Unbox", [new ClassType("STD", "Any") { Nullable = true }], new ClassType(ns, className) { Nullable = true }, EmptyBlock, classLine) { i = methods.Count });
                }
                classes.Add(new Class(ns, className, classLine, methods, staticFields, instanceFields));
            }
            else if (tokens.IsIdentifier("import"))
            {
                string importName = tokens.Identifier(out _);
                importedNamespaces.Add(importName);
                tokens.Symbol(";");
            }
            else if (tokens.IsIdentifier("namespace"))
            {
                ns = tokens.Identifier(out _);
                tokens.Symbol(";");
            }
            else if (tokens.IsIdentifier("type"))
            {
                string alias = tokens.Identifier(out _);
                tokens.Symbol("=");
                string namespace_ = tokens.Identifier(out _);
                string name = tokens.Identifier(out _);
                qualified[alias] = new ClassType(namespace_, name);
                tokens.Symbol(";");
            }
            else if (tokens.IsIdentifier("using", out var usingLine))
            {
                ClassType type = ParseType(tokens) as ClassType ?? throw new Exception("Expected class type for using");
                if (type.Nullable)
                    throw new Exception("Nullable types are not supported for using, wtf are you doing lol");
                tokens.Symbol(";");
                usingTypes.Add(type);
            }
            else
                throw new Exception("Expected import, namespace, or class");
        return new FileParseResult(classes, importedNamespaces, usingTypes);
    }
    static readonly string[] valueTypes = new[]
    {
        "bool", "int", "uint", "long", "ulong", "float", "double", "sbyte", "byte", "char", "short", "ushort", "cstr"
    };
    static Type ParseType(TokenSet tokens, string typeName, int typeLine)
    {
        Type type = valueTypes.Contains(typeName)
            ? new ValueType(typeName, typeLine)
            : new ClassType(null, typeName, typeLine);
        if (qualified.TryGetValue(typeName, out var qualifiedType))
            type = qualifiedType with { Line = typeLine };

        if (tokens.IsSymbol("?"))
        {
            if (type is ClassType classType)
                type = classType with { Nullable = true };
            else
                throw new Exception("Only class types can be nullable");
        }

        return type;
    }
    static Type ParseType(TokenSet tokens)
    {
        string typeName = tokens.Identifier(out int typeLine);
        return ParseType(tokens, typeName, typeLine);
    }
    static Statement ParseStatement(TokenSet tokens)
    {
        int line;
        if (tokens.IsSymbol("{", out line))
            return ParseBlock(tokens, line);
        if (tokens.IsIdentifier("if", out line))
        {
            var condition = ParseExpression(tokens);

            tokens.Symbol(";");
            var trueBody = ParseStatement(tokens);
            Statement? falseBody = null;
            if (tokens.IsIdentifier("else"))
            {
                falseBody = ParseStatement(tokens);
            }
            return new IfStatement(condition, trueBody, falseBody, line);
        }
        if (tokens.IsIdentifier("while", out line))
        {
            var condition = ParseExpression(tokens);

            tokens.Symbol(";");
            var body = ParseStatement(tokens);
            return new WhileStatement(condition, body, line);
        }
        if (tokens.IsIdentifier("return", out line))
        {
            if (tokens.IsSymbol(";"))
                return new ReturnStatement(null, line);
            var expression = ParseExpression(tokens);
            tokens.Symbol(";");
            return new ReturnStatement(expression, line);
        }
        if (tokens.IsIdentifier("gc", out line))
        {
            tokens.Symbol(";");
            return new GcStatement(line);
        }
        if (tokens.IsSymbol(";", out line))
            return new BlockStatement(new List<Statement>(), new Dictionary<int, Type>(), line);
        tokens.Push();
        Type type = ParseType(tokens);
        string name = tokens.IdentifierSafe(out int nameLine);
        if (tokens.IsSymbol("="))
        {
            var expression = ParseExpression(tokens);

            tokens.Symbol(";");
            if (locals.TryGet(name, out _))
                throw new Exception($"Duplicate local variable name '{name}'");
            int id = localIDs.Pop() + 1;
            localIDs.Push(id);
            locals.Set(name, (type, id));

            return new LocalAssignmentStatement(id, expression, line);
        }
        else
        {
            tokens.Pop();
            Expression lhs = ParseExpression(tokens);
            if (lhs is CallStaticExpression or CallInstanceExpression or CallExpression)
            {
                tokens.Symbol(";");
                return new CallStatement(lhs, line);
            }
            else
            {
                tokens.Symbol("=");
                Expression rhs = ParseExpression(tokens);
                tokens.Symbol(";");
                if (lhs is LocalExpression localExpression)
                    return new LocalAssignmentStatement(localExpression.ID, rhs, line);
                else if (lhs is StaticFieldExpression staticFieldExpression)
                    return new StaticFieldAssignmentStatement(staticFieldExpression, rhs, line);
                else if (lhs is InstanceFieldExpression instanceFieldExpression)
                    return new InstanceFieldAssignmentStatement(instanceFieldExpression, rhs, line);
                else if (lhs is ClassExpression classExpression)
                    if (classExpression.Class.Nullable)
                        throw new Exception($"Invalid assignment target");
                    else
                        return new AssignmentStatement(classExpression.Class.Name, rhs, line);
                else
                    throw new Exception($"Invalid assignment target");
            }
        }
    }
    static BlockStatement ParseBlock(TokenSet tokens, int line)
    {
        locals.Push();
        localIDs.Push(localIDs.Peek());
        var statements = new List<Statement>();
        while (!tokens.IsSymbol("}"))
        {
            var statement = ParseStatement(tokens);
            statements.Add(statement);
        }
        var localsDeclared = new Dictionary<int, Type>();
        foreach (var (_, (type, id)) in locals.Peek())
        {
            localsDeclared.Add(id, type);
        }
        localIDs.Pop();
        locals.Pop();
        return new BlockStatement(statements, localsDeclared, line);
    }
    private enum Assoc
    {
        Left,
        Right
    }

    private readonly struct OpInfo
    {
        public readonly int Prec;
        public readonly Assoc Assoc;

        public OpInfo(int prec, Assoc assoc)
        {
            Prec = prec;
            Assoc = assoc;
        }
    }

    static readonly Dictionary<string, OpInfo> BinaryOps = new()
    {
        ["??"] = new OpInfo(20, Assoc.Right),

        ["||"] = new OpInfo(30, Assoc.Left),
        ["&&"] = new OpInfo(40, Assoc.Left),

        ["|"] = new OpInfo(50, Assoc.Left),
        ["^"] = new OpInfo(60, Assoc.Left),
        ["&"] = new OpInfo(70, Assoc.Left),

        ["=="] = new OpInfo(80, Assoc.Left),
        ["!="] = new OpInfo(80, Assoc.Left),

        ["<"] = new OpInfo(90, Assoc.Left),
        [">"] = new OpInfo(90, Assoc.Left),
        ["<="] = new OpInfo(90, Assoc.Left),
        [">="] = new OpInfo(90, Assoc.Left),

        ["<<"] = new OpInfo(100, Assoc.Left),
        [">>"] = new OpInfo(100, Assoc.Left),

        ["+"] = new OpInfo(110, Assoc.Left),
        ["-"] = new OpInfo(110, Assoc.Left),

        ["*"] = new OpInfo(120, Assoc.Left),
        ["/"] = new OpInfo(120, Assoc.Left),
        ["%"] = new OpInfo(120, Assoc.Left),
    };
    private static readonly HashSet<string> PrefixOps = new()
    {
        "!", "~", "+", "-"
    };
    static Expression ParseExpression(TokenSet tokens, int minPrec = 0)
    {
        var left = ParsePrefix(tokens);

        left = ParsePostfix(tokens, left);

        while (tokens.Safe())
        {
            tokens.Push();
            if (!tokens.SymbolSafe(out string op, out int opLine) || !BinaryOps.TryGetValue(op, out var info))
            {
                tokens.Pop();
                break;
            }

            if (info.Prec < minPrec)
            {
                tokens.Pop();
                break;
            }

            tokens.Compress();

            int nextMinPrec = info.Assoc == Assoc.Left ? info.Prec + 1 : info.Prec;
            var right = ParseExpression(tokens, nextMinPrec);
            left = new BinaryExpression(left, op, right);
        }

        return left;
    }

    static Expression ParsePrefix(TokenSet tokens)
    {
        tokens.Push();
        if (tokens.SymbolSafe(out string op, out int opLine))
            if (PrefixOps.Contains(op))
            {
                tokens.Compress();
                var right = ParseExpression(tokens);
                return new UnaryExpression(op, right, opLine);
            }
            else if (op == "(")
            {
                tokens.Compress();
                var expression = ParseExpression(tokens);
                tokens.Symbol(")");
                return expression;
            }
        tokens.Pop();
        Token token = tokens.Get();
        if (token.type == TokenType.Identifier)
            if (token.value == "new")
                return new NewExpression(token.line);
            else if (token.value == "true")
                return new NumberExpression("1", token.line);
            else if (token.value == "false")
                return new NumberExpression("0", token.line);
            else if (token.value == "if")
            {
                Expression condition = ParseExpression(tokens);
                tokens.Symbol(";");
                Expression trueBody = ParseExpression(tokens);
                tokens.Identifier("else");
                Expression falseBody = ParseExpression(tokens);
                return new IfExpression(condition, trueBody, falseBody, token.line);
            }
            else if (token.value == "nil")
                return new NilExpression(token.line);
            else if (locals.TryGet(token.value, out var local))
                return new LocalExpression(local.id, token.line);
            else if (arguments.TryGetValue(token.value, out var arg))
                return new ArgumentExpression(arg.id, token.line);
            else
                return new ClassExpression(new ClassType(null, token.value, token.line), token.line);

        else if (token.type == TokenType.Number)
            return new NumberExpression(token.value, token.line);
        else if (token.type == TokenType.String)
            return new CallStaticExpression(new ClassType("STD", "String"), "New", [new StringExpression(token.value, token.line)], token.line);
        throw new Exception($"Invalid expression {token.value}");
    }

    static Expression ParsePostfix(TokenSet tokens, Expression left)
    {
        while (true)
        {
            if (tokens.IsSymbol("."))
            {
                string name = tokens.Identifier(out int nameLine);

                if (left is ClassExpression classExpression)
                    left = new StaticFieldExpression(classExpression.Class, name, nameLine);
                else
                    left = new InstanceFieldExpression(left, name, nameLine);
                continue;
            }
            if (tokens.IsSymbol("[", out int bracketLine))
            {
                var indexArgs = new List<Expression>
                {
                    left,
                    ParseExpression(tokens)
                };
                if (tokens.IsSymbol(","))
                    throw new Exception("Bracket indexing only supports one argument");
                tokens.Symbol("]");
                left = new CallInstanceExpression("Get", indexArgs, bracketLine);
                continue;
            }
            if (tokens.IsSymbol(out string callValue, "!", "("))
            {
                List<Expression> arguments = new();
                Expression call;
                if (left is StaticFieldExpression staticFieldExpression)
                    call = new CallStaticExpression(staticFieldExpression.Class, staticFieldExpression.Field, arguments, left.Line);
                else if (left is InstanceFieldExpression instanceFieldExpression)
                {
                    call = new CallInstanceExpression(instanceFieldExpression.Field, arguments, left.Line);
                    arguments.Add(instanceFieldExpression.Instance);
                }
                else if (left is ClassExpression classExpression)
                    if (classExpression.Class.Nullable)
                        throw new Exception($"Invalid call target (must be a method on an instance or a static)");
                    else
                        call = new CallExpression(classExpression.Class.Name, arguments, left.Line);
                else throw new Exception("Invalid call target (must be a method on an instance or a static)");
                if (callValue == "(")
                    while (!tokens.IsSymbol(")"))
                    {
                        arguments.Add(ParseExpression(tokens));
                        if (!tokens.IsSymbol(","))
                        {
                            tokens.Symbol(")");
                            break;
                        }
                    }
                left = call;
                continue;
            }
            if (tokens.IsSymbol("@", out int opLine))
            {
                left = new PostfixExpression(left, "@", opLine);
                continue;
            }
            return left;
        }
    }
}
