using OutlookAiSecretary.Core.Capabilities;

namespace OutlookAiSecretary.OutlookCom;

public sealed class OutlookComCapabilityProbe
{
    public CapabilityReport Run(bool includeBodyProbe)
    {
        var results = new List<CapabilityProbeResult>();
        object? outlook = null;
        object? session = null;
        object? inbox = null;
        object? items = null;
        object? first = null;

        try
        {
            var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
            if (outlookType is null)
            {
                results.Add(CapabilityProbeResult.Failed("outlook-com", "ProgIdUnavailable", CapabilitySeverity.Blocked, new Dictionary<string, string> { ["errorClass"] = "ProgIdUnavailable" }));
                return WithStaticProbes(results);
            }

            results.Add(CapabilityProbeResult.Passed("outlook-progid", "ProgIdRegistered"));
            outlook = Activator.CreateInstance(outlookType);
            if (outlook is null)
            {
                results.Add(CapabilityProbeResult.Failed("outlook-com", "CreateInstanceReturnedNull", CapabilitySeverity.Blocked, new Dictionary<string, string> { ["errorClass"] = "CreateInstanceReturnedNull" }));
                return WithStaticProbes(results);
            }

            results.Add(CapabilityProbeResult.Passed("outlook-com", "ComApplicationAvailable"));
            dynamic outlookDynamic = outlook;
            session = outlookDynamic.Session;
            results.Add(CapabilityProbeResult.Passed("outlook-profile", "DefaultProfileAccessible"));

            dynamic sessionDynamic = session;
            inbox = sessionDynamic.GetDefaultFolder(6); // olFolderInbox
            results.Add(CapabilityProbeResult.Passed("outlook-inbox", "InboxAccessible"));

            dynamic inboxDynamic = inbox;
            items = inboxDynamic.Items;
            int count = Convert.ToInt32(((dynamic)items).Count);
            results.Add(CapabilityProbeResult.Passed("outlook-inbox-count", "InboxCountRead", new Dictionary<string, string> { ["count"] = count.ToString() }));
            results.Add(CapabilityProbeResult.Passed("outlook-polling", "PollingFallbackAvailable", new Dictionary<string, string> { ["feature"] = "polling" }));
            results.Add(CapabilityProbeResult.Warning("outlook-new-mail-event", "EventProbeDeferred", new Dictionary<string, string> { ["feature"] = "NewMailEx", ["enabled"] = "false" }));
            results.Add(CapabilityProbeResult.Warning("outlook-calendar", "CalendarProbeDeferred", new Dictionary<string, string> { ["feature"] = "calendar", ["enabled"] = "false" }));

            if (count > 0)
            {
                first = ((dynamic)items)[1];
                dynamic firstDynamic = first;
                _ = Convert.ToString(firstDynamic.EntryID);
                _ = Convert.ToString(firstDynamic.Subject);
                _ = Convert.ToString(firstDynamic.SenderName);
                _ = firstDynamic.ReceivedTime;
                results.Add(CapabilityProbeResult.Passed("outlook-mail-metadata", "MailMetadataReadable"));

                if (includeBodyProbe)
                {
                    _ = Convert.ToString(firstDynamic.Body);
                    results.Add(CapabilityProbeResult.Passed("outlook-mail-body", "MailBodyReadable"));
                }
                else
                {
                    results.Add(new CapabilityProbeResult("outlook-mail-body", CapabilityStatus.Skipped, CapabilitySeverity.Info, "BodyProbeSkipped", new Dictionary<string, string> { ["enabled"] = "false" }));
                }
            }
            else
            {
                results.Add(CapabilityProbeResult.Warning("outlook-mail-metadata", "InboxEmpty", new Dictionary<string, string> { ["count"] = "0" }));
            }
        }
        catch (Exception ex)
        {
            results.Add(CapabilityProbeResult.Failed("outlook-com-exception", ex.GetType().Name, CapabilitySeverity.Degraded, new Dictionary<string, string> { ["errorClass"] = ex.GetType().Name }));
        }
        finally
        {
            ComRelease.FinalRelease(first);
            ComRelease.FinalRelease(items);
            ComRelease.FinalRelease(inbox);
            ComRelease.FinalRelease(session);
            ComRelease.FinalRelease(outlook);
        }

        return WithStaticProbes(results);
    }

    private static CapabilityReport WithStaticProbes(List<CapabilityProbeResult> results)
    {
        results.Add(CapabilityProbeResult.Passed("notification-capability", "TrayNotificationAvailable", new Dictionary<string, string> { ["feature"] = "tray-notification", ["enabled"] = "true" }));
        results.Add(CapabilityProbeResult.Passed("rule-only-mode", "RuleOnlyModeAvailable", new Dictionary<string, string> { ["feature"] = "rule-only", ["enabled"] = "true" }));
        results.Add(CapabilityProbeResult.Warning("llm-endpoint", "EndpointNotConfigured", new Dictionary<string, string> { ["feature"] = "llm-endpoint", ["enabled"] = "false" }));
        return new CapabilityReport(DateTimeOffset.UtcNow, results);
    }
}
