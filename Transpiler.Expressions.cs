using System.Security.Cryptography;
using System.Text;

public static partial class Transpiler
{
    static void TranslateArguments(List<Expression> arguments, IReadOnlyList<Type>? expectedTypes = null)
    {
        int i = 0;
        foreach (var arg in arguments)
        {
            if (i++ > 0)
                C(", ");
            if (expectedTypes != null)
                C($"({TranslateType(expectedTypes[i - 1])})");
            TranslateExpression(arg);
        }
    }
    static string CaptureExpressionText(Expression expression)
    {
        int start = c.Length;
        TranslateExpression(expression, false);
        string text = c.ToString(start, c.Length - start);
        c.Length = start;
        return text;
    }
    static void TranslateExpression(Expression expression, bool paren = true)
    {
        switch (expression)
        {
            case LocalExpression localExpression:
                if (inlineIsBindings.TryGetValue(localExpression.ID, out var inlineBinding))
                {
                    C($"({TranslateType(inlineBinding.target)})");
                    TranslateExpression(inlineBinding.source);
                }
                else
                    C($"l_{localExpression.ID}");
                break;
            case ArgumentExpression argumentExpression:
                C($"p_{argumentExpression.ID}");
                break;
            case CallStaticExpression callStaticExpression:
                {
                    Method? method = callStaticExpression.cachedMethod;
                    Class? @class = GetClass(callStaticExpression.Callee);
                    if (method == null)
                    {
                        if (!GetMethod(ref @class, callStaticExpression.Name, callStaticExpression.Arguments.Select(GetType), out method))
                            throw new Exception($"Ambiguous/nonexistent method call {callStaticExpression.Name} on line {callStaticExpression.Line}");
                        callStaticExpression.cachedMethod = method;
                    }
                    if (paren)
                        C("(");
                    C("static_method_call(");
                    C($"{BuildFunctionPointerType(method!)}, ");
                    C($"{@class!.Namespace}_{@class.Name}, ");
                    C($"{method!.i}, ");
                    TranslateArguments(callStaticExpression.Arguments, method!.Arguments);
                    C(")");
                    if (paren)
                        C(")");
                    break;
                }
            case CallExpression callExpression:
                {
                    Method? method = callExpression.cachedMethod;
                    Class? @class = callExpression.cachedClass;
                    if (method == null || @class == null)
                    {
                        if (!GetMethod(ref @class, callExpression.Name, callExpression.Arguments.Select(GetType), out method))
                            throw new Exception($"Ambiguous/nonexistent method call {callExpression.Name} on line {callExpression.Line}");
                        callExpression.cachedMethod = method;
                        callExpression.cachedClass = @class;
                    }
                    if (paren)
                        C("(");
                    C("static_method_call(");
                    C($"{BuildFunctionPointerType(method!)}, ");
                    C($"{@class!.Namespace}_{@class.Name}, ");
                    C($"{method!.i}, ");
                    TranslateArguments(callExpression.Arguments, method!.Arguments);
                    C(")");
                    if (paren)
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
            case IsExpression isExpression:
                {
                    Type left = GetType(isExpression.True);
                    Type right = GetType(isExpression.False);
                    if (!TypeMatches(left, right, true))
                        throw new Exception($"is expression branch type mismatch on line {isExpression.Line}");
                    string sourceText = CaptureExpressionText(isExpression.Source);
                    string sourceInstanceExpr = $"((Instance*)({sourceText}))";
                    if (paren)
                        C("(");
                    C(BuildRuntimeTypeCheckExpr(sourceInstanceExpr, isExpression.TargetType));
                    C(" ? ");
                    locals.Push(new Dictionary<int, Type> { { isExpression.BindID, isExpression.TargetType } });
                    inlineIsBindings[isExpression.BindID] = (isExpression.TargetType, isExpression.Source);
                    TranslateExpression(isExpression.True);
                    inlineIsBindings.Remove(isExpression.BindID);
                    locals.Pop();
                    C(" : ");
                    TranslateExpression(isExpression.False);
                    if (paren)
                        C(")");
                    break;
                }
            case AsExpression asExpression:
                {
                    Type sourceType = GetType(asExpression.Source);
                    if (sourceType is not ClassType)
                        throw new Exception($"as source must be class/interface type on line {asExpression.Line}");
                    string sourceText = CaptureExpressionText(asExpression.Source);
                    string sourceInstanceExpr = $"((Instance*)({sourceText}))";
                    string targetType = TranslateType(asExpression.TargetType with { Nullable = true });
                    if (paren)
                        C("(");
                    C($"({targetType})(");
                    C(BuildRuntimeTypeCheckExpr(sourceInstanceExpr, asExpression.TargetType));
                    C(" ? ");
                    C(sourceInstanceExpr);
                    C(" : NULL)");
                    if (paren)
                        C(")");
                    break;
                }
            case CallInstanceExpression callInstanceExpression:
                {
                    Type type = GetType(callInstanceExpression.Arguments[0]);
                    if (type is not ClassType classType)
                        throw new Exception($"Cannot call on a non-class type you moron on line {callInstanceExpression.Line}");
                    if (classType.Nullable)
                        throw new Exception($"Cannot call on a nullable type you moron on line {callInstanceExpression.Line}");
                    if (TryGetInterface(classType, out _))
                        throw new Exception($"Cannot call instance methods on interface-typed value on line {callInstanceExpression.Line}");
                    Method? method = callInstanceExpression.cachedMethod;
                    Class? @class = GetClass(classType);
                    if (method == null)
                    {
                        if (!GetMethod(ref @class, callInstanceExpression.Name, callInstanceExpression.Arguments.Select(GetType), out method, true))
                            throw new Exception($"Ambiguous/nonexistent method call {callInstanceExpression.Name} on line {callInstanceExpression.Line}");
                        callInstanceExpression.cachedMethod = method;
                    }
                    if (paren)
                        C("(");
                    C("static_method_call(");
                    C($"{BuildFunctionPointerType(method!)}, ");
                    C($"{@class!.Namespace}_{@class.Name}, ");
                    C($"{method!.i}, ");
                    TranslateArguments(callInstanceExpression.Arguments, method!.Arguments);
                    C(")");
                    if (paren)
                        C(")");
                    break;
                }
            case ClassExpression classExpression:
                {
                    var (@class, _, fieldId) = ResolveUnqualifiedStaticField(classExpression.Class.Name, classExpression.Line);
                    if (paren)
                        C("(");
                    C($"static_data({@class.Namespace}_{@class.Name})->f_{fieldId}");
                    if (paren)
                        C(")");
                    break;
                }
            case StaticFieldExpression staticFieldExpression:
                {
                    Class @class = GetClass(staticFieldExpression.Class);
                    int field = @class.StaticFields.FindIndex(f => f.Name == staticFieldExpression.Field);
                    if (field == -1)
                        throw new Exception($"Static field not found on line {staticFieldExpression.Line}");
                    if (paren)
                        C("(");
                    C($"static_data({@class.Namespace}_{@class.Name})->f_{field}");
                    if (paren)
                        C(")");
                    break;
                }
            case InstanceFieldExpression instanceFieldExpression:
                {
                    Type type = GetType(instanceFieldExpression.Instance);
                    if (type is not ClassType classType)
                        throw new Exception($"Cannot access instance field on a non-class type you moron on line {instanceFieldExpression.Line}");
                    if (TryGetInterface(classType, out _))
                        throw new Exception($"Cannot access fields on interface-typed value on line {instanceFieldExpression.Line}");
                    Class @class = GetClass(classType);
                    int field = GetAllInstanceFields(@class).FindIndex(f => f.Name == instanceFieldExpression.Field);
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
                    switch (binaryExpression.Op)
                    {
                        case "??":
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
                                break;
                            }
                        case "&&":
                            {
                                TranslateExpression(binaryExpression.Left);
                                C(" ? ");
                                TranslateExpression(binaryExpression.Right);
                                C(" : 0");
                                break;
                            }
                        case "||":
                            {
                                TranslateExpression(binaryExpression.Left);
                                C(" ? 1 : ");
                                TranslateExpression(binaryExpression.Right);
                                break;
                            }
                        default:
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
                                break;
                            }
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
                    if (paren)
                        C("(");
                    C($"({TranslateType(CurrentType)})runtime_new(state, \"{Namespace}\", \"{Name}\")");
                    if (paren)
                        C(")");
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
