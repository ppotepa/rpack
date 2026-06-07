using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Rpack.Open;

internal static class Program
{
    private const int VkShift = 0x10;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            var options = OpenOptions.Parse(args);
            var request = OpenRequest.FromOptions(options, IsShiftPressed());
            if (request.PackagePaths.Count == 0)
            {
                MessageBox.Show(
                    "No .rpack package path was provided.",
                    "rpack open",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }

            using var singleInstance = SingleInstanceCoordinator.Create();
            if (!singleInstance.IsPrimary)
            {
                return singleInstance.SendToPrimary(request) ? 0 : 1;
            }

            IDisposable? server = null;
            using var form = new OpenBatchForm();
            form.Shown += (_, _) => server ??= singleInstance.StartServer(form.AddRequest);
            form.AddRequest(request);
            try
            {
                Application.Run(form);
            }
            finally
            {
                server?.Dispose();
            }

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ErrorFormatter.SummarizeException(ex).Details,
                "rpack open failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool IsShiftPressed()
    {
        return (GetKeyState(VkShift) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
