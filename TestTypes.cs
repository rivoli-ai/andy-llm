using System;
using System.Linq;
using System.Reflection;

var assembly = Assembly.Load("Andy.Model");
var types = assembly.GetExportedTypes();

Console.WriteLine($"Found {types.Length} exported types in Andy.Model:");
foreach (var type in types.OrderBy(t => t.FullName))
{
    Console.WriteLine($"  - {type.FullName}");
}