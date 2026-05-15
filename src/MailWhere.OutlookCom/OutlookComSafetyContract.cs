namespace MailWhere.OutlookCom;

public static class OutlookComSafetyContract
{
    public static readonly string[] ForbiddenAutomaticOperations =
    [
        "mailbox-send",
        "mailbox-delete",
        "mailbox-move",
        "mailbox-save",
        "reply-create-or-send",
        "forward-create-or-send",
        "read-state-mutation",
        "flag-or-category-mutation",
        "folder-manipulation",
        "attachment-open-save-execute",
        "inspector-display-side-effect"
    ];
}
