using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text;
using static CLC.CLCPublic;

namespace CLC
{
    public class Scope
    {
        private Dictionary<string, Variable> Variables { get; } = new();
        public Scope? Parent { get; }

        public Scope(Scope? parent = null)
        {
            Parent = parent;
        }

        public Variable? Get(string name)
        {
            if (Variables.TryGetValue(name, out var value))
                return value;

            return Parent?.Get(name);
        }

        public bool TryGet(string name, out Variable value)
        {
            if (Variables.TryGetValue(name, out value))
                return true;
            if (Parent != null)
                return Parent.TryGet(name, out value);

            value = null!;
            return false;
        }
        public bool Contains(string name)
        {
            var scope = this;
            while (scope != null)
            {
                if (scope.Variables.ContainsKey(name))
                    return true;

                scope = scope.Parent;
            }
            return false;
        }
        public void Set(string name, Variable value)
        {
            // Find the scope where variable is already defined (closest first)
            var scope = this;
            while (scope != null)
            {
                if (scope.Variables.ContainsKey(name))
                {
                    scope.Variables[name] = value;
                    return;
                }
                scope = scope.Parent;
            }

            // Not found, define in current scope
            Variables[name] = value;
        }
        public void Remove(string name)
        {
            // Find the scope where variable is already defined (closest first)
            var scope = this;
            while (scope != null)
            {
                if (scope.Variables.ContainsKey(name))
                {
                    scope.Variables.Remove(name);
                    return;
                }
                scope = scope.Parent;
            }
        }

        public void Define(string name, Variable value)
        {
            // Always define in current scope (no upwalking)
            Variables[name] = value;
        }
        public void AssignOrDefine(string name, Variable value)
        {
            if (Contains(name))
                Set(name, value);
            else
                Define(name, value);
        }

    }

