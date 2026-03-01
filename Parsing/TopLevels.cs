
public record Method(string Name, List<Type> Arguments, Type? ReturnType, Statement Body, int Line)
{
    public int i;
};
public record InterfaceMethod(string Name, List<Type> Arguments, Type? ReturnType, int Line);
public record Field(string Name, Type Type, int Line);
public record InterfaceDef(string Namespace, string Name, int Line, List<InterfaceMethod> Methods);
public record Class(
    string Namespace,
    string Name,
    int Line,
    List<Method> Methods,
    List<Field> StaticFields,
    List<Field> InstanceFields,
    ClassType? Base = null,
    List<ClassType>? Interfaces = null)
{
    public void BinaryOut(BinaryWriter writer, List<Class> classes)
    {
        writer.Write(Namespace);
        writer.Write(Name);
        writer.Write(Base != null);
        if (Base != null)
            Base.BinaryOut(writer, classes);
        writer.Write(Interfaces?.Count ?? 0);
        foreach (var iface in Interfaces ?? [])
            iface.BinaryOut(writer, classes);
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
        ClassType? baseType = null;
        bool hasBase = reader.ReadBoolean();
        if (hasBase)
            baseType = Type.BinaryIn(reader) as ClassType ?? throw new Exception("Invalid base class type in binary");
        int interfaceCount = reader.ReadInt32();
        var interfaces = new List<ClassType>(interfaceCount);
        for (int i = 0; i < interfaceCount; i++)
            interfaces.Add(Type.BinaryIn(reader) as ClassType ?? throw new Exception("Invalid interface type in binary"));
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
        return new Class(ns, name, 0, methods, staticFields, instanceFields, baseType, interfaces);
    }
};
