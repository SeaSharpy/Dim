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