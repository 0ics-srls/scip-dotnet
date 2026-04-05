namespace LibA;
public class Greeter
{
    public string Greet(string name) => new LibB.Formatter().Format(name);
}
