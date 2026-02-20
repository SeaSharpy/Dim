
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