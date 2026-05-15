using System.Runtime.InteropServices;

namespace MailWhere.OutlookCom;

internal static class ComRelease
{
    public static void FinalRelease(object? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(candidate))
            {
                Marshal.FinalReleaseComObject(candidate);
            }
        }
        catch
        {
            // Releasing a Runtime Callable Wrapper is best-effort cleanup.
        }
    }
}
