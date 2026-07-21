using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

var config = LoadConfig();
var admin = new ServiceBusAdministrationClient(config.AdminConnectionString);
await using var client = new ServiceBusClient(config.DataConnectionString);

Console.WriteLine("ServiceBusExplorer");
Console.WriteLine("=================");
Console.Title = "ServiceBusExplorer";
Console.WriteLine($"Config: {config.ConfigPath}");

while (true)
{
	var topics = await ListTopicsAsync(admin);
	if (topics.Count == 0)
	{
		Console.WriteLine("No topics found. Press Enter to retry, or 0 to quit.");
		var retry = Console.ReadLine();
		if (retry == "0")
			break;
		continue;
	}

	Console.WriteLine();
	Console.WriteLine("Topics:");
	for (var i = 0; i < topics.Count; i++)
		Console.WriteLine($"  [{i + 1}] {topics[i]}");
	Console.WriteLine("  [0] Quit");
	Console.Write("> ");

	if (!TryReadChoice(topics.Count, out var topicIndex))
		continue;
	if (topicIndex == 0)
		break;

	var topicName = topics[topicIndex - 1];
	await RunSubscriptionLoopAsync(admin, client, topicName);
}

Console.WriteLine("Bye.");

static async Task<List<string>> ListTopicsAsync(ServiceBusAdministrationClient admin)
{
	var topics = new List<string>();
	await foreach (var topic in admin.GetTopicsAsync())
		topics.Add(topic.Name);
	topics.Sort(StringComparer.OrdinalIgnoreCase);
	return topics;
}

static async Task<List<string>> ListSubscriptionsAsync(ServiceBusAdministrationClient admin, string topicName)
{
	var subscriptions = new List<string>();
	await foreach (var sub in admin.GetSubscriptionsAsync(topicName))
		subscriptions.Add(sub.SubscriptionName);
	subscriptions.Sort(StringComparer.OrdinalIgnoreCase);
	return subscriptions;
}

static async Task RunSubscriptionLoopAsync(
	ServiceBusAdministrationClient admin,
	ServiceBusClient client,
	string topicName)
{
	while (true)
	{
		var subscriptions = await ListSubscriptionsAsync(admin, topicName);
		Console.WriteLine();
		Console.WriteLine($"Subscriptions on '{topicName}':");
		if (subscriptions.Count == 0)
		{
			Console.WriteLine("  (none)");
			Console.WriteLine("  [0] Back");
			Console.Write("> ");
			_ = Console.ReadLine();
			return;
		}

		for (var i = 0; i < subscriptions.Count; i++)
			Console.WriteLine($"  [{i + 1}] {subscriptions[i]}");
		Console.WriteLine("  [0] Back");
		Console.Write("> ");

		if (!TryReadChoice(subscriptions.Count, out var subIndex))
			continue;
		if (subIndex == 0)
			return;

		var subscriptionName = subscriptions[subIndex - 1];
		await RunActionsLoopAsync(admin, client, topicName, subscriptionName);
	}
}

