# ServiceBusExplorer

Interactive console tool for exploring and smoking an **Azure Service Bus** namespace — especially the [local emulator](https://learn.microsoft.com/azure/service-bus-messaging/test-locally-with-service-bus-emulator).

Browse topics and subscriptions, inspect message counts, send JSON messages, and pull the top *N* messages from a subscription.

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Azure Service Bus Emulator running locally (or update the connection strings for a real namespace)

## Quick start

```bash
dotnet run --project ServiceBusExplorer
```

Or open `ServiceBusExplorer.sln` in Visual Studio and run the `ServiceBusExplorer` project.

## What you can do

```
Topics
  └─ pick a topic
       └─ Subscriptions
            └─ pick a subscription
                 ├─ Message counts   (Active / Dead-letter / Transfer)
                 ├─ Send message     (paste your own JSON)
                 └─ Receive top X    (peek-lock receive + complete)
```

Navigation uses numbered menus. Enter `0` to go back or quit.

### Send message

1. Choose **Send message**
2. Paste a JSON body
3. Press Enter on an empty line to finish

Example:

```json
{
  "Id": "user-conn-001",
  "AccountNumber": "pb12345",
  "ConnectionId": "abc123-signalr-connection-id",
  "ConnectionType": 1
}
```

Messages are sent with `ContentType = application/json`. Invalid JSON is rejected before send.

### Receive top X

Prompts for how many messages to receive, then:

- Receives up to that many from the selected subscription
- Prints `MessageId`, `ContentType`, and body
- Completes each message (removes it from the subscription)

If fewer than X messages are available, it receives what it can and stops.

## Configuration

Connection strings are in [`ServiceBusExplorer/appsettings.json`](ServiceBusExplorer/appsettings.json). The file is copied next to the executable on build/publish, so you can change it **without rebuilding**.

```json
{
  "AdminConnectionString": "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
  "DataConnectionString": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
}
```

| Setting | Default endpoint | Purpose |
|---------|------------------|---------|
| `AdminConnectionString` | `sb://localhost:5300` | List topics/subscriptions, message counts |
| `DataConnectionString` | `sb://localhost` | Send and receive messages |

**Where to edit**

- While developing: `ServiceBusExplorer/appsettings.json` (copied on the next run if newer)
- Without rebuild: edit `appsettings.json` beside the built exe, e.g.  
  `ServiceBusExplorer/bin/Debug/net9.0/appsettings.json`  
  or next to a published binary

On startup the app prints the config path it loaded.

## Project layout

```
ServiceBusExplorer.sln
└── ServiceBusExplorer/
    ├── ServiceBusExplorer.csproj
    ├── Program.cs
    └── appsettings.json
```

Depends on [`Azure.Messaging.ServiceBus`](https://www.nuget.org/packages/Azure.Messaging.ServiceBus) only.

## Notes

- **Subscription filters** still apply. If a subscription filters on `ContentType = 'application/json'`, the send path will match. Other filters may drop messages so they never appear on the subscription.
- Counts come from the administration API and can lag slightly on the emulator.
- Receive uses peek-lock and completes messages; they will not remain on the subscription after a successful receive.
