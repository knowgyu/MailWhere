namespace MailWhere.OutlookCom;

internal static class OutlookStaExecutor
{
    public static Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return Task.FromResult(operation());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(operation());
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "MailWhere-COM-STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return DisposeRegistrationAfterCompletionAsync(completion.Task, registration);
    }

    private static async Task<T> DisposeRegistrationAfterCompletionAsync<T>(Task<T> task, CancellationTokenRegistration registration)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }
}
