using System.Collections;
using System.Net;

public record FileParseResult(List<Class> Classes, List<string> ImportedNamespaces, List<ClassType> UsingTypes, string Path = "");
public static class Parser
{
    static Dictionary<string, (Type type, int id)> arguments = new();
    static DictionaryStack<string, (Type type, int id)> locals = new();
    static Stack<int> localIDs = new();
    public static FileParseResult Parse(TokenSet tokens, string path)
    {
        try
        {
            return Parse(tokens) with { Path = path };
        }
        catch (Exception e)
        {
            throw new Exception($"Error parsing at line {tokens.Get(false).line}: {e.Message}");
        }
    }
    static string ns = "";
    static string className = "";
    static Dictionary<string, Type> qualified = new();
    public static FileParseResult Parse(TokenSet tokens)
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
                        Statement statement;
                        if (tokens.IsSymbol("=>", out int arrowLine))
                        {
                            statement = new ReturnStatement(ParseExpression(tokens), arrowLine);
                            tokens.Symbol(";");
                        }
                        else
                            statement = ParseStatement(tokens);
                        localIDs.Pop();
                        locals.Pop();
                        methods.Add(new Method(name, args, type, statement, nameLine) { i = methods.Count });
                    }
                }
                if (instanceFields.Count > 0)
                {
                    methods.Add(new Method("Box", [new ClassType(ns, className) { Nullable = true }], new ClassType("STD", "Any"), new EmptyStatement(classLine), classLine) { i = methods.Count });
                    methods.Add(new Method("Unbox", [new ClassType("STD", "Any") { Nullable = true }], new ClassType(ns, className) { Nullable = true }, new EmptyStatement(classLine), classLine) { i = methods.Count });
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
    static Statement ParseIfStatement(TokenSet tokens, int line)
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
    static Statement ParseWhileStatement(TokenSet tokens, int line)
    {
        var condition = ParseExpression(tokens);

        tokens.Symbol(";");
        var body = ParseStatement(tokens);
        return new WhileStatement(condition, body, line);
    }
    static Statement ParseReturnStatement(TokenSet tokens, int line)
    {
        if (tokens.IsSymbol(";"))
            return new ReturnStatement(null, line);
        var expression = ParseExpression(tokens);
        tokens.Symbol(";");
        return new ReturnStatement(expression, line);
    }
    static Statement ParseGcStatement(TokenSet tokens, int line)
    {
        tokens.Symbol(";");
        return new GcStatement(line);
    }
    static Statement ParseDeclarationStatement(TokenSet tokens, Type type, string name, int line)
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
    static Statement ParseAssignmentStatement(TokenSet tokens, Expression lhs)
    {
        tokens.Symbol("=");
        Expression rhs = ParseExpression(tokens);
        tokens.Symbol(";");
        if (lhs is LocalExpression localExpression)
            return new LocalAssignmentStatement(localExpression.ID, rhs, lhs.Line);
        else if (lhs is StaticFieldExpression staticFieldExpression)
            return new StaticFieldAssignmentStatement(staticFieldExpression, rhs, lhs.Line);
        else if (lhs is InstanceFieldExpression instanceFieldExpression)
            return new InstanceFieldAssignmentStatement(instanceFieldExpression, rhs, lhs.Line);
        else if (lhs is ClassExpression classExpression)
            if (classExpression.Class.Nullable)
                throw new Exception($"Invalid assignment target");
            else
                return new AssignmentStatement(classExpression.Class.Name, rhs, lhs.Line);
        else
            throw new Exception($"Invalid assignment target");
    }
    static Statement ParseStatement(TokenSet tokens)
    {
        int line;
        if (tokens.IsSymbol("{", out line))
            return ParseBlockStatement(tokens, line);
        if (tokens.IsIdentifier("if", out line))
            return ParseIfStatement(tokens, line);
        if (tokens.IsIdentifier("while", out line))
            return ParseWhileStatement(tokens, line);
        if (tokens.IsIdentifier("return", out line))
            return ParseReturnStatement(tokens, line);
        if (tokens.IsIdentifier("gc", out line))
            return ParseGcStatement(tokens, line);
        if (tokens.IsSymbol(";", out line))
            return new EmptyStatement(line);
        tokens.Push();
        Type type = ParseType(tokens);
        string name = tokens.IdentifierSafe(out line);
        if (tokens.IsSymbol("="))
            return ParseDeclarationStatement(tokens, type, name, line);
        else
        {
            tokens.Pop();
            Expression exp = ParseExpression(tokens);
            if (exp is CallStaticExpression or CallInstanceExpression or CallExpression)
            {
                tokens.Symbol(";");
                return new CallStatement(exp, line);
            }
            else
                return ParseAssignmentStatement(tokens, exp);
        }
    }
    static BlockStatement ParseBlockStatement(TokenSet tokens, int line)
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
    static Expression ParseIdentifier(TokenSet tokens, Token token)
    {
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
            return ParseIdentifier(tokens, token);
        else if (token.type == TokenType.Number)
            return new NumberExpression(token.value, token.line);
        else if (token.type == TokenType.String)
            return ForString(token);
        throw new Exception($"Invalid expression {token.value}");
    }
    static Expression ForString(Token token) => new CallStaticExpression(new ClassType("STD", "String"), "New", [new StringExpression(token.value, token.line)], token.line);
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
                List<Expression> indexArgs = [left, ParseExpression(tokens)];
                tokens.Symbol("]");
                left = new CallInstanceExpression("Get", indexArgs, bracketLine);
                continue;
            }
            if (tokens.IsSymbol(out string callValue, "!", "("))
            {
                left = ParseCall(tokens, left, callValue);
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
    static Expression ParseCall(TokenSet tokens, Expression left, string callValue)
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
        return call;
    }
}