    public class LineFunction
    {
        public string Name;
        public Variable[] Args;
        public string ArgStr;
        public char Denom;
    }
    public class UserDefinedFunction
    {
        public string Name;
        public string[] ArgNameList;
        public int ArgCount;
        public string? FunctionValue;
    }
    public class Variable
    {
        public Variable(object val, Type _T)
        {
            T = _T;
            Value = val;
        }
        public object Value;
        public Type T;
    }
    public static class IEnumerableExtensions
    {
        public static string Unescape(string input)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    i++;
                    sb.Append(input[i] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        ',' => ',',  // For your case: escaped separator
                        '"' => '"',
                        '\'' => '\'',
                        _ => input[i] // Unknown escape? keep it as-is
                    });
                }
                else
                {
                    sb.Append(input[i]);
                }
            }

            return sb.ToString();
        }
        public static List<string> SplitTopLevelCommas(string input, char sep, char spL, char spR)
        {
            var result = new List<string>();
            int depth = 0;
            bool escape = false;
            var current = new StringBuilder();

            foreach (char c in input)
            {
                if (escape)
                {
                    current.Append(Unescape("\\" + c)[0]);
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == spL) depth++;
                else if (c == spR) depth--;
                else if (c == sep && depth == 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result.ToList();
        }
        public static int FindNthIndex<T>(this IEnumerable<T> enumerable, Predicate<T> match, int count)
        {
            var index = 0;

            foreach (var item in enumerable)
            {
                if (match.Invoke(item))
                    count--;
                if (count == 0)
                    return index;
                index++;
            }

            return -1;
        }
        public static int FindNthIndexFromLast<T>(this IEnumerable<T> enumerable, Predicate<T> match, int count)
        {
            var index = enumerable.Count();

            foreach (var item in enumerable.Reverse())
            {
                if (match.Invoke(item))
                    count--;
                if (count == 0)
                    return index - 1;
                index--;
            }

            return -1;
        }
    }
    public static class CLCPublic
    {
       public static int GetMatchScore(ParameterInfo[] parameters, Variable[] args)
        {
            if (parameters.Length != args.Length)
                return -1;

            int score = 0;

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                var arg = args[i];

                if (arg.T == paramType)
                    score += 20; // exact match
                else if (paramType.IsAssignableFrom(arg.T))
                    score += 10;
                else if (arg.Value != null && paramType.IsAssignableFrom(arg.Value.GetType()))
                    score += 5;
                else if (CanConvert(arg.Value, paramType))
                    score += 2;
                else
                    return -1; // not compatible
            }

            return score;
        }

        private static bool CanConvert(object? value, Type targetType)
        {
            try
            {
                if (value == null)
                    return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

                Convert.ChangeType(value, targetType);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void RegisterType(
    Dictionary<string, Func<Scope, Variable[], Variable?>> registry,
    string prefix,
    Type type)
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);

            var grouped = methods
                .Where(m => !m.IsSpecialName && !m.IsGenericMethod)
                .GroupBy(m => m.Name.ToLower())
                .ToDictionary(g => g.Key, g => g.ToArray());

            foreach (var (key, overloads) in grouped)
            {
                registry[$"{prefix}.{key}"] = (Scope scope, Variable[] args) =>
                {
                    if (args.Length < 1 || args[0].T != type)
                        throw new Exception($"First argument of '{prefix}.{key}' must be {type.Name}");

                    object instance = args[0].Value;
                    var userArgs = args.Skip(1).ToArray();

                    var best = overloads
                        .Select(m => new {
                            Method = m,
                            Params = m.GetParameters(),
                            Score = GetMatchScore(m.GetParameters(), userArgs)
                        })
                        .Where(x => x.Score >= 0)
                        .OrderByDescending(x => x.Score)
                        .FirstOrDefault();

                    if (best == null)
                        throw new Exception($"No suitable overload found for '{prefix}.{key}'");

                    object[] parameters = new object[best.Params.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var paramType = best.Params[i].ParameterType;
                        object value = userArgs[i].Value;

                        if (value == null && paramType.IsValueType)
                            throw new Exception($"Cannot pass null to value-type parameter '{paramType.Name}'");

                        parameters[i] = Convert.ChangeType(value, paramType);
                    }

                    object? result = best.Method.Invoke(instance, parameters);
                    if (best.Method.ReturnType == typeof(void))
                        return null;

                    return new Variable(result!, best.Method.ReturnType);
                };
            }
        }
        public abstract class DLLClass
        {
            public static Dictionary<string, Func<Scope, Variable[], Variable?>> Run = new();
            public static Dictionary<string, Type> RunTypes = new();
            public static Dictionary<string, object> RunTypesDefault = new();
        }
        public static void ThrowError(string func, string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static void ThrowArgumentError(string func, int needed, int got)
        {
            string message = $"Need {needed} {(needed > 1 ? "Arguments" : "Argument")}! Provided {got}";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static void ThrowArgumentError(string func, int[] needed, int got)
        {
            string message = $"Need either one of {needed.Select(x => x + ",")} Arguments! Provided {got}";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static void ThrowTypeError(string func, Type needed, Type got)
        {
            string message = $"Expected '{needed.Name}' got '{got.Name}'";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static void ThrowTypeError(string func, Type[] needed, Type got)
        {
            string message = $"Expected either one of '{needed.Select(x => x.Name + ",")}' got '{got.Name}'";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static void ThrowNullError(string func)
        {
            string message = $"Variable provided was null";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static void ThrowNotFoundVariableError(string func, string name)
        {
            string message = $"Variable {name} does not exist yet!";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static void ThrowNullError(string func, string name)
        {
            string message = $"Variable {name} was null";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error accured at " + func + ": " + message);

            Environment.Exit(-1);
        }
        public static IList GetListFromVariable(string funcName, Variable variable)
        {
            if (variable.T == null)
                ThrowError(funcName, $"{funcName}: variable type is null.");

            if (!variable.T.IsGenericType || variable.T.GetGenericTypeDefinition() != typeof(List<>))
                ThrowError(funcName, $"{funcName}: variable is not a List<T> (got {variable.T.Name}).");

            var list = variable.Value as IList;
            if (list == null)
                ThrowError(funcName, $"{funcName}: variable value is not a valid IList instance.");
            
            return list!;
        }
        public static Type GetElementTypeFromListVariable(string funcName, Variable variable)
        {
            if (!variable.T.IsGenericType || variable.T.GetGenericTypeDefinition() != typeof(List<>))
                ThrowError(funcName, $"{funcName}: variable is not a List<T>");

            return variable.T.GetGenericArguments()[0];
        }
        public static bool IsListVariable(Variable variable)
        {
            if (variable.Value is not IList)
                return false;

            if (variable.T == null)
                return false;

            // Accept if T is generic List<> or IList<>
            if (variable.T.IsGenericType)
            {
                var genDef = variable.T.GetGenericTypeDefinition();
                if (genDef == typeof(List<>) || genDef == typeof(IList<>))
                    return true;
            }

            // Also accept non-generic IList type (fallback)
            if (variable.T == typeof(IList))
                return true;

            return false;
        }

    }
    internal class Program
    {
        private static Dictionary<string, UserDefinedFunction> userdefinedFunctions = new Dictionary<string, UserDefinedFunction>();
        private static Dictionary<string, Func<Scope, Variable[], Variable?>> baselineFunctions = new Dictionary<string, Func<Scope, Variable[], Variable?>>()
        {
            {"print", (Scope scope, Variable[] value) => {
                Console.Write(String.Join("", value.Select(x => x.Value.ToString()))); return null;
            } },
            {"println", (Scope scope, Variable[] value) => {
                Console.WriteLine(String.Join("", value.Select(x => x.Value.ToString()))); return null;
            } },
            {"read", (Scope scope, Variable[] value) => {
                const string f = "read";
                if(value.Length != 1)
                    ThrowArgumentError(f, 1, value.Length);

                if(value[0].T != typeof(string))
                    ThrowTypeError(f, typeof(string), value[0].T);

                if(!scope.Contains((string)value[0].Value))
                    ThrowNotFoundVariableError(f, (string)value[0].Value);

                scope.AssignOrDefine((string)value[0].Value, new Variable((char)Console.ReadKey().KeyChar, typeof(char)));
                
                return null;
            } },
            {"readln", (Scope scope, Variable[] value) => {
                const string f = "readln";
                if(value.Length != 1)
                    ThrowArgumentError(f, 1, value.Length);

                if(value[0].T != typeof(string))
                    ThrowTypeError(f, typeof(string), value[0].T);

                if(!scope.Contains((string)value[0].Value))
                    ThrowNotFoundVariableError(f, (string)value[0].Value);

                scope.AssignOrDefine((string)value[0].Value, new Variable(Console.ReadLine(), typeof(string)));

                return null;
            } },
            {"str.append", (Scope scope, Variable[] value) => {
                const string f = "str.append";
                if(value.Length != 2)
                    ThrowArgumentError(f, 2, value.Length);

                if(value[0].T != typeof(string))
                    ThrowTypeError(f, typeof(string), value[0].T);

                if(!scope.Contains((string)value[0].Value))
                    ThrowNotFoundVariableError(f, (string)value[0].Value);

                var a = scope.Get((string)value[0].Value)!.Value as string;

                scope.AssignOrDefine(value[0].Value as string, new Variable(a + value[1].Value, typeof(string)));
                return null;
            } },
            {"str.prepend", (Scope scope, Variable[] value) => {
                const string f = "str.prepend";
                if(value.Length != 2)
                    ThrowArgumentError(f, 2, value.Length);

                if(value[0].T != typeof(string))
                    ThrowTypeError(f, typeof(string), value[0].T);

                if(!scope.Contains((string)value[0].Value))
                    ThrowNotFoundVariableError(f, (string)value[0].Value);

                scope.AssignOrDefine(((string)value[0].Value), new Variable(scope.Get((value[1].Value as string) + value[0].Value as string)!, typeof(string)));
                
                return null;
            } },
            {"str.substr", (Scope scope, Variable[] value) => {
                const string f = "str.substr";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((string)value[0].Value).Substring((int)value[1].Value), typeof(string));
                } else if (value.Length == 3)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    if(value[2].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[2].T);

                    return new Variable(((string)value[0].Value).Substring((int)value[1].Value, (int)value[2].Value), typeof(string));
                } else
                {
                    ThrowArgumentError(f, new int[2] {2,3}, value.Length);
                }

                return null;
            } },
            {"str.tolower", (Scope scople, Variable[] value) => {
                const string f = "str.tolower";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    return new Variable(((string)value[0].Value).ToLower(), typeof(string));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"str.length", (Scope scople, Variable[] value) => {
                const string f = "str.length";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    return new Variable(((string)value[0].Value).Length, typeof(int));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"str.char_at", (Scope scope, Variable[] value) => {
                const string f = "str.char_at";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    string arg1 = (string)value[0].Value;
                    int arg2 = (int)value[1].Value;

                    return new Variable(arg1[arg2], typeof(char));
                } else
                {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"str.index_of", (Scope scope, Variable[] value) => {
                const string f = "str.index_of";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[1].T);

                    string arg1 = (string)value[0].Value;
                    char arg2 = (char)value[1].Value;

                    return new Variable(arg1.IndexOf(arg2), typeof(int));
                } else
                {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"str.nth_index", (Scope scope, Variable[] value) => {
                const string f = "str.nth_index";
                if(value.Length == 3)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[1].T);

                    if(value[2].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[2].T);

                    string arg1 = (string)value[0].Value;
                    char arg2 = (char)value[1].Value;
                    int arg3 = (int)value[2].Value;

                    return new Variable(arg1.FindNthIndex(x => x == arg2, arg3), typeof(int));
                } else
                {
                    ThrowArgumentError(f, 3, value.Length);
                }

                return null;
            } },
            {"str.nth_index_from_last", (Scope scope, Variable[] value) => {
                const string f = "str.nth_index_from_last";
                if(value.Length == 3)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[1].T);

                    if(value[2].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[2].T);

                    string arg1 = (string)value[0].Value;
                    char arg2 = (char)value[1].Value;
                    int arg3 = (int)value[2].Value;

                    return new Variable(arg1.FindNthIndexFromLast(x => x == arg2, arg3), typeof(int));
                } else
                {
                    ThrowArgumentError(f, 3, value.Length);
                }

                return null;
            } },
            {"str.replace", (Scope scope, Variable[] value) => {
                const string f = "str.replace";
                if(value.Length == 3)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(string) && value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[1].T);

                    if(value[2].T != typeof(string) && value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[2].T);

                    string arg1 = (string)value[0].Value;
                    string arg2 = value[1].T == typeof(string) ? ((string)value[1].Value) : ((char)value[1].Value + "");
                    string arg3 = value[2].T == typeof(string) ? ((string)value[2].Value) : ((char)value[2].Value + "");

                    return new Variable(arg1.Replace(arg2, arg3), typeof(string));
                } else
                {
                    ThrowArgumentError(f, 3, value.Length);
                }

                return null;
            } },
            {"str.split", (Scope scope, Variable[] value) => {
                const string f = "str.split";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(string) && value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[1].T);

                    string arg1 = (string)value[0].Value;
                    string arg2 = value[1].T == typeof(string) ? ((string)value[1].Value) : ((char)value[1].Value + "");

                    return new Variable(arg1.Split(arg2).ToList(), typeof(List<>));
                } else
                {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"str.trim", (Scope scope, Variable[] value) => {
                const string f = "str.trim";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[1].T);

                    string arg1 = (string)value[0].Value;
                    char arg2 =   (char)value[1].Value;

                    return new Variable(arg1.Trim(arg2), typeof(string));
                } else if(value.Length == 1) {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    string arg1 = (string)value[0].Value;

                    return new Variable(arg1.Trim(), typeof(string));
                }
                else
                {
                    ThrowArgumentError(f, new int[2] {1,2}, value.Length);
                }

                return null;
            } },
            {"str.contains", (Scope scope, Variable[] value) => {
                const string f = "str.contains";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(string) && value[1].T != typeof(char))
                        ThrowTypeError(f, typeof(char), value[1].T);

                    string arg1 = (string)value[0].Value;
                    string arg2 = value[1].T == typeof(string) ? ((string)value[1].Value) : ((char)value[1].Value + "");

                    return new Variable(arg1.Contains(arg2), typeof(bool));
                } else
                {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.subtract", (Scope scope, Variable[] value) => {
                const string f = "int.subtract";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) - ((int)value[1].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.add", (Scope scope, Variable[] value) => {
                const string f = "int.add";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) + ((int)value[1].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.pow", (Scope scope, Variable[] value) => {
                const string f = "int.pow";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) ^ ((int)value[1].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.min", (Scope scope, Variable[] value) => {
                const string f = "int.min";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(int.Min((int)value[0].Value, (int)value[1].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.max", (Scope scope, Variable[] value) => {
                const string f = "int.max";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(int.Max((int)value[0].Value, (int)value[1].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.abs", (Scope scope, Variable[] value) => {
                const string f = "int.abs";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    return new Variable(int.Abs((int)value[0].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"int.round", (Scope scope, Variable[] value) => {
                const string f = "int.round";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    return new Variable((int)Math.Round((double)(int)value[0].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"int.floor", (Scope scope, Variable[] value) => {
                const string f = "int.floor";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    return new Variable((int)Math.Floor((double)(int)value[0].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"int.ceil", (Scope scope, Variable[] value) => {
                const string f = "int.ceil";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    return new Variable((int)Math.Ceiling((double)(int)value[0].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"int.sqrt", (Scope scope, Variable[] value) => {
                const string f = "int.sqrt";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    return new Variable((int)Math.Sqrt((double)(int)value[0].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"int.random", (Scope scope, Variable[] value) => {
                const string f = "int.random";
                if(value.Length == 0)
                {
                    return new Variable(Random.Shared.Next(), typeof(int));
                } else if(value.Length == 2) {

                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    int arg1 = (int)value[0].Value;
                    int arg2 = (int)value[1].Value;

                    return new Variable(Random.Shared.Next(arg1, arg2), typeof(int));
                } else {
                    ThrowArgumentError(f, new int[2] {0,2}, value.Length);
                }

                return null;
            } },
            {"int.mod", (Scope scope, Variable[] value) => {
                const string f = "int.mod";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) % ((int)value[1].Value), typeof(int));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.bigger", (Scope scope, Variable[] value) => {
                const string f = "int.bigger";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) > ((int)value[1].Value), typeof(bool));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.smaller", (Scope scope, Variable[] value) => {
                const string f = "int.smaller";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) < ((int)value[1].Value), typeof(bool));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.eqauls", (Scope scope, Variable[] value) => {
                const string f = "int.equals";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) == ((int)value[1].Value), typeof(bool));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.biggerorequal", (Scope scope, Variable[] value) => {
                const string f = "int.biggerorequal";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) >= ((int)value[1].Value), typeof(bool));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"int.smallerorequal", (Scope scope, Variable[] value) => {
                const string f = "int.smallerorequal";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    return new Variable(((int)value[0].Value) <= ((int)value[1].Value), typeof(bool));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"notequals", (Scope scople, Variable[] value) => {
                const string f = "notequals";
                if(value.Length == 2)
                {
                    if(value[0].Value == null)
                        ThrowNullError(f);

                    if(value[1].Value == null)
                        ThrowNullError(f);

                    return new Variable(!Object.Equals(value[0].Value, value[1].Value), typeof(bool));
                } else {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"not", (Scope scople, Variable[] value) => {
                const string f = "not";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(bool))
                        ThrowTypeError(f, typeof(bool), value[0].T);

                    return new Variable(!((bool)value[0].Value), typeof(bool));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"str.toupper", (Scope scople, Variable[] value) => {
                const string f = "str.toupper";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    return new Variable(((string)value[0].Value).ToUpper(), typeof(string));
                } else {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            { "arr.get", (Scope scope, Variable[] value) => {
                const string f = "arr.get";
                if (value.Length != 2)
                    ThrowArgumentError(f, 2, value.Length);

                if (value[0].T != typeof(string))
                    ThrowTypeError(f, typeof(string), value[0].T);

                if (value[1].T != typeof(int))
                    ThrowTypeError(f, typeof(int), value[1].T);

                string varName = (string)value[0].Value;
                int index = (int)value[1].Value;

                if (!scope.Contains(varName))
                    ThrowNotFoundVariableError(f, varName);

                Variable variable = scope.Get(varName)!;

               var list = variable.Value as IList;
                if (list == null)
                    ThrowError(f, $"{f}: variable '{varName}' is not a list.");

                if (index < 0 || index >= list.Count)
                    ThrowError(f, $"{f}: index {index} is out of bounds (0..{list.Count - 1}).");

                Type elementType = variable.T.IsGenericType
                    ? variable.T.GetGenericArguments()[0]
                    : typeof(object); // fallback

                return new Variable(list[index], elementType);
            }},
            {"arr.set", (Scope scope, Variable[] value) => {
                const string f = "arr.set";
                if(value.Length == 3)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    string arg1 = (string)value[0].Value;
                    int arg2 = (int)value[1].Value;
                    object arg3 = value[2].Value;

                    if(!scope.Contains(arg1))
                        ThrowNotFoundVariableError(f, arg1);

                    if(arg2 < 0)
                        ThrowError(f, "arg2 must be positive or netural");

                    Variable variable = scope.Get(arg1)!;

                    if(!IsListVariable(variable))
                        ThrowError(f, "Variable arg1 is not a list!");

                    IList list = (IList)variable.Value;

                    Type type = GetElementTypeFromListVariable(f, variable);

                    list[arg2] = Convert.ChangeType(arg3, type);

                } else
                {
                    ThrowArgumentError(f, 3, value.Length);
                }

                return null;
            } },
            {"arr.remove", (Scope scope, Variable[] value) => {
                const string f = "arr.remove";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(value[1].T != typeof(int))
                        ThrowTypeError(f, typeof(int), value[1].T);

                    string arg1 = (string)value[0].Value;
                    int arg2 = (int)value[1].Value;

                    if(!scope.Contains(arg1))
                        ThrowNotFoundVariableError(f, arg1);

                    if(arg2 < 0)
                        ThrowError(f, "arg2 must be positive or netural");

                    Variable variable = scope.Get(arg1)!;

                    if(!IsListVariable(variable))
                        ThrowError(f, "Variable arg1 is not a list!");

                    IList list = (IList)variable.Value;

                    list.RemoveAt(arg2);

                } else
                {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"arr.removelement", (Scope scope, Variable[] value) => {
                const string f = "arr.removelement";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    string arg1 = (string)value[0].Value;

                    if(!scope.Contains(arg1))
                        ThrowNotFoundVariableError(f, arg1);

                    Variable variable = scope.Get(arg1)!;

                    if(!IsListVariable(variable))
                        ThrowError(f, "Variable arg1 is not a list!");

                    Type type = GetElementTypeFromListVariable(f, variable);

                    if(value[1].T != type)
                        ThrowTypeError(f, type, value[1].T);

                    IList list = (IList)variable.Value;

                    object arg2 = Convert.ChangeType(value[1].Value, type);

                    list.Remove(arg2);

                } else
                {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"arr.push", (Scope scope, Variable[] value) => {
                const string f = "arr.push";
                if(value.Length == 2)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    string arg1 = (string)value[0].Value;

                    if(!scope.Contains(arg1))
                        ThrowNotFoundVariableError(f, arg1);

                    Variable variable = scope.Get(arg1)!;

                    if(!IsListVariable(variable))
                        ThrowError(f, "Variable arg1 is not a list!");

                    Type type = GetElementTypeFromListVariable(f, variable);

                    if(value[1].T != type)
                        ThrowTypeError(f, type, value[1].T);

                    IList list = (IList)variable.Value;

                    object arg2 = Convert.ChangeType(value[1].Value, type);

                    list.Add(arg2);

                } else
                {
                    ThrowArgumentError(f, 2, value.Length);
                }

                return null;
            } },
            {"arr.pop", (Scope scope, Variable[] value) => {
                const string f = "arr.pop";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    string arg1 = (string)value[0].Value;

                    if(!scope.Contains(arg1))
                        ThrowNotFoundVariableError(f, arg1);

                    Variable variable = scope.Get(arg1)!;

                    if(!IsListVariable(variable))
                        ThrowError(f, "Variable arg1 is not a list!");

                    Type type = GetElementTypeFromListVariable(f, variable);

                    IList list = (IList)variable.Value;

                    if(list.Count <= 0)
                        return null;

                    object last = Convert.ChangeType(list[list.Count - 1], type)!;

                    list.RemoveAt(list.Count - 1);

                    return new Variable(last, type);
                } else
                {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"arr.first", (Scope scope, Variable[] value) => {
                const string f = "arr.first";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    string arg1 = (string)value[0].Value;

                    if(!scope.Contains(arg1))
                        ThrowNotFoundVariableError(f, arg1);

                    Variable variable = scope.Get(arg1)!;

                    if(!IsListVariable(variable))
                        ThrowError(f, "Variable arg1 is not a list!");

                    Type type = GetElementTypeFromListVariable(f, variable);

                    IList list = (IList)variable.Value;

                    if(list.Count <= 0)
                        return null;

                    object first = Convert.ChangeType(list[0], type)!;

                    return new Variable(first, type);
                } else
                {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            {"arr.last", (Scope scope, Variable[] value) => {
                const string f = "arr.last";
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    string arg1 = (string)value[0].Value;

                    if(!scope.Contains(arg1))
                        ThrowNotFoundVariableError(f, arg1);

                    Variable variable = scope.Get(arg1)!;

                    if(!IsListVariable(variable))
                        ThrowError(f, "Variable arg1 is not a list!");

                    Type type = GetElementTypeFromListVariable(f, variable);

                    IList list = (IList)variable.Value;

                    if(list.Count <= 0)
                        return null;

                    object last = Convert.ChangeType(list[list.Count - 1], type)!;

                    return new Variable(last, type);
                } else
                {
                    ThrowArgumentError(f, 1, value.Length);
                }

                return null;
            } },
            { "arr.length", (Scope scope, Variable[] value) => {
                const string f = "arr.length";
                if (value.Length != 1)
                    ThrowArgumentError(f, 1, value.Length);

                if(!IsListVariable(value[0]))
                    ThrowTypeError(f, typeof(List<>), value[0].T);

                IList arg1 = (IList)value[0].Value;

                return new Variable(arg1.Count, typeof(int));
            }},
            {"del", (Scope scope, Variable[] value) => {
                const string f = "del";

                if(value.Length != 1)
                    ThrowArgumentError(f, 1, value.Length);

                if(value[0].T != typeof(string))
                    ThrowTypeError(f, typeof(string), value[0].T);

                scope.Remove((string)value[0].Value);

                return null;
            } },
            {"equals", (Scope scope, Variable[] value) => {
                const string f = "equals";
                if(value.Length != 2)
                    ThrowArgumentError(f, 2, value.Length);

                return new Variable(object.Equals(value[0].Value, value[1].Value), typeof(bool));
            } },

            {"sleep", (Scope scope, Variable[] value) => {
                const string f = "sleep";
                if(value.Length != 1)
                    ThrowArgumentError(f, 1, value.Length);

                if(value[0].T != typeof(int))
                    ThrowTypeError(f, typeof(int), value[0].T);

                System.Threading.Thread.Sleep((int)value[0].Value * 1000);

                return null;
            } },
            {"sleepms", (Scope scope, Variable[] value) => {
                const string f = "sleepms";
                if(value.Length != 1)
                    ThrowArgumentError(f, 1, value.Length);

                if(value[0].T != typeof(int))
                    ThrowTypeError(f, typeof(int), value[0].T);

                System.Threading.Thread.Sleep((int)value[0].Value);

                return null;
            } },
            {"break", (Scope scope, Variable[] value) => {
                StandardCom = "BreakOutOfLoop";
                return null;
            } },
            {"async", (Scope scope, Variable[] value) => {
                const string f = "async";
                if(value.Length >= 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError(f, typeof(string), value[0].T);

                    if(!userdefinedFunctions.ContainsKey((string)value[0].Value))
                        ThrowError(f, "Function '"+value[0].Value+"' not found");

                    UserDefinedFunction function = userdefinedFunctions[(string)value[0].Value];

                    if(value.Length != (1 + function.ArgNameList.Length))
                        ThrowArgumentError(f, (1 + function.ArgNameList.Length), value.Length);

                    Variable[] args = value[1..(value.Length)];

                    new Thread(() =>
                    {
                        RunUserDefinedFunction(function, -1, args, scope);
                    }).Start();

                    return null;
                    
                } else
                {
                    ThrowError(f, "Need more than 1 Argument!");
                }
                return null;
            } },
            {"typeof", (Scope scope, Variable[] value) => {
                if(value.Length == 1)
                {
                    if(value[0].T != typeof(string))
                        ThrowTypeError("typeof", typeof(string), value[0].T);

                    if(!scope.Contains((string)value[0].Value))
                        ThrowNotFoundVariableError("typeof", "arg1");

                    Variable variable = scope.Get((string)value[0].Value);

                    if(variable == null)
                        ThrowNullError("typeof", "arg1");

                    return new Variable(variable!.T.Name, typeof(string));
                } else
                {
                    ThrowArgumentError("typeof", 1, value.Length);
                }
                return null;
            } },
        };
        public static Variable ParseNestedLists(string line, int depth, Scope scope)
        {
            // 1) Remove outer [ ] once
            string inner = line.Substring(1, line.Length - 2);
            string[] items = IEnumerableExtensions.SplitTopLevelCommas(inner, ',', '[', ']').ToArray();

            Type commonInnerType = null;
            object[] rawValues = new object[items.Length];

            // 2) Determine commonInnerType **without** unwrapping values
            for (int i = 0; i < items.Length; i++)
            {
                Variable v = ExecuteLine(items[i], depth + 1, scope);
                Type t = v.T;  // e.g. List<String> for "[a,b]"

                if (commonInnerType == null)
                    commonInnerType = t;
                else if (commonInnerType != t)
                    throw new InvalidOperationException("Mixed inner types are not supported yet.");

                rawValues[i] = v.Value;  // v.Value is List<string> here
            }

            // 3) Build List<commonInnerType>
            Type listType = typeof(List<>).MakeGenericType(commonInnerType);
            var typedList = (IList)Activator.CreateInstance(listType);
            foreach (object val in rawValues)
                typedList.Add(val);

            return new Variable(typedList, listType);
        }
        private static object ParseForArrayGets(string line, Scope scope)
        {
            line = line.Substring(1);

            string Name = line.Split('[')[0];

            if (line.Count(x => x == '[') != line.Count(x => x == ']'))
            {
                ThrowError("array-parser", "Missing [ or ] on array access");
            }

            if(!scope.Contains(Name))
            {
                ThrowError("array-parser", "Variable not found!");
            }

            object current = scope.Get(Name).Value;

            bool Inside = false;
            string InsideText = "";
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == ']')
                {
                    if (!Inside)
                        ThrowError("array-parser", "Closed ] without openning any [");
                    Inside = false;

                    int idx = Convert.ToInt32(InsideText);

                    current = ((IList)current)[idx];
                }

                if (Inside)
                {
                    InsideText += line[i];
                }

                if (line[i] == '[')
                {
                    if (Inside)
                        ThrowError("array-parser", "[ inside of a [ please remove the extra [");
                    Inside = true;
                }
            }

            return current;
        }
        private static LineFunction GetLineFunction(string line, int depth, Scope scope)
        {
            // (name?_args)(&)
            // (test?_(str.substr?_var,0,6)(&))(=)
            LineFunction lineFunction = new LineFunction();

            string[] split = line.Split("?_");

            lineFunction.Name = split[0].Substring(1);

            var a = String.Join("?_", split[1..(split.Length)]);

            var c = a.FindNthIndexFromLast(x => x == ')', 2);

            lineFunction.ArgStr = a.Substring(0, c);

            lineFunction.Args = IEnumerableExtensions.SplitTopLevelCommas(lineFunction.ArgStr, ',', '(', ')').Select(x => ExecuteLine(x, depth + 1, scope)).ToArray();

            lineFunction.Denom = line[line.Length-2];

            return lineFunction;
        }
        static object H(Variable variable)
        {
            if (variable.Value is Variable)
            {
                return H(variable.Value as Variable);
            }
            else
            {
                return variable.Value;
            }
        }
        private static string StandardCom = "";
        private static UserDefinedFunction ParseFunction(string _firstLine, Func<string?> getline)
        {
            // (myfunction?_arg1,arg2)(f)
            // myfunction arg1 arg2 { <-
            // (println?_arg1,arg2)(&)
            // } <-
            UserDefinedFunction function = new UserDefinedFunction();
            string firstLine = _firstLine.Split('\n').First().TrimEnd();
            function.Name = firstLine.Split(' ')[0];
            function.ArgCount = firstLine.Split(' ').Length - 2;
            function.ArgNameList = firstLine.Split(' ')[1..(firstLine.Split(' ').Length - 1)];

            if(!userdefinedFunctions.ContainsKey(function.Name) && function.Name[0] != '%' && function.Name != "if" && function.Name != "while")
            {
                ThrowError("function-parser", "Please pre-define your function before defining it!");
            }

            if (firstLine.Last() != '{')
            {
                ThrowError("function-parser", "Expected '{' got '" + firstLine.Last() + "'");
            }

            int i = 0;
            int o = 0;

            StringBuilder stringBuilder = new StringBuilder();

            string? current_line = getline();

            if (current_line == null)
            {
                ThrowError("function-parser", "Function is not defined yet!");
            }

            current_line = current_line.TrimStart('\t');

            while (true) {
                if(current_line.Length == i || current_line == "")
                {
                    current_line = getline();

                    i = 0;

                    if(current_line == null)
                    {
                        ThrowError("function-parser", "Function is not defined yet!");
                    }


                    stringBuilder.Append('\n');

                    current_line = current_line.TrimStart('\t');

                    continue;
                }

                if (current_line[i] == '{')
                {
                    o++;
                } else if (current_line[i] == '}')
                {
                    o--;
                }

                if(o == -1)
                {
                    break;
                }

                stringBuilder.Append(current_line[i]);

                i++;
            }

            function.FunctionValue = stringBuilder.ToString();

            return function;
        }
        private static Func<string> global_getline;
        private static Variable? RunUserDefinedFunction(UserDefinedFunction userDefinedFunction, int depth, Variable[] args, Scope scope)
        {
            if (userDefinedFunction.FunctionValue == null)
            {
                ThrowError("function-runner", "Function '" + userDefinedFunction.Name + "' is not defined yet!");
            }

            string[] lines = userDefinedFunction.FunctionValue.Split('\n');
            Variable? returnValue = null;

            Scope variableScope = new(scope);

            int x = 0;
            foreach (var item in args)
            {
                variableScope.Set(userDefinedFunction.ArgNameList[x], item);
                x++;
            }

            for (int w = 0; w < lines.Length - 1; w++)
            {
                ExecuteLine(lines[w], depth + 1, variableScope); // !!

                int z = 0;
                UseStandardCom(lines[w], () => { z++; if (z + w >= lines.Length) { return null; } else { return lines[z + w];  } }, depth + 1 ,scope);
                w += z;
            }
            returnValue = ExecuteLine(lines.Last(), depth + 1, scope);

            return returnValue;
        }
        private static Dictionary<string, Type> TypeConvertList = new()
        {
            {"string", typeof(string) },
            {"int", typeof(int) },
            {"array", typeof(IList) },
            {"object", typeof(object) },
            {"char", typeof(char) },
            {"bool", typeof(bool) }
        };
        private static Dictionary<string, object> TypeDefaultList = new()
        {
            {"string", "" },
            {"int", 0 },
            {"array", new List<object>() { } },
            {"object", null },
            {"char", 'a' },
            {"bool", false },
        };
        public static string Unescape(string input)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    i++;
                    sb.Append(input[i] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        ',' => ',',  // For your case: escaped separator
                        '"' => '"',
                        '\'' => '\'',
                        _ => input[i] // Unknown escape? keep it as-is
                    });
                }
                else
                {
                    sb.Append(input[i]);
                }
            }

            return sb.ToString();
        }
        private static void IncludeLibrary(string libName, Scope scope)
        {
            string libPath = libName + ".dll";
            if (!File.Exists(libPath))
            {
                ThrowError("lib-include", libPath + " not found!");
            }

            Assembly assembly = Assembly.LoadFrom(libPath);
            Type type = assembly.GetType($"{libName}");
            var method = type.GetMethod("Run");
            Dictionary<string, Func<Scope, Variable[], Variable?>> returnValue = (Dictionary<string, Func<Scope, Variable[], Variable?>>)method.Invoke(null, new object[] { scope });

            foreach (var item in returnValue)
            {
                baselineFunctions.Add(item.Key, item.Value);
            }

            var method2 = type.GetMethod("RunTypes");
            var method3 = type.GetMethod("RunTypesDefault");

            if (method2 != null && method3 != null)
            {
                Dictionary<string, Type> types = (Dictionary<string, Type>)method2.Invoke(null, new object[] { });
                foreach (var item in types)
                {
                    TypeConvertList.Add(item.Key, item.Value);
                }

                Dictionary<string, object> typeDefaults = (Dictionary<string, object>)method3.Invoke(null, new object[] { });

                foreach (var item in typeDefaults)
                {
                    TypeDefaultList.Add(item.Key, item.Value);
                }
            }
        }
        private static Variable? ExecuteLine(string line, int depth, Scope scope)
        {
            if (line.Length <= 0) return null;

            if(depth > 40)
            {
                ThrowError("main-line", "Depth max limit of 40 reached! Please shorten your lines.");
            }

            if (line[0] != '(') {
                if (line[0] == '&')
                {
                    if(line.Contains('[') || line.Contains(']'))
                    {
                        var parse = ParseForArrayGets(line, scope);
                        return new Variable(parse, parse.GetType());
                    } else { 
                        if (!scope.Contains(line.Substring(1)))
                        {
                            ThrowError("in-line-error", "Variable provided '" + line + "' does not exist yet!");
                        }

                        return scope.Get(line.Substring(1));
                    }
                }
                else if (line[0] == '[')
                {
                    return ParseNestedLists(line, depth + 1, scope);
                } else if(line.Last() == '{')
                {
                    StandardCom = "ParseFunctionAfterLine";
                    return null;
                } else if (line.StartsWith("###"))
                {
                    IncludeLibrary(line.Substring(3), scope);
                    return null;
                } else if (line.ToCharArray().All(x => char.IsDigit(x)))
                {
                    return new Variable(Convert.ToInt32(line), typeof(int));
                } else if(line == "true" || line == "false")
                {
                    return new Variable(Convert.ToBoolean(line), typeof(bool));
                } else if (line[0] == '|' && line.Last() == '{')
                {
                    StandardCom = "ParseFunctionAfterLine";
                    return null;
                }
                else {
                    return new Variable(Unescape(line), typeof(string));
                }
            }

            LineFunction lineFunction = GetLineFunction(line, depth + 1, scope);

            if(lineFunction.Denom == '&') { 

                if(baselineFunctions.ContainsKey(lineFunction.Name))
                {
                    return baselineFunctions[lineFunction.Name](scope, lineFunction.Args); // !!
                }

                if (userdefinedFunctions.ContainsKey(lineFunction.Name))
                {
                    UserDefinedFunction userDefinedFunction = userdefinedFunctions[lineFunction.Name];

                    Variable? returnValue = RunUserDefinedFunction(userDefinedFunction, depth + 1, lineFunction.Args, scope);

                    return returnValue;
                }

                ThrowError("main", "Function not found with name '" + lineFunction.Name + "'");

            } else if (lineFunction.Denom == '*')
            {;
                if(String.IsNullOrEmpty(lineFunction.ArgStr))
                    scope.AssignOrDefine(lineFunction.Name, new Variable("", typeof (string) ));
                else {
                    string name = lineFunction.Name;
                    object a = TypeDefaultList[lineFunction.ArgStr];
                    Type b = TypeConvertList[lineFunction.ArgStr];
                    Variable c = new(a, b);
                    scope.AssignOrDefine(name, c);
                }
                return null;
            } else if(lineFunction.Denom == '=')
            {
                scope.AssignOrDefine(lineFunction.Name, ExecuteLine(lineFunction.ArgStr, depth + 1, scope) ?? new Variable(null, null));
                return null;
            } else if (lineFunction.Denom == 'f')
            {
                userdefinedFunctions.Add(lineFunction.Name, new UserDefinedFunction() { Name = lineFunction.Name });
                return null;
            } else if(lineFunction.Denom == 'T')
            {
                Variable variable = scope.Get(lineFunction.Name);

                if(variable == null)
                {
                    ThrowError("t-parser", "Variable '" + lineFunction.Name + "' doesn't exist yet!");
                }

                Type type = TypeConvertList[lineFunction.ArgStr];

                scope.Set(lineFunction.Name, new Variable(variable.Value, type));

                return null;
            } else if (lineFunction.Denom == 't')
            {
                Variable variable = scope.Get(lineFunction.Name);

                if (variable == null)
                {
                    ThrowError("t-parser", "Variable '" + lineFunction.Name + "' doesn't exist yet!");
                }

                Type type = TypeConvertList[lineFunction.ArgStr];

                if (type == typeof(IList))
                    scope.Set(lineFunction.Name, new Variable((IList)variable.Value, type));
                else
                    scope.Set(lineFunction.Name, new Variable(Convert.ChangeType(variable.Value, type), type));

                return null;
            }
            else if (lineFunction.Denom == '%')
            {
                Type type = TypeConvertList[lineFunction.ArgStr];
                
                return new Variable(Convert.ChangeType(lineFunction.Name, type), type);
            }

            return null;
        }
        private static void UseStandardCom(string line, Func<string> get_line, int depth, Scope mainScope)
        {
            if (StandardCom == "ParseFunctionAfterLine")
            {
                StandardCom = "";

                UserDefinedFunction func = ParseFunction(line, get_line);
                userdefinedFunctions[func.Name] = func;

                if (func.Name[0] == '%')
                {
                    if (func.Name.Contains(".."))
                    {
                        Variable startVar = ExecuteLine(func.Name.Substring(1).Split("..")[0], depth, mainScope);
                        if(startVar == null || startVar.T != typeof(int))
                        {
                            if (startVar != null)
                                ThrowTypeError("stc-loop-parser", typeof(int), startVar.T);
                            else
                                ThrowNullError("stc-loop-parser");
                        }

                        Variable endVar = ExecuteLine(func.Name.Substring(1).Split("..")[1], depth, mainScope);
                        if (endVar == null || endVar.T != typeof(int))
                        {
                            if (endVar != null)
                                ThrowTypeError("stc-loop-parser", typeof(int), endVar.T);
                            else
                                ThrowNullError("stc-loop-parser");
                        }
                        int start = (int)startVar!.Value;
                        int end = (int)endVar!.Value;
                        for (int i = start; i < end; i++)
                        {
                            Variable[] _args = { new Variable(i + "", typeof(string)) };
                            RunUserDefinedFunction(func, 0, _args, mainScope);

                            if (StandardCom == "BreakOutOfLoop")
                            {
                                StandardCom = "";
                                break;
                            }
                        }
                    } else
                    {
                        for (int i = 0; i < Convert.ToInt32(func.Name.Substring(1)); i++)
                        {
                            Variable[] _args = { new Variable(i + "", typeof(string)) };
                            RunUserDefinedFunction(func, 0, _args, mainScope);

                            if (StandardCom == "BreakOutOfLoop")
                            {
                                StandardCom = "";
                                break;
                            }
                        }
                    }

                    userdefinedFunctions.Remove(func.Name);
                }

                if (func.Name.StartsWith("if"))
                {
                    // if (...) {
                    Variable? returnValue = ExecuteLine(String.Join(" ", func.ArgNameList[0..(func.ArgNameList.Length)]), 0, mainScope);

                    if (returnValue.T == typeof(bool))
                    {
                        if ((bool)returnValue.Value == true)
                        {
                            RunUserDefinedFunction(func, 0, new Variable[0], mainScope);
                        }
                    }
                    else if (returnValue != null)
                    {
                        RunUserDefinedFunction(func, 0, new Variable[0], mainScope);
                    }

                    userdefinedFunctions.Remove(func.Name);
                }

                if (func.Name.StartsWith("while"))
                {
                    // while (...) {
                    
                    while (true) {
                        Variable? returnValue = ExecuteLine(String.Join(" ", func.ArgNameList[0..(func.ArgNameList.Length)]), 0, mainScope);

                        if (returnValue.T == typeof(bool))
                        {
                            if((bool)returnValue.Value == false)
                            {
                                break;
                            }
                        }
                        else if (returnValue == null)
                        {
                            break;
                        }

                        RunUserDefinedFunction(func, 0, new Variable[0], mainScope);
                    }

                    userdefinedFunctions.Remove(func.Name);
                }
            }
        }
        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.White;

            Console.WriteLine("[Init]");

            Scope mainScope = new();

            Console.WriteLine("[InitCompleted]");

            Console.Write("\r\nFile to run: ");

            string runningFilePath = Console.ReadLine();

            if(String.IsNullOrEmpty(runningFilePath) || String.IsNullOrWhiteSpace(runningFilePath))
            {
                Console.WriteLine("Invalid path!");
                Environment.Exit(0);
            }

            if(!File.Exists(runningFilePath))
            {
                Console.WriteLine("Invalid path!");
                Environment.Exit(0);
            }

            StreamReader reader = new StreamReader(runningFilePath);

            global_getline = () => { return reader.ReadLine(); };

            string? line;

            while ((line = reader.ReadLine()) != null)
            {   
                ExecuteLine(line, -1, mainScope);

                UseStandardCom(line, global_getline, -1, mainScope);
            }
        }
    }
}
