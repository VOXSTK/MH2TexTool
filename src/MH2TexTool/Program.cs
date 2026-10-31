using System.Runtime.InteropServices;
using System.Text;
using MH2TexTool.Core;

namespace MH2TexTool;

static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--"))
        {
            AttachConsole(-1);

            string logPath = Environment.GetEnvironmentVariable("MH2_LOG")
                             ?? Path.Combine(Path.GetTempPath(), "MH2TexTool-cli.log");
            var file = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };

            TextWriter console = TextWriter.Null;
            try
            {
                var stdout = Console.OpenStandardOutput();
                console = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch
            {
                // консоли нет — пишем только в файл
            }

            var tee = new TeeWriter(console, file);
            Console.SetOut(tee);
            Console.SetError(tee);
            try
            {
                int code = Cli.Run(args);
                tee.WriteLine($"exit={code}");
                tee.WriteLine($"log={logPath}");
                return code;
            }
            finally
            {
                tee.Flush();
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        return 0;
    }
}

internal sealed class TeeWriter : TextWriter
{
    private readonly TextWriter _a;
    private readonly TextWriter _b;

    public TeeWriter(TextWriter a, TextWriter b)
    {
        _a = a;
        _b = b;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => Both(w => w.Write(value));

    public override void Write(string? value) => Both(w => w.Write(value));

    public override void WriteLine(string? value) => Both(w => w.WriteLine(value));

    public override void Flush() => Both(w => w.Flush());

    private void Both(Action<TextWriter> action)
    {
        try { action(_a); } catch { /* нет консоли */ }
        action(_b);
    }
}
