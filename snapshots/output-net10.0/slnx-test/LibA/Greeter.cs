  namespace LibA;
//          ^^^^ reference scip-dotnet nuget . . LibA/
  public class Greeter
//             ^^^^^^^ definition scip-dotnet nuget . . LibA/Greeter#
//                     documentation ```cs\nclass Greeter\n```
  {
      public string Greet(string name) => new LibB.Formatter().Format(name);
//                  ^^^^^ definition scip-dotnet nuget . . LibA/Greeter#Greet().
//                        documentation ```cs\npublic string Greeter.Greet(string name)\n```
//                               ^^^^ definition scip-dotnet nuget . . LibA/Greeter#Greet().(name)
//                                    documentation ```cs\nstring name\n```
//                                            ^^^^ reference scip-dotnet nuget . . LibB/
//                                                 ^^^^^^^^^ reference scip-dotnet nuget . . LibB/Formatter#
//                                                             ^^^^^^ reference scip-dotnet nuget . . LibB/Formatter#Format().
//                                                                    ^^^^ reference scip-dotnet nuget . . LibA/Greeter#Greet().(name)
  }
