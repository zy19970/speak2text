using System.Reflection;

namespace Speak2Text;

/// <summary>
/// Adds the CUDA-preferred choice without duplicating MainForm's backend
/// selection logic. MainForm currently maps unknown indices to "auto";
/// the native dispatcher turns that auto request into an explicit CUDA
/// request only while this option is selected.
/// </summary>
internal static class CudaBackendUi
{
    public const string ForceCudaEnvironmentVariable = "SPEAK2TEXT_FORCE_CUDA";
    private const string CudaLabel = "CUDA（NVIDIA）";

    public static bool IsCudaSelected
        => string.Equals(
            Environment.GetEnvironmentVariable(ForceCudaEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    public static void Attach(MainForm form)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var backendField = typeof(MainForm).GetField("_backend", flags);
        var gridField = typeof(MainForm).GetField("_queueGrid", flags);

        if (backendField?.GetValue(form) is not ComboBox backend)
            return;

        if (!backend.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), CudaLabel, StringComparison.Ordinal)))
            backend.Items.Add(CudaLabel);

        void SyncSelection()
        {
            var useCuda = backend.SelectedItem?.ToString() == CudaLabel;
            Environment.SetEnvironmentVariable(
                ForceCudaEnvironmentVariable,
                useCuda ? "1" : null);
        }

        backend.SelectedIndexChanged += (_, _) => SyncSelection();
        SyncSelection();

        // MainForm's result object still sees the original "auto" request.
        // Keep the visible queue honest while CUDA is explicitly selected.
        if (gridField?.GetValue(form) is DataGridView grid)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) =>
            {
                if (!IsCudaSelected || !grid.Columns.Contains("Backend"))
                    return;

                foreach (DataGridViewRow row in grid.Rows)
                {
                    var cell = row.Cells["Backend"];
                    var value = cell.Value?.ToString();
                    if (value is "全局" or "Auto" or "auto")
                        cell.Value = "CUDA";
                }
            };
            timer.Start();
            form.FormClosed += (_, _) => timer.Dispose();
        }

        form.FormClosed += (_, _) =>
            Environment.SetEnvironmentVariable(ForceCudaEnvironmentVariable, null);
    }
}