static async Task RunActionsLoopAsync(
	ServiceBusAdministrationClient admin,
	ServiceBusClient client,
	string topicName,
	string subscriptionName)
{
	while (true)
	{
		Console.WriteLine();
		Console.WriteLine($"{topicName} / {subscriptionName}");
		Console.WriteLine("  [1] Message counts");
		Console.WriteLine("  [2] Send message");
		Console.WriteLine("  [3] Receive top X");
		Console.WriteLine("  [0] Back");
		Console.Write("> ");

		if (!TryReadChoice(3, out var action))
			continue;
		if (action == 0)
			return;

		try
		{
			switch (action)
			{
				case 1:
					await ShowCountsAsync(admin, topicName, subscriptionName);
					break;
				case 2:
					await SendMessageAsync(client, topicName);
					break;
				case 3:
					await ReceiveTopAsync(client, topicName, subscriptionName);
					break;
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
		}
	}
}

static async Task ShowCountsAsync(
	ServiceBusAdministrationClient admin,
	string topicName,
	string subscriptionName)
{
	var runtime = await admin.GetSubscriptionRuntimePropertiesAsync(topicName, subscriptionName);
	Console.WriteLine($"Active:      {runtime.Value.ActiveMessageCount}");
	Console.WriteLine($"Dead-letter: {runtime.Value.DeadLetterMessageCount}");
	Console.WriteLine($"Transfer:    {runtime.Value.TransferMessageCount}");
}

static async Task SendMessageAsync(ServiceBusClient client, string topicName)
{
	Console.WriteLine("Paste JSON body. Finish with an empty line:");
	var json = ReadMultilineInput();
	if (string.IsNullOrWhiteSpace(json))
	{
		Console.WriteLine("Cancelled — no JSON provided.");
		return;
	}

	try
	{
		using var _ = JsonDocument.Parse(json);
	}
	catch (JsonException ex)
	{
		Console.WriteLine($"Invalid JSON: {ex.Message}");
		return;
	}

	await using var sender = client.CreateSender(topicName);
	var message = new ServiceBusMessage(json)
	{
		ContentType = "application/json"
	};

	await sender.SendMessageAsync(message);
	Console.WriteLine($"Sent to '{topicName}':");
	Console.WriteLine(json);
}

static string ReadMultilineInput()
{
	var lines = new List<string>();
	while (true)
	{
		var line = Console.ReadLine();
		if (line is null)
			break;
		if (line.Length == 0)
			break;
		lines.Add(line);
	}

	return string.Join(Environment.NewLine, lines);
}

static async Task ReceiveTopAsync(
	ServiceBusClient client,
	string topicName,
	string subscriptionName)
{
	Console.Write("How many messages (X)? ");
	var input = Console.ReadLine();
	if (!int.TryParse(input, out var max) || max <= 0)
	{
		Console.WriteLine("Enter a positive integer.");
		return;
	}

	await using var receiver = client.CreateReceiver(topicName, subscriptionName);

	var remaining = max;
	var total = 0;

	while (remaining > 0)
	{
		var batchSize = Math.Min(remaining, 100);
		var messages = await receiver.ReceiveMessagesAsync(
			maxMessages: batchSize,
			maxWaitTime: TimeSpan.FromSeconds(2));

		if (messages.Count == 0)
			break;

		foreach (var message in messages)
		{
			total++;
			remaining--;
			Console.WriteLine($"MessageId: {message.MessageId}");
			Console.WriteLine($"ContentType: {message.ContentType}");
			Console.WriteLine($"Body: {message.Body}");
			Console.WriteLine("---");
			await receiver.CompleteMessageAsync(message);

			if (remaining == 0)
				break;
		}
	}

	Console.WriteLine(total == 0
		? "No messages available."
		: $"Received and completed {total} message(s).");
}

static bool TryReadChoice(int maxOption, out int choice)
{
	choice = -1;
	var input = Console.ReadLine();
	if (!int.TryParse(input, out choice) || choice < 0 || choice > maxOption)
	{
		Console.WriteLine("Invalid choice.");
		return false;
	}

	return true;
}

static AppConfig LoadConfig()
{
	var candidates = new[]
	{
		Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
		Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
	};

	var path = candidates.FirstOrDefault(File.Exists)
		?? throw new FileNotFoundException(
			"appsettings.json not found. Place it next to the executable or in the current directory.",
			"appsettings.json");

	var json = File.ReadAllText(path);
	var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
	var file = JsonSerializer.Deserialize<AppSettingsFile>(json, options)
		?? throw new InvalidOperationException($"Could not parse config file: {path}");

	if (string.IsNullOrWhiteSpace(file.AdminConnectionString))
		throw new InvalidOperationException("AdminConnectionString is missing in appsettings.json.");
	if (string.IsNullOrWhiteSpace(file.DataConnectionString))
		throw new InvalidOperationException("DataConnectionString is missing in appsettings.json.");

	return new AppConfig(file.AdminConnectionString, file.DataConnectionString, path);
}

sealed record AppSettingsFile
{
	public string AdminConnectionString { get; init; } = "";
	public string DataConnectionString { get; init; } = "";
}

sealed record AppConfig(string AdminConnectionString, string DataConnectionString, string ConfigPath);
