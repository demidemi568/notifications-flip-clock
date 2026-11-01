namespace FlipClock;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Only one clock at a time.
        using var mutex = new Mutex(true, @"Local\FlipClock.SingleInstance", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
