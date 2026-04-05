  namespace LibB;
//          ^^^^ reference scip-dotnet nuget . . LibB/
  public class Formatter
//             ^^^^^^^^^ definition scip-dotnet nuget . . LibB/Formatter#
//                       documentation ```cs\nclass Formatter\n```
  {
      public string Format(string name) => $"Hello, {name}!";
//                  ^^^^^^ definition scip-dotnet nuget . . LibB/Formatter#Format().
//                         documentation ```cs\npublic string Formatter.Format(string name)\n```
//                                ^^^^ definition scip-dotnet nuget . . LibB/Formatter#Format().(name)
//                                     documentation ```cs\nstring name\n```
//                                                   ^^^^ reference scip-dotnet nuget . . LibB/Formatter#Format().(name)
  }
