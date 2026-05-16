namespace MailWhere.OutlookCom;

public sealed class OutlookSourceLookupException : Exception
{
    public OutlookSourceLookupException(string reason)
        : base(reason)
    {
    }

    public OutlookSourceLookupException(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }
}
