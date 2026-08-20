namespace Tarui.Contracts;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TaruiCommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
