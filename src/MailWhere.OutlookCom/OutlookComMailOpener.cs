namespace MailWhere.OutlookCom;

public sealed record OutlookOpenResult(bool Success, string StatusCode, string Message)
{
    public static OutlookOpenResult Opened { get; } = new(true, "opened", "Outlook에서 메일을 열었습니다.");

    public static OutlookOpenResult Failed(string statusCode, string message) => new(false, statusCode, message);
}

public sealed class OutlookComMailOpener
{
    public Task<OutlookOpenResult> OpenAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return Task.FromResult(OutlookOpenResult.Failed("missing-source-id", "이 항목은 원본 메일 연결 정보가 없습니다."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return OutlookStaExecutor.RunAsync(() => OpenOnSta(sourceId, cancellationToken), cancellationToken);
    }

    private static OutlookOpenResult OpenOnSta(string sourceId, CancellationToken cancellationToken)
    {
        object? outlook = null;
        object? session = null;
        object? item = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
            if (outlookType is null)
            {
                return OutlookOpenResult.Failed("outlook-progid-unavailable", "Outlook COM을 사용할 수 없습니다.");
            }

            outlook = Activator.CreateInstance(outlookType);
            if (outlook is null)
            {
                return OutlookOpenResult.Failed("outlook-com-null", "Outlook 실행 객체를 만들 수 없습니다.");
            }

            dynamic outlookDynamic = outlook;
            session = outlookDynamic.Session;
            dynamic sessionDynamic = session;
            item = sessionDynamic.GetItemFromID(sourceId);
            if (item is null)
            {
                return OutlookOpenResult.Failed("outlook-item-not-found", "Outlook에서 원본 메일을 찾지 못했습니다.");
            }

            dynamic itemDynamic = item;
            itemDynamic.Display(false);
            return OutlookOpenResult.Opened;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OutlookOpenResult.Failed("outlook-open-failed", ex.GetType().Name);
        }
        finally
        {
            ComRelease.FinalRelease(item);
            ComRelease.FinalRelease(session);
            ComRelease.FinalRelease(outlook);
        }
    }
}
