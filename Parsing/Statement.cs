
public abstract record Statement(int Line);
public record CallStatement(Expression Expression, int Line) : Statement(Line);
public record ReturnStatement(Expression? Expression, int Line) : Statement(Line);
public record GcStatement(int Line) : Statement(Line);
public record AssignmentStatement(string Name, Expression Expression, int Line) : Statement(Line);
public record TryStatement(CallStatement Body, Dictionary<ClassType, CallStatement> Catchers, int Line) : Statement(Line);
public record ThrowStatement(Expression Expression, int Line) : Statement(Line);
public record LocalAssignmentStatement(int ID, Expression Expression, int Line) : Statement(Line);
public record StaticFieldAssignmentStatement(StaticFieldExpression StaticField, Expression Expression, int Line) : Statement(Line);
public record InstanceFieldAssignmentStatement(InstanceFieldExpression InstanceField, Expression Expression, int Line) : Statement(Line);
public record WhileStatement(Expression Condition, Statement Body, int Line) : Statement(Line);
public record IfStatement(Expression Condition, Statement True, Statement? False, int Line) : Statement(Line);
public record IsStatement(ClassType TargetType, int BindID, Expression Source, Statement True, Statement? False, int Line) : Statement(Line);
public record BlockStatement(List<Statement> Body, Dictionary<int, Type> Locals, int Line) : Statement(Line);
public record EmptyStatement(int Line) : Statement(Line);
