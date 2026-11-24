// Поддельный claude для тестов ClaudeCliRunner. Режим задаётся переменной окружения FAKE_CLAUDE_MODE:
//   echo  — вернуть stdin как есть (код 0)
//   tags  — вернуть stdin, обёрнутый в <text>…</text> с пробелами (код 0)
//   fail  — напечатать ошибку в stderr, код 3
//   auth  — "Not logged in · Please run /login", код 1
//   empty — ничего не выводить, код 0
//   hang  — зависнуть
using System.Text;

Console.InputEncoding = Encoding.UTF8;
var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)).ReadToEnd();

switch (Environment.GetEnvironmentVariable("FAKE_CLAUDE_MODE"))
{
    case "tags":
        stdout.Write("\n  <text>\n" + stdin + "\n</text>  \n");
        return 0;
    case "fail":
        Console.Error.WriteLine("Something went wrong");
        return 3;
    case "auth":
        Console.Error.WriteLine("Not logged in · Please run /login");
        return 1;
    case "empty":
        return 0;
    case "hang":
        Thread.Sleep(Timeout.Infinite);
        return 0;
    case "args":
        stdout.Write(string.Join("|", args.Select(a => a.Length == 0 ? "<empty>" : a)));
        return 0;
    default:
        stdout.Write(stdin);
        return 0;
}
