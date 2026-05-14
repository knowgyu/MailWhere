# Assumptions

## A-OUTLOOK-001 Classic Outlook exists

Probe: `Outlook.Application` ProgID and COM application object.  
If false: disable Outlook connector and keep manual mode.

## A-MAIL-001 POP3/exported mail appears as MailItem

Probe: default Inbox and recent item metadata/body.  
If false: use manual selected-text/current-mail fallback.

## A-LLM-001 Local or approved LLM endpoint is available

Probe: endpoint health check in settings.  
If false: rule-based analyzer and review inbox only.

## A-CALENDAR-001 Calendar may not be available

Probe: optional only.  
If false: local reminders and later shadow calendar only.
